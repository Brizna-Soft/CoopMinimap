using System;
using System.Collections.Generic;
using System.Linq;
using SilkSongMapInteractive;
using UnityEngine;
using ILogger = SSMP.Logging.ILogger;

namespace CoopMinimap;

/// <summary>
/// Interactive Map places the player with a fit through 22 hand-picked points, which is off by up to ~25 units in
/// places. The game's own map has pins with exact positions (benches, stations, vendors) and Interactive Map has
/// markers for the same places, so pairing them gives many exact reference points across the whole map. From those
/// this fits a better transform, applies it to Interactive Map's calibration, and adds a local correction for what
/// a single transform cannot fix.
/// </summary>
internal class MapCalibrationImprover : MonoBehaviour {
    // Interactive Map categories with a counterpart pin on the game's own map
    private static readonly int[] ReferenceCategories = [
        13533, // Bench
        13535, // Bellway Station
        13539, // Ventrica Station
        13534 // Vendor
    ];

    private ILogger _logger = null!;
    private GameMap? _doneFor;
    private float _nextTry;

    public void Setup(ILogger logger) {
        _logger = logger;
    }

    private void Update() {
        if (Time.unscaledTime < _nextTry) {
            return;
        }

        _nextTry = Time.unscaledTime + 2f;

        var gm = GameManager.instance;
        if (gm == null || gm.gameMap == null || MapRenderer._mapData?.locations == null) {
            return;
        }

        if (_doneFor == gm.gameMap) {
            return;
        }

        _doneFor = gm.gameMap;
        try {
            Improve(gm.gameMap);
        } catch (Exception e) {
            _logger.Error($"CoopMinimap: calibration failed, keeping Interactive Map's own:\n{e}");
        }
    }

    private void Improve(GameMap gameMap) {
        var mapTransform = gameMap.transform;

        // Where each pin (or the row of pins it sits in) is, in the game map's own coordinates
        var anchors = new List<Vector2>();
        var seen = new HashSet<Transform>();
        foreach (var pin in gameMap.GetComponentsInChildren<MapPin>(true)) {
            var layout = pin.GetComponentInParent<GameMapPinLayout>(true);
            var anchor = layout != null ? layout.transform : pin.transform;
            if (seen.Add(anchor)) {
                anchors.Add(mapTransform.InverseTransformPoint(anchor.position));
            }
        }

        var markers = MapRenderer._mapData.locations
            .Where(l => Array.IndexOf(ReferenceCategories, l.category_id) >= 0)
            .Select(MapPositionHelper.ConvertLocationCoordinatesToMapPosition)
            .ToList();

        var calibrator = InteractiveMapAccess.Calibrator;
        var original = new Affine(calibrator.aX, calibrator.bX, calibrator.cX, calibrator.aY, calibrator.bY,
            calibrator.cY);

        var pairs = Match(anchors, markers, original, 70f);
        if (pairs.Count < 10) {
            _logger.Info($"CoopMinimap: calibration skipped, only {pairs.Count} pin/marker pairs " +
                         $"({anchors.Count} pins, {markers.Count} markers)");
            return;
        }

        var fit = Affine.Fit(pairs) ?? original;
        foreach (var radius in new[] { 40f, 25f, 20f }) {
            pairs = Match(anchors, markers, fit, radius);
            fit = FitWithoutOutliers(pairs) ?? fit;
        }

        LogCompassSpace(gameMap);

        // The original calibration never saw these pairs, so its error on them is a fair measure; the new fit is
        // measured on each pair left out of its own fit, so it cannot look better just by fitting its own points
        var before = Errors(pairs, original);
        var heldOut = HeldOutErrors(pairs);
        _logger.Info($"CoopMinimap: calibration with {pairs.Count} pairs ({anchors.Count} pins, {markers.Count} " +
                     $"markers): error before mean {before.mean:F1} max {before.max:F1}, new fit on left-out pairs " +
                     $"mean {heldOut.mean:F1} max {heldOut.max:F1}");

        var finite = !float.IsNaN(fit.AX + fit.BX + fit.CX + fit.AY + fit.BY + fit.CY) &&
                     !float.IsInfinity(fit.AX + fit.BX + fit.CX + fit.AY + fit.BY + fit.CY);
        if (pairs.Count < 10 || !finite || heldOut.mean >= before.mean * 0.85f ||
            heldOut.max > Mathf.Max(before.max, 30f)) {
            _logger.Info("CoopMinimap: the new fit is not clearly better, keeping Interactive Map's calibration");
            return;
        }

        calibrator.aX = fit.AX;
        calibrator.bX = fit.BX;
        calibrator.cX = fit.CX;
        calibrator.aY = fit.AY;
        calibrator.bY = fit.BY;
        calibrator.cY = fit.CY;

        // What is left after the fit, near each pair, can be corrected locally (capped, so one odd pair cannot pull
        // much); only used when leaving each pair out shows that it helps the pairs it did not see
        var points = new List<Vector2>();
        var offsets = new List<Vector2>();
        foreach (var (map, local) in pairs) {
            var placed = fit.Apply(map);
            points.Add(placed);
            offsets.Add(Vector2.ClampMagnitude(local - placed, 40f));
        }

        double withoutLocal = 0, withLocal = 0;
        for (var i = 0; i < pairs.Count; i++) {
            withoutLocal += offsets[i].magnitude;
            var correction = InteractiveMapAccess.CorrectionFrom(points, offsets, points[i], i);
            withLocal += (offsets[i] - correction).magnitude;
        }

        withoutLocal /= pairs.Count;
        withLocal /= pairs.Count;
        var useLocal = withLocal < withoutLocal * 0.9;
        _logger.Info($"CoopMinimap: local correction on left-out pairs: mean {withoutLocal:F1} -> {withLocal:F1}, " +
                     (useLocal ? "used" : "not used"));

        InteractiveMapAccess.SetLocalCorrection(useLocal ? points : new List<Vector2>(),
            useLocal ? offsets : new List<Vector2>(), _logger);
        MapPositionHelper.RefreshSceneCache();
        _logger.Info("CoopMinimap: improved calibration applied");
    }

