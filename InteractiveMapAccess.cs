using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using ILogger = SSMP.Logging.ILogger;

namespace CoopMinimap;

/// <summary>
/// The one calibration Interactive Map uses to turn game map positions into its picture's coordinates, so that our
/// markers and its own player icon move together, plus a local correction learnt from the game's own map pins.
/// </summary>
internal static class InteractiveMapAccess {
    private static FieldInfo? _calibratorField;
    private static MapTransformationCalibrator? _fallback;
    private static bool _patched;

    // Local correction: offsets measured at known places (after the affine fit), blended by distance
    private static readonly List<Vector2> CorrectionPoints = new();
    private static readonly List<Vector2> CorrectionOffsets = new();
    private const float CorrectionRadius = 140f;
    private const float FadeWeight = 0.05f;

    public static MapTransformationCalibrator Calibrator {
        get {
            _calibratorField ??= typeof(MapPositionHelper).GetField("_transformationCalibrator",
                BindingFlags.NonPublic | BindingFlags.Static);
            if (_calibratorField?.GetValue(null) is MapTransformationCalibrator calibrator) {
                return calibrator;
            }

            // Make Interactive Map create its own, so both of us use the same one
            MapPositionHelper.InitializeReflection();
            if (_calibratorField?.GetValue(null) is MapTransformationCalibrator created) {
                return created;
            }

            if (_fallback == null) {
                _fallback = new MapTransformationCalibrator();
                _fallback.Calibrate();
            }

            return _fallback;
        }
    }

    /// <summary>Our own position in the picture's coordinates, as Interactive Map last worked it out.</summary>
    public static Vector2 OwnIconPosition => Calibrator.TransformMapToLocal(MapPositionHelper._currentLocalPlayerPosition);

    public static void SetLocalCorrection(List<Vector2> points, List<Vector2> offsets, ILogger logger) {
        CorrectionPoints.Clear();
        CorrectionOffsets.Clear();
        CorrectionPoints.AddRange(points);
        CorrectionOffsets.AddRange(offsets);

        if (_patched) {
            return;
        }

        _patched = true;
        try {
            var original = AccessTools.Method(typeof(MapTransformationCalibrator),
                nameof(MapTransformationCalibrator.TransformMapToLocal));
            new Harmony("coopminimap.calibration").Patch(original,
                postfix: new HarmonyMethod(typeof(InteractiveMapAccess), nameof(TransformPostfix)));
        } catch (Exception e) {
            logger.Error($"CoopMinimap: could not add the local map correction:\n{e}");
        }
    }

    private static void TransformPostfix(ref Vector2 __result) {
        __result += CorrectionAt(__result);
    }

    private static Vector2 CorrectionAt(Vector2 p) => CorrectionFrom(CorrectionPoints, CorrectionOffsets, p, -1);

    /// <summary>
    /// The offsets measured near p, blended by distance and fading out far from all of them; the point at
    /// skipIndex is left out (to measure how well the correction predicts a point it did not see).
    /// </summary>
    public static Vector2 CorrectionFrom(List<Vector2> points, List<Vector2> offsets, Vector2 p, int skipIndex) {
        if (points.Count == 0) {
            return Vector2.zero;
        }

        var sum = Vector2.zero;
        var weights = FadeWeight;
        for (var i = 0; i < points.Count; i++) {
            if (i == skipIndex) {
                continue;
            }

            var d = (p - points[i]).magnitude / CorrectionRadius;
            if (d > 3f) {
                continue;
            }

            var w = Mathf.Exp(-d * d);
            sum += offsets[i] * w;
            weights += w;
        }

        return sum / weights;
    }
}
