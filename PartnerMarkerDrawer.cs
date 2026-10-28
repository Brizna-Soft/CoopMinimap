using System;
using System.Collections.Generic;
using System.Reflection;
using SilkSongMapInteractive;
using SSMP.Api.Client;
using UnityEngine;
using UnityEngine.UI;
using ILogger = SSMP.Logging.ILogger;
using Object = UnityEngine.Object;

namespace CoopMinimap;

/// <summary>
/// Draws every other connected player on each of Interactive Map's map views (the minimap and the full map), as a
/// small Hornet head in the partner's colour with their name. On the minimap, a partner or the current main objective
/// that is out of view gets an arrow on the edge pointing at them, and the current main objective is named above it.
/// The partner's position comes from their body while they are in our room, and otherwise from the map position SSMP
/// shares (which SSMP only sends while that player wears the compass, unless the host turned on global map icons).
/// </summary>
internal class PartnerMarkerDrawer : MonoBehaviour {
    private const float UpdateInterval = 0.1f;
    private const string OwnMarkerName = "PlayerMarker";
    private const float MinimapHeadSize = 26f;
    private const float EdgeMargin = 13f;

    private IClientApi _api = null!;
    private ILogger _logger = null!;

    // Per map view (by instance id), per player id
    private readonly Dictionary<int, Dictionary<ushort, GameObject>> _markers = new();

    // Edge arrows of the minimap, by key ("p<id>" for a partner, "objective")
    private readonly Dictionary<string, GameObject> _edges = new();
    private Text? _caption;

    private Sprite? _head;
    private float _nextUpdate;
    private float _nextDebugLog;
    private bool _loggedFirstDraw;

    private MethodInfo? _getMapPosition;
    private FieldInfo? _currentScene;
    private FieldInfo? _currentSceneObj;
    private FieldInfo? _currentScenePos;
    private FieldInfo? _currentSceneSize;

    public void Setup(IClientApi api, ILogger logger) {
        _api = api;
        _logger = logger;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var gameMapType = typeof(GameMap);
        _getMapPosition = gameMapType.GetMethod("GetMapPosition", flags, null,
            [typeof(Vector2), typeof(GameMapScene), typeof(GameObject), typeof(Vector2), typeof(Vector2)], null);
        _currentScene = gameMapType.GetField("currentScene", flags);
        _currentSceneObj = gameMapType.GetField("currentSceneObj", flags);
        _currentScenePos = gameMapType.GetField("currentScenePos", flags);
        _currentSceneSize = gameMapType.GetField("currentSceneSize", flags);

        if (_getMapPosition == null || _currentScene == null) {
            _logger.Warn("CoopMinimap: could not find GameMap members, same-room positions will not work");
        }
    }

    private void Update() {
        if (Time.unscaledTime < _nextUpdate) {
            return;
        }

        _nextUpdate = Time.unscaledTime + UpdateInterval;

        try {
            Refresh();
        } catch (Exception e) {
            _logger.Error($"CoopMinimap: refresh failed:\n{e}");
            // Back off so a persistent error does not flood the log
            _nextUpdate = Time.unscaledTime + 5f;
        }
    }

