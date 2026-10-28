using System;
using System.Collections.Generic;
using System.Reflection;
using GlobalEnums;
using TMProOld;
using UnityEngine;
using UnityEngine.UI;
using ILogger = SSMP.Logging.ILogger;
using Object = UnityEngine.Object;

namespace CoopMinimap;

/// <summary>
/// What the game's quick map draws over the rooms, copied onto the game-map minimap: the map pins (benches, shops and
/// the rest), quest markers, the markers the player placed, and the names of the next areas at the zone's exits. Each
/// shows under the rules the game uses for its quick map, at the same place and size, with the same pictures and font.
/// </summary>
internal class MinimapDecorations {
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;

    // The game's own visibility rules, private in its classes
    private static readonly MethodInfo? PinCanBeActive = typeof(MapPin).GetMethod("CanBeActive", Flags);
    private static readonly MethodInfo? QuestIsActive =
        typeof(QuestMapMarker).GetMethod("IsActive", Flags, null, [typeof(bool), typeof(MapZone)], null);
    private static readonly FieldInfo? QuestInitialPos = typeof(MapMarkerArrow).GetField("initialPos", Flags);
    private static readonly FieldInfo? PinCondition = typeof(MapPinConditional).GetField("visibleCondition", Flags);
    private static readonly FieldInfo? NextVisitedBool = typeof(MapNextAreaDisplay).GetField("visitedString", Flags);
    private static readonly FieldInfo? NextCondition = typeof(MapNextAreaDisplay).GetField("visibleCondition", Flags);
    private static readonly FieldInfo? NextSceneVisited = typeof(MapNextAreaDisplay).GetField("sceneVisited", Flags);
    private static readonly FieldInfo? MarkerTemplates = typeof(GameMap).GetField("mapMarkerTemplates", Flags);
    private static readonly FieldInfo? MarkerParent = typeof(GameMap).GetField("markerParent", Flags);
    private static readonly FieldInfo? TextToSingleLine = typeof(SetTextMeshProGameText).GetField("toSingleLine", Flags);

    private readonly ILogger _logger;
    private readonly RectTransform _container;
    private readonly List<GameObject> _made = new();
    private int _signature;

    private GameMap? _cachedFor;
    private MapPin[] _pins = [];
    private QuestMapMarker[] _questMarkers = [];
    private MapNextAreaDisplay[] _nextAreas = [];

    // What is visible now, gathered every refresh
    private readonly List<MapPin> _shownPins = new();
    private readonly List<(QuestMapMarker marker, Vector3 offset)> _shownQuests = new();
    private readonly List<MapNextAreaDisplay> _shownNextAreas = new();
    private readonly List<(GameObject template, Vector3 offset)> _shownPlaced = new();

    public MinimapDecorations(RectTransform content, ILogger logger) {
        _logger = logger;
        var container = new GameObject("Decorations", typeof(RectTransform));
        container.transform.SetParent(content, false);
        _container = container.GetComponent<RectTransform>();
        _container.sizeDelta = Vector2.zero;

        // Without these the matching pieces are left out; say which, in case the game changed
        var missing = new List<string>();
        if (PinCanBeActive == null) missing.Add("MapPin.CanBeActive");
        if (QuestIsActive == null) missing.Add("QuestMapMarker.IsActive");
        if (QuestInitialPos == null) missing.Add("MapMarkerArrow.initialPos");
        if (NextVisitedBool == null || NextCondition == null || NextSceneVisited == null) missing.Add("MapNextAreaDisplay");
        if (MarkerTemplates == null || MarkerParent == null) missing.Add("GameMap markers");
        if (missing.Count > 0) {
            _logger.Warn("CoopMinimap: the game changed, the minimap leaves out: " + string.Join(", ", missing));
        }
    }

    /// <summary>Keeps the decorations drawn over the rooms, which are rebuilt into the same content.</summary>
    public void BringToFront() {
        _container.SetAsLastSibling();
    }