    private static (float mean, float max) HeldOutErrors(List<(Vector2 map, Vector2 local)> pairs) {
        var errors = new List<float>();
        for (var i = 0; i < pairs.Count; i++) {
            var rest = new List<(Vector2 map, Vector2 local)>(pairs);
            rest.RemoveAt(i);
            var fit = Affine.Fit(rest);
            if (fit != null) {
                errors.Add((fit.Value.Apply(pairs[i].map) - pairs[i].local).magnitude);
            }
        }

        return errors.Count == 0 ? (float.MaxValue, float.MaxValue) : (errors.Average(), errors.Max());
    }

    /// <summary>
    /// Pins are measured in the game map's own space; the game draws the compass head with GetMapPosition in its
    /// parent's local space. Logs whether that parent is the game map itself, which is what makes the two agree.
    /// </summary>
    private void LogCompassSpace(GameMap gameMap) {
        try {
            var field = typeof(GameMap).GetField("compassIcon",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            if (field?.GetValue(gameMap) is GameObject compass) {
                var parent = compass.transform.parent;
                _logger.Info($"CoopMinimap: compass icon parent is '{(parent != null ? parent.name : "none")}', " +
                             $"the game map itself: {parent == gameMap.transform}, parent scale " +
                             $"{(parent != null ? parent.localScale : Vector3.one)}");
            }
        } catch (Exception e) {
            _logger.Info($"CoopMinimap: could not check the compass icon: {e.Message}");
        }
    }

    private static List<(Vector2 map, Vector2 local)> Match(List<Vector2> anchors, List<Vector2> markers,
        Affine transform, float radius) {
        // For each marker, the closest pin that clearly points at it and at nothing else
        var best = new Dictionary<int, (int anchor, float distance)>();
        for (var a = 0; a < anchors.Count; a++) {
            var predicted = transform.Apply(anchors[a]);
            var nearest = -1;
            var d1 = float.MaxValue;
            var d2 = float.MaxValue;
            for (var m = 0; m < markers.Count; m++) {
                var d = (markers[m] - predicted).magnitude;
                if (d < d1) {
                    d2 = d1;
                    d1 = d;
                    nearest = m;
                } else if (d < d2) {
                    d2 = d;
                }
            }

            if (nearest < 0 || d1 > radius || d2 < d1 * 1.6f) {
                continue;
            }

            if (!best.TryGetValue(nearest, out var current) || d1 < current.distance) {
                best[nearest] = (a, d1);
            }
        }

        return best.Select(kv => (anchors[kv.Value.anchor], markers[kv.Key])).ToList();
    }

    private static Affine? FitWithoutOutliers(List<(Vector2 map, Vector2 local)> pairs) {
        var fit = Affine.Fit(pairs);
        if (fit == null) {
            return null;
        }

        for (var round = 0; round < 2; round++) {
            var residuals = pairs.Select(p => (fit.Value.Apply(p.map) - p.local).magnitude).ToList();
            var sorted = residuals.OrderBy(r => r).ToList();
            var median = sorted[sorted.Count / 2];
            var limit = Mathf.Max(median * 2.5f, 8f);
            var kept = pairs.Where((_, i) => residuals[i] <= limit).ToList();
            if (kept.Count < 10) {
                break;
            }

            fit = Affine.Fit(kept) ?? fit;
        }

        return fit;
    }

    private static (float mean, float max) Errors(List<(Vector2 map, Vector2 local)> pairs, Affine transform) {
        if (pairs.Count == 0) {
            return (0f, 0f);
        }

        var errors = pairs.Select(p => (transform.Apply(p.map) - p.local).magnitude).ToList();
        return (errors.Average(), errors.Max());
    }

    private readonly struct Affine {
        public readonly float AX, BX, CX, AY, BY, CY;

        public Affine(float ax, float bx, float cx, float ay, float by, float cy) {
            AX = ax;
            BX = bx;
            CX = cx;
            AY = ay;
            BY = by;
            CY = cy;
        }

        public Vector2 Apply(Vector2 p) => new(AX * p.x + BX * p.y + CX, AY * p.x + BY * p.y + CY);

        /// <summary>Least squares fit of x' = ax + by + c and y' = dx + ey + f.</summary>
        public static Affine? Fit(List<(Vector2 map, Vector2 local)> pairs) {
            if (pairs.Count < 3) {
                return null;
            }

            double sxx = 0, sxy = 0, sx = 0, syy = 0, sy = 0, n = 0;
            double ux = 0, uy = 0, u = 0, vx = 0, vy = 0, v = 0;
            foreach (var (map, local) in pairs) {
                double x = map.x, y = map.y;
                sxx += x * x;
                sxy += x * y;
                sx += x;
                syy += y * y;
                sy += y;
                n += 1;
                ux += local.x * x;
                uy += local.x * y;
                u += local.x;
                vx += local.y * x;
                vy += local.y * y;
                v += local.y;
            }

            var m = new[,] { { sxx, sxy, sx }, { sxy, syy, sy }, { sx, sy, n } };
            var cx = Solve(m, [ux, uy, u]);
            var cy = Solve(m, [vx, vy, v]);
            if (cx == null || cy == null) {
                return null;
            }

            return new Affine((float) cx[0], (float) cx[1], (float) cx[2], (float) cy[0], (float) cy[1],
                (float) cy[2]);
        }

        private static double[]? Solve(double[,] a, double[] b) {
            double Det(double[,] m) =>
                m[0, 0] * (m[1, 1] * m[2, 2] - m[1, 2] * m[2, 1]) -
                m[0, 1] * (m[1, 0] * m[2, 2] - m[1, 2] * m[2, 0]) +
                m[0, 2] * (m[1, 0] * m[2, 1] - m[1, 1] * m[2, 0]);

            var det = Det(a);
            if (Math.Abs(det) < 1e-9) {
                return null;
            }

            var result = new double[3];
            for (var col = 0; col < 3; col++) {
                var replaced = (double[,]) a.Clone();
                for (var row = 0; row < 3; row++) {
                    replaced[row, col] = b[row];
                }

                result[col] = Det(replaced) / det;
            }

            return result;
        }
    }
}