    private void Refresh() {
        var clientManager = _api.ClientManager;
        var calibrator = InteractiveMapAccess.Calibrator;
        var renderers = Object.FindObjectsByType<MapRenderer>(FindObjectsSortMode.None);

        // Forget views that no longer exist
        var alive = new HashSet<int>();
        foreach (var renderer in renderers) {
            alive.Add(renderer.GetInstanceID());
        }

        var gone = new List<int>();
        foreach (var key in _markers.Keys) {
            if (!alive.Contains(key)) {
                gone.Add(key);
            }
        }

        foreach (var key in gone) {
            _markers.Remove(key);
        }

        // Our own map position worked out the same way as a partner's, so that a partner in our room can be placed
        // relative to our own icon: any error left in the calibration is then the same for both and cancels out
        var hero = HeroController.instance;
        var hasOwn = false;
        var ownLocal = Vector2.zero;
        if (hero != null && TryGetLocalSceneMapPosition(hero.transform.position, out var ownMap)) {
            hasOwn = true;
            ownLocal = calibrator.TransformMapToLocal(ownMap);
        }

        // Where each partner is in Interactive Map's coordinates, or absent when unknown
        var positions = new Dictionary<ushort, Vector2>();
        var names = new Dictionary<ushort, string>();
        foreach (var player in clientManager.Players) {
            if (player.Username == clientManager.Username) {
                continue;
            }

            if (!TryGetPartnerMapPosition(player, out var mapPos, out var sameRoom)) {
                continue;
            }

            var partnerLocal = calibrator.TransformMapToLocal(mapPos);
            if (sameRoom && hasOwn) {
                partnerLocal = InteractiveMapAccess.OwnIconPosition + (partnerLocal - ownLocal);
            }

            positions[player.Id] = partnerLocal;
            names[player.Id] = player.Username;

            if (sameRoom && hasOwn && Time.unscaledTime >= _nextDebugLog) {
                _nextDebugLog = Time.unscaledTime + 60f;
                _logger.Info(
                    $"CoopMinimap: hero world {(Vector2) hero!.transform.position}, {player.Username} world " +
                    $"{(Vector2) player.PlayerObject!.transform.position}; own icon {InteractiveMapAccess.OwnIconPosition}, " +
                    $"partner drawn at {partnerLocal}");
            }
        }

        foreach (var renderer in renderers) {
            var content = renderer._mapContent;
            if (content == null) {
                continue;
            }

            var isMinimap = renderer is MinimapUI;
            var headSize = RestyleOwnMarker(content, isMinimap);

            var id = renderer.GetInstanceID();
            if (!_markers.TryGetValue(id, out var perPlayer)) {
                _markers[id] = perPlayer = new Dictionary<ushort, GameObject>();
            }

            // Hide markers of players without a known position or who left
            foreach (var entry in perPlayer) {
                if (entry.Value != null && !positions.ContainsKey(entry.Key)) {
                    entry.Value.SetActive(false);
                }
            }

            foreach (var pos in positions) {
                if (!perPlayer.TryGetValue(pos.Key, out var marker) || marker == null) {
                    perPlayer[pos.Key] = marker = CreatePartnerMarker(content, names[pos.Key], headSize);
                }

                marker.SetActive(true);
                marker.GetComponent<RectTransform>().anchoredPosition = pos.Value;
                // Keep it above location markers and the player's own icon
                marker.transform.SetAsLastSibling();
            }

            if (isMinimap) {
                UpdateMinimapEdges(content, positions, names);
            }
        }

        if (!_loggedFirstDraw && positions.Count > 0 && renderers.Length > 0) {
            _loggedFirstDraw = true;
            _logger.Info($"CoopMinimap: drawing {positions.Count} partner(s) on {renderers.Length} map view(s)");
        }
    }

    /// <summary>
    /// Arrows on the minimap's edge for partners and the main objective that are out of view, and the main
    /// objective's name next to the minimap.
    /// </summary>
    private void UpdateMinimapEdges(RectTransform content, Dictionary<ushort, Vector2> positions,
        Dictionary<ushort, string> names) {
        if (content.parent is not RectTransform viewport) {
            return;
        }

        var shown = new HashSet<string>();
        foreach (var pos in positions) {
            var key = "p" + pos.Key;
            shown.Add(key);
            PlaceEdge(viewport, content, key, pos.Value, UiParts.PartnerColour, names[pos.Key]);
        }

        var main = ObjectiveHighlighter.CurrentMain;
        if (main != null) {
            shown.Add("objective");
            PlaceEdge(viewport, content, "objective", MapPositionHelper.ConvertLocationCoordinatesToMapPosition(main),
                UiParts.MainObjectiveColour, null);
        }

        foreach (var edge in _edges) {
            if (!shown.Contains(edge.Key) && edge.Value != null) {
                edge.Value.SetActive(false);
            }
        }

        UpdateCaption(viewport, main);
    }