    /// <summary>
    /// Works out what the quick map would show now and redraws it when that changed. isAreaShown tells whether an
    /// object's map area is drawn on the minimap (its second argument: only the current zone's).
    /// </summary>
    public void Refresh(GameMap gameMap, MapZone zone, Func<Transform, bool, bool> isAreaShown, bool zoneOnly) {
        if (_cachedFor != gameMap) {
            _cachedFor = gameMap;
            _pins = gameMap.GetComponentsInChildren<MapPin>(true);
            _questMarkers = gameMap.GetComponentsInChildren<QuestMapMarker>(true);
            _nextAreas = gameMap.GetComponentsInChildren<MapNextAreaDisplay>(true);
            _signature = 0;
        }

        var signature = 17;

        _shownPins.Clear();
        foreach (var pin in _pins) {
            if (pin != null && isAreaShown(pin.transform, zoneOnly) && PinVisible(pin)) {
                _shownPins.Add(pin);
                signature = signature * 31 + pin.GetInstanceID();
                // The map lays pins out around each other, so they can move
                signature = signature * 31 + pin.transform.localPosition.GetHashCode();
            }
        }

        // Quest markers sit at their own place on the quick map (on the world map they slide to its edge)
        _shownQuests.Clear();
        foreach (var marker in _questMarkers) {
            if (marker == null || !isAreaShown(marker.transform, zoneOnly) || !QuestVisible(marker, zone) ||
                QuestInitialPos?.GetValue(marker) is not Vector2 initial || marker.transform.parent == null) {
                continue;
            }

            var offset = marker.transform.parent.TransformPoint(initial) - marker.transform.position;
            _shownQuests.Add((marker, offset));
            signature = signature * 31 + marker.GetInstanceID();
            signature = signature * 31 + initial.GetHashCode();
        }

        // Next areas' names: the quick map shows only the current zone's
        _shownNextAreas.Clear();
        var playerData = PlayerData.instance;
        if (playerData != null) {
            foreach (var label in _nextAreas) {
                if (label != null && isAreaShown(label.transform, true) && NextAreaVisible(label, playerData)) {
                    _shownNextAreas.Add(label);
                    signature = signature * 31 + label.GetInstanceID();
                    signature = signature * 31 + label.transform.localPosition.GetHashCode();
                }
            }
        }

        GatherPlacedMarkers(gameMap, ref signature);
        if (signature == _signature && _made.Count > 0) {
            return;
        }

        _signature = signature;
        Rebuild(gameMap.transform);
    }

    private void GatherPlacedMarkers(GameMap gameMap, ref int signature) {
        _shownPlaced.Clear();
        var playerData = PlayerData.instance;
        if (playerData?.placedMarkers == null || CollectableItemManager.IsInHiddenMode() ||
            MarkerTemplates?.GetValue(gameMap) is not GameObject[] templates ||
            MarkerParent?.GetValue(gameMap) is not Transform parent || parent == null) {
            return;
        }

        // As GameMap.SetupMapMarkers places them: each saved position, in the markers' parent, with that type's look
        for (var type = 0; type < templates.Length && type < playerData.placedMarkers.Length; type++) {
            var positions = playerData.placedMarkers[type]?.List;
            var template = templates[type];
            if (positions == null || template == null) {
                continue;
            }

            foreach (var position in positions) {
                var offset = parent.TransformPoint(position) - template.transform.position;
                _shownPlaced.Add((template, offset));
                signature = signature * 31 + type;
                signature = signature * 31 + position.GetHashCode();
            }
        }
    }

    private void Rebuild(Transform map) {
        foreach (var made in _made) {
            if (made != null) {
                Object.Destroy(made);
            }
        }

        _made.Clear();
        foreach (var pin in _shownPins) {
            CopyPictures(pin.gameObject, Vector3.zero, map);
        }

        foreach (var (marker, offset) in _shownQuests) {
            CopyPictures(marker.gameObject, offset, map);
        }

        foreach (var (template, offset) in _shownPlaced) {
            CopyPictures(template, offset, map);
        }

        foreach (var label in _shownNextAreas) {
            CopyPictures(label.gameObject, Vector3.zero, map);
        }

        BringToFront();
        _logger.Info($"CoopMinimap: minimap shows {_shownPins.Count} map pins, {_shownQuests.Count} quest markers, " +
                     $"{_shownPlaced.Count} placed markers and {_shownNextAreas.Count} next-area names");
    }

    /// <summary>Copies an object's pictures and texts (those its own children show) at its place on the map.</summary>
    private void CopyPictures(GameObject source, Vector3 worldOffset, Transform map) {
        foreach (var renderer in source.GetComponentsInChildren<SpriteRenderer>(true)) {
            if (renderer.sprite != null && ShownBelow(renderer.transform, source.transform)) {
                AddSprite(renderer, worldOffset, map);
            }
        }

        foreach (var text in source.GetComponentsInChildren<TMP_Text>(true)) {
            if (ShownBelow(text.transform, source.transform)) {
                AddText(text, worldOffset, map);
            }
        }
    }

    private void AddSprite(SpriteRenderer renderer, Vector3 worldOffset, Transform map) {
        var sprite = renderer.sprite;
        var go = new GameObject("Pic_" + renderer.name, typeof(RectTransform));
        go.transform.SetParent(_container, false);
        var rt = go.GetComponent<RectTransform>();
        var scale = RelativeScale(renderer.transform, map);
        rt.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
        rt.anchoredPosition = (Vector2) map.InverseTransformPoint(renderer.transform.position + worldOffset) *
                              VanillaMinimap.BaseUnit;
        rt.sizeDelta = new Vector2(sprite.bounds.size.x * Mathf.Abs(scale.x), sprite.bounds.size.y * Mathf.Abs(scale.y)) *
                       VanillaMinimap.BaseUnit;
        rt.localScale = new Vector3(Mathf.Sign(scale.x) * (renderer.flipX ? -1f : 1f),
            Mathf.Sign(scale.y) * (renderer.flipY ? -1f : 1f), 1f);
        rt.localEulerAngles = new Vector3(0f, 0f, renderer.transform.eulerAngles.z - map.eulerAngles.z);

        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        // Full strength, as for the rooms: the map fades its pictures in when it opens
        var colour = renderer.color;
        colour.a = 1f;
        image.color = colour;
        image.raycastTarget = false;
        _made.Add(go);
    }

    private void AddText(TMP_Text source, Vector3 worldOffset, Transform map) {
        var words = TextOf(source);
        if (string.IsNullOrEmpty(words) || source.font == null) {
            return;
        }

        var go = new GameObject("Text_" + source.name, typeof(RectTransform));
        go.transform.SetParent(_container, false);
        var text = go.AddComponent<TextMeshProUGUI>();
        text.font = source.font;
        // The label's own look (outline and the rest) when its shader also draws on a canvas
        var material = source.fontSharedMaterial;
        if (material != null && material.shader != null && material.shader.name.Contains("TextMeshPro") &&
            !material.shader.name.Contains("Surface")) {
            text.fontSharedMaterial = material;
        }
        text.fontStyle = source.fontStyle;
        text.alignment = source.alignment;
        text.enableWordWrapping = source.enableWordWrapping;
        text.characterSpacing = source.characterSpacing;
        text.lineSpacing = source.lineSpacing;
        text.raycastTarget = false;
        var colour = source.color;
        colour.a = 1f;
        text.color = colour;

        // The text's own units on the minimap; world-space TextMeshPro draws its font a tenth as big unless orthographic
        var unit = Mathf.Abs(RelativeScale(source.transform, map).y) * VanillaMinimap.BaseUnit;
        text.fontSize = source.fontSize * (source.isOrthographic ? 1f : 0.1f) * unit;
        text.margin = source.margin * unit;

        var rt = text.rectTransform;
        var sourceRect = source.rectTransform;
        rt.pivot = sourceRect.pivot;
        rt.sizeDelta = sourceRect.rect.size * unit;
        rt.anchoredPosition = (Vector2) map.InverseTransformPoint(source.transform.position + worldOffset) *
                              VanillaMinimap.BaseUnit;
        rt.localEulerAngles = new Vector3(0f, 0f, source.transform.eulerAngles.z - map.eulerAngles.z);
        text.text = words;
        _made.Add(go);
    }