    private void PlaceEdge(RectTransform viewport, RectTransform content, string key, Vector2 local, Color colour,
        string? label) {
        if (!_edges.TryGetValue(key, out var edge) || edge == null || edge.transform.parent != viewport) {
            _edges[key] = edge = CreateEdge(viewport, key, colour, label);
        }

        // Where the target is in the minimap's window, from its centre, through the actual transforms (so it holds
        // whatever the anchors, pivots and zoom of Interactive Map's minimap are)
        var rect = viewport.rect;
        var inView = (Vector2) viewport.InverseTransformPoint(content.TransformPoint(local)) - rect.center;
        var half = rect.size * 0.5f - new Vector2(EdgeMargin, EdgeMargin);
        if (half.x <= 0f || half.y <= 0f || (Mathf.Abs(inView.x) <= half.x && Mathf.Abs(inView.y) <= half.y)) {
            edge.SetActive(false);
            return;
        }

        var t = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(inView.x), 0.001f),
            half.y / Mathf.Max(Mathf.Abs(inView.y), 0.001f));
        var onEdge = inView * t;
        edge.GetComponent<RectTransform>().anchoredPosition = onEdge;

        var arrow = edge.transform.Find("Arrow");
        if (arrow != null) {
            arrow.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(inView.y, inView.x) * Mathf.Rad2Deg - 90f);
        }

        var name = edge.transform.Find("Label");
        if (name != null) {
            // Put the name on the inner side of the arrow, so it stays inside the minimap
            name.GetComponent<RectTransform>().anchoredPosition = -onEdge.normalized * 18f;
        }

        edge.SetActive(true);
        edge.transform.SetAsLastSibling();
    }

    private static GameObject CreateEdge(RectTransform viewport, string key, Color colour, string? label) {
        var edge = new GameObject("CoopEdge_" + key, typeof(RectTransform));
        edge.transform.SetParent(viewport, false);
        edge.GetComponent<RectTransform>().sizeDelta = new Vector2(20f, 20f);

        var arrow = new GameObject("Arrow", typeof(RectTransform));
        arrow.transform.SetParent(edge.transform, false);
        arrow.GetComponent<RectTransform>().sizeDelta = new Vector2(20f, 20f);
        UiParts.AddImage(arrow, UiParts.Arrow, colour);
        UiParts.AddRim(arrow);

        if (label != null) {
            var text = UiParts.AddLabel(edge.transform, "Label", 12, colour, Vector2.zero);
            if (text != null) {
                text.text = label;
            }
        }

        return edge;
    }

    private void UpdateCaption(RectTransform viewport, MapLocation? main) {
        if (viewport.parent is not RectTransform container) {
            return;
        }

        if (_caption == null || _caption.transform.parent != container) {
            _caption = UiParts.AddLabel(container, "CoopObjectiveCaption", 14, UiParts.MainObjectiveColour,
                Vector2.zero);
            if (_caption == null) {
                return;
            }
        }

        // Above the minimap when it sits low on the screen, below it when it sits high
        var rt = _caption.rectTransform;
        var low = container.anchorMin.y < 0.5f;
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, low ? 1f : 0f);
        rt.pivot = new Vector2(0.5f, low ? 0f : 1f);
        rt.anchoredPosition = new Vector2(0f, low ? 4f : -4f);

        _caption.text = main != null ? Lang.T("Principal: ", "Main: ") + main.title : "";
        _caption.gameObject.SetActive(main != null);
    }

    private bool TryGetPartnerMapPosition(IClientPlayer player, out Vector2 mapPos, out bool sameRoom) {
        mapPos = Vector2.zero;
        sameRoom = false;

        // In our room: work it out from their body, the way the game does for our own icon
        if (player.IsInLocalScene && player.PlayerObject != null && TryGetLocalSceneMapPosition(
                player.PlayerObject.transform.position, out mapPos)) {
            sameRoom = true;
            return true;
        }

        // Elsewhere: the map position SSMP received for them, if they share one
        var mapManager = _api.ClientManager.MapManager;
        if (mapManager.TryGetEntry(player.Id, out var entry) && entry != null && entry.HasMapIcon) {
            mapPos = new Vector2(entry.Position.X, entry.Position.Y);
            return true;
        }

        return false;
    }

    private bool TryGetLocalSceneMapPosition(Vector2 worldPos, out Vector2 mapPos) {
        mapPos = Vector2.zero;
        if (_getMapPosition == null || _currentScene == null || _currentSceneObj == null ||
            _currentScenePos == null || _currentSceneSize == null) {
            return false;
        }

        var gm = GameManager.instance;
        if (gm == null || gm.gameMap == null) {
            return false;
        }

        var gameMap = gm.gameMap;
        // Interactive Map calls UpdateCurrentScene every refresh for our own icon, so these fields are current
        var scene = _currentScene.GetValue(gameMap);
        var sceneObj = _currentSceneObj.GetValue(gameMap);
        if (scene == null || sceneObj == null) {
            return false;
        }

        var result = _getMapPosition.Invoke(gameMap, [
            worldPos,
            scene,
            sceneObj,
            _currentScenePos.GetValue(gameMap),
            _currentSceneSize.GetValue(gameMap)
        ]);
        mapPos = (Vector2) result;
        return true;
    }

    /// <summary>
    /// On the minimap, make Interactive Map's own head icon smaller, so where it sits is easier to read; returns the
    /// head size used on this map view.
    /// </summary>
    private static float RestyleOwnMarker(RectTransform content, bool isMinimap) {
        var own = content.Find(OwnMarkerName);
        if (own == null || !own.TryGetComponent<RectTransform>(out var rt)) {
            return isMinimap ? MinimapHeadSize : 42f;
        }

        if (isMinimap && rt.sizeDelta.x > MinimapHeadSize + 0.5f) {
            rt.sizeDelta = new Vector2(MinimapHeadSize, MinimapHeadSize);
        }

        return rt.sizeDelta.x;
    }

    private GameObject CreatePartnerMarker(RectTransform parent, string name, float size) {
        var go = new GameObject("CoopPartnerMarker", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(size, size);

        _head ??= IconLoader.LoadCategoryIcon("me") ?? UiParts.Dot;
        UiParts.AddImage(go, _head, UiParts.PartnerColour);
        UiParts.AddRim(go);

        var label = UiParts.AddLabel(go.transform, "Name", 12, UiParts.PartnerColour, new Vector2(0f, size * 0.5f + 8f));
        if (label != null) {
            label.text = name;
        }

        return go;
    }
}