    /// <summary>
    /// The text in the player's language. The game fills these in when the map first shows them, so before that the
    /// text is read from the same localised entry the game uses.
    /// </summary>
    private static string TextOf(TMP_Text source) {
        var setter = source.GetComponent<SetTextMeshProGameText>();
        if (setter != null) {
            try {
                var entry = setter.Text;
                var words = !entry.IsEmpty ? (string) entry
                    : !string.IsNullOrEmpty(setter.convName) ? TeamCherry.Localization.Language.Get(setter.convName, setter.sheetName)
                    : "";
                if (!string.IsNullOrEmpty(words) && !words.StartsWith("#!#")) {
                    return TextToSingleLine?.GetValue(setter) is true
                        ? words.Replace("<br>", " ").Replace("\n", " ")
                        : words;
                }
            } catch (Exception) {
                // Falls back to the text as it is
            }
        }

        return source.text;
    }

    /// <summary>A picture under a shown object is drawn unless it or a parent below the object is switched off.</summary>
    private static bool ShownBelow(Transform t, Transform root) {
        for (var x = t; x != null && x != root; x = x.parent) {
            if (!x.gameObject.activeSelf || Hidden(x.gameObject)) {
                return false;
            }
        }

        return !Hidden(t.gameObject);
    }

    /// <summary>Pieces the quick map leaves out: world-map-only pictures and pins whose condition is not met.</summary>
    private static bool Hidden(GameObject go) {
        if (go.GetComponent<DisplayOnWorldMapOnly>() != null) {
            return true;
        }

        var conditional = go.GetComponent<MapPinConditional>();
        return conditional != null && PinCondition?.GetValue(conditional) is MapPinConditional.Condition condition &&
               !condition.IsFulfilled;
    }

    private static bool PinVisible(MapPin pin) {
        if (Hidden(pin.gameObject)) {
            return false;
        }

        // The quick map shows every pin that MapPin.CanBeActive allows, whatever the pin display setting
        try {
            return PinCanBeActive?.Invoke(pin, null) is true;
        } catch (Exception) {
            return false;
        }
    }

    private static bool QuestVisible(QuestMapMarker marker, MapZone zone) {
        try {
            return QuestIsActive?.Invoke(marker, [true, zone]) is true;
        } catch (Exception) {
            return false;
        }
    }

    /// <summary>MapNextAreaDisplay.Refresh's rules for the quick map.</summary>
    private static bool NextAreaVisible(MapNextAreaDisplay label, PlayerData playerData) {
        if (NextVisitedBool?.GetValue(label) is string visited && visited.Length > 0 && !playerData.GetBool(visited)) {
            return false;
        }

        if (NextCondition?.GetValue(label) is MapPinConditional.Condition condition && !condition.IsFulfilled) {
            return false;
        }

        if (NextSceneVisited?.GetValue(label) is string scene && scene.Length > 0 &&
            !playerData.scenesVisited.Contains(scene)) {
            return false;
        }

        // A name outside any room is shown, as the game does (it only logs an error)
        var mapScene = label.GetComponentInParent<GameMapScene>(true);
        return mapScene == null || mapScene.IsMapped;
    }

    /// <summary>How much bigger than the map's own units a transform draws, per axis (negative when mirrored).</summary>
    private static Vector2 RelativeScale(Transform t, Transform map) {
        var own = t.lossyScale;
        var mapScale = map.lossyScale;
        return new Vector2(
            Mathf.Approximately(mapScale.x, 0f) ? 0f : own.x / mapScale.x,
            Mathf.Approximately(mapScale.y, 0f) ? 0f : own.y / mapScale.y);
    }
}
