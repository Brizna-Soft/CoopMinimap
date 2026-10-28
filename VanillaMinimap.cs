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
/// A minimap drawn from the game's own map: the room pieces the game shows on its map (only the areas whose map the
/// player has, as the game does), placed in the game map's own coordinates and drawn at the scale of the game's quick
/// map, with the player (shown with the game's compass icon) and partners placed by the same function the game uses
/// for its compass. It therefore matches the game's map exactly, unlike a picture of the map. Replaces Interactive
/// Map's minimap while on; F8 switches between the two, F10 shows or hides.
/// </summary>
internal class VanillaMinimap : MonoBehaviour {
    private const KeyCode StyleKey = KeyCode.F8;
    // Rooms are laid out once at this many pixels per game map unit; zooming scales the whole layout instead
    internal const float BaseUnit = 100f;
    private const float Margin = 20f;
    private const float HeadSize = 22f;
    private const float EdgeMargin = 12f;
    private static readonly Vector2 FrameSize = new(260f, 190f);
    // GameMap.TryOpenQuickMap draws the map at this scale; the minimap uses the same, so it looks like the quick map
    private const float QuickMapScale = 1.4725f;
    // Interactive Map's minimap zoom, used only until the camera that draws the game map is found
    private const float FallbackPpu = 185f;

    // Pixels per game map unit on screen: the quick map's (measured through its camera) times the player's own zoom
    private float _ppu = -1f;
    private float _zoom = 1f;
    private float _quickMapPpu = FallbackPpu;
    private Camera? _mapCamera;
    private float _nextMapScale;
    private bool _loggedMapScale;

    // The player is drawn with the game's compass icon, as big as on the quick map
    private FieldInfo? _compassIconField;
    private bool _compassDone;
    private Vector2 _ownMapSize;
    private Vector2 _ownMapOffset;

    // The caption: the area's name as the quick map titles it, then the main objective
    private string _zoneName = "";
    private int _currentZone = -1;

    // Pins, quest markers and next areas' names, as the quick map shows them
    private MinimapDecorations? _decorations;
    private bool _decorationsBroken;
    private int _decorationFailures;
    private Func<Transform, bool, bool>? _isAreaShown;
    private string? _captionZone;
    private object? _captionObjective;

    private IClientApi _api = null!;
    private ILogger _logger = null!;

    private bool _gameStyle = true;
    private bool _visible = true;
    private bool _syncedOnce;

    private InteractiveMap? _imap;
    private float _nextImapLookup;

    private Canvas? _canvas;
    private RectTransform? _frame;
    private RectTransform? _viewport;
    private RectTransform? _content;
    private RectTransform? _own;
    private Text? _caption;
    private Sprite? _head;

    private readonly Dictionary<ushort, GameObject> _partners = new();
    private readonly Dictionary<string, GameObject> _edges = new();
    private GameObject? _objectiveRing;

    // Rooms of the game map, found once per game map
    private GameMap? _cachedFor;
    private readonly List<(SpriteRenderer renderer, GameMapScene scene, object parentInfo, int zone)> _roomRenderers =
        new();
    // Each map area's unlock state and zone, by the area (its rooms are its direct children)
    private readonly Dictionary<Transform, (object parentInfo, int zone)> _areas = new();
    private bool _loggedGeometry;
    private readonly List<GameObject> _roomImages = new();
    private int _builtSignature;
    private float _nextRebuild;
    private float _nextSceneUpdate;
    private MinimapPosition _corner = (MinimapPosition) (-1);

    private FieldInfo? _zoneInfoField;
    private MethodInfo? _getMapPosition;
    private FieldInfo? _currentScene;
    private FieldInfo? _currentSceneObj;
    private FieldInfo? _currentScenePos;
    private FieldInfo? _currentSceneSize;
    private PropertyInfo? _isUnlocked;
    private bool _warnedNoUnlock;

    // The current room's place on the game map, read when the room changes (see TryGetMapPosition)
    private string? _sceneName;
    private bool _sceneValid;
    private bool _sceneHasBounds;
    private Vector2 _scenePos;
    private Vector2 _sceneSize;
    private Vector2 _sceneSpriteSize;

    private readonly HashSet<ushort> _seen = new();

    public void Setup(IClientApi api, ILogger logger) {
        _api = api;
        _logger = logger;

        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        var gameMapType = typeof(GameMap);
        _zoneInfoField = gameMapType.GetField("mapZoneInfo", flags);
        _getMapPosition = gameMapType.GetMethod("GetMapPosition", flags, null,
            [typeof(Vector2), typeof(GameMapScene), typeof(GameObject), typeof(Vector2), typeof(Vector2)], null);
        _currentScene = gameMapType.GetField("currentScene", flags);
        _currentSceneObj = gameMapType.GetField("currentSceneObj", flags);
        _currentScenePos = gameMapType.GetField("currentScenePos", flags);
        _currentSceneSize = gameMapType.GetField("currentSceneSize", flags);
        _compassIconField = gameMapType.GetField("compassIcon", flags);

        LoadSettings();
        if (_zoneInfoField == null || _getMapPosition == null) {
            _logger.Warn("CoopMinimap: the game map has changed, the game-map minimap is off");
            _gameStyle = false;
        }
    }

    private void Update() {
        try {
            HandleKeys();
            SyncInteractiveMap();

            var gm = GameManager.instance;
            var inGame = gm != null && gm.gameMap != null && HeroController.instance != null && gm.IsGameplayScene();

            // Interactive Map only hides its own minimap during play, so it stayed on the menus; hide it there
            if (!inGame && InteractiveMap._minimapComponent != null &&
                InteractiveMap._minimapComponent.gameObject.activeSelf) {
                InteractiveMap._minimapComponent.gameObject.SetActive(false);
            }

            var show = _gameStyle && _visible && inGame;
            if (!show) {
                if (_canvas != null) {
                    _canvas.gameObject.SetActive(false);
                }

                return;
            }

            EnsureUi();
            _canvas!.gameObject.SetActive(true);

            if (Time.unscaledTime >= _nextRebuild) {
                _nextRebuild = Time.unscaledTime + 1f;
                ApplyCorner();
                RebuildRooms(gm!.gameMap);
                RefreshDecorations(gm.gameMap);
            }

            UpdateView(gm!.gameMap);
        } catch (Exception e) {
            _logger.Error($"CoopMinimap: game-map minimap failed, turning it off:\n{e}");
            _gameStyle = false;
            if (_imap != null) {
                _imap.enableMinimap.Value = true;
            }

            if (_canvas != null) {
                _canvas.gameObject.SetActive(false);
            }
        }
    }

    private void HandleKeys() {
        var zoomIn = Input.GetKeyDown(KeyCode.PageUp) || Input.GetKeyDown(KeyCode.KeypadPlus);
        var zoomOut = Input.GetKeyDown(KeyCode.PageDown) || Input.GetKeyDown(KeyCode.KeypadMinus);
        if (zoomIn || zoomOut) {
            _zoom = Mathf.Clamp(_zoom * (zoomIn ? 1.25f : 0.8f), 0.25f, 4f);
            SaveSettings();
        }

        if (!Input.GetKeyDown(StyleKey)) {
            return;
        }

        try {
            if (_api.UiManager.ChatBox.IsOpen) {
                return;
            }
        } catch {
            // The chat may not be there yet; the key still works
        }

        _gameStyle = !_gameStyle;
        _visible = true;
        if (_imap != null) {
            _imap.enableMinimap.Value = !_gameStyle;
        }

        SaveSettings();
        Say(_gameStyle
            ? Lang.T("Minimapa: mapa del juego (F8 para cambiar)", "Minimap: game map (F8 to switch)")
            : Lang.T("Minimapa: MapGenie (F8 para cambiar)", "Minimap: MapGenie (F8 to switch)"));
    }

    /// <summary>
    /// While the game-map minimap is chosen, Interactive Map's own minimap stays off; its F10 key turning it back on
    /// is taken as showing or hiding ours instead.
    /// </summary>
    private void SyncInteractiveMap() {
        if (_imap == null && Time.unscaledTime >= _nextImapLookup) {
            _nextImapLookup = Time.unscaledTime + 3f;
            _imap = Object.FindFirstObjectByType<InteractiveMap>();
        }

        if (_imap == null || !_gameStyle) {
            return;
        }

        if (_imap.enableMinimap.Value) {
            if (_syncedOnce) {
                _visible = !_visible;
                SaveSettings();
            }

            _imap.enableMinimap.Value = false;
        }

        _syncedOnce = true;
    }

    private void EnsureUi() {
        if (_canvas != null) {
            return;
        }

        var root = new GameObject("CoopGameMapMinimap");
        DontDestroyOnLoad(root);
        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 998;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster: the minimap never takes clicks

        var frame = new GameObject("Frame", typeof(RectTransform));
        frame.transform.SetParent(root.transform, false);
        _frame = frame.GetComponent<RectTransform>();
        _frame.sizeDelta = FrameSize;
        var background = frame.AddComponent<Image>();
        background.color = new Color(0.02f, 0.02f, 0.04f, 0.82f);
        background.raycastTarget = false;
        var border = frame.AddComponent<Outline>();
        border.effectColor = new Color(0.85f, 0.75f, 0.5f, 0.85f);
        border.effectDistance = new Vector2(2f, -2f);

        var viewport = new GameObject("Viewport", typeof(RectTransform));
        viewport.transform.SetParent(frame.transform, false);
        _viewport = viewport.GetComponent<RectTransform>();
        _viewport.anchorMin = Vector2.zero;
        _viewport.anchorMax = Vector2.one;
        _viewport.offsetMin = new Vector2(4f, 4f);
        _viewport.offsetMax = new Vector2(-4f, -4f);
        viewport.AddComponent<RectMask2D>();

        var content = new GameObject("Content", typeof(RectTransform));
        content.transform.SetParent(viewport.transform, false);
        _content = content.GetComponent<RectTransform>();
        _content.sizeDelta = Vector2.zero;
        _decorations = new MinimapDecorations(_content, _logger);

        var own = new GameObject("Own", typeof(RectTransform));
        own.transform.SetParent(viewport.transform, false);
        _own = own.GetComponent<RectTransform>();
        _own.sizeDelta = new Vector2(HeadSize, HeadSize);
        _head ??= IconLoader.LoadCategoryIcon("me") ?? UiParts.Dot;
        UiParts.AddImage(own, _head, Color.white);
        UiParts.AddRim(own);

        _caption = UiParts.AddLabel(frame.transform, "Caption", 14, UiParts.MainObjectiveColour, Vector2.zero);
        ApplyCorner();
    }

    /// <summary>Uses the corner chosen in Interactive Map's settings, so the minimap stays where the player put it.</summary>
    private void ApplyCorner() {
        if (_frame == null) {
            return;
        }

        var corner = _imap != null ? _imap.minimapPosition.Value : MinimapPosition.BottomRight;
        if (corner == _corner) {
            return;
        }

        _corner = corner;
        var right = corner == MinimapPosition.TopRight || corner == MinimapPosition.BottomRight;
        var top = corner == MinimapPosition.TopRight || corner == MinimapPosition.TopLeft;
        var anchor = new Vector2(right ? 1f : 0f, top ? 1f : 0f);
        _frame.anchorMin = _frame.anchorMax = _frame.pivot = anchor;
        _frame.anchoredPosition = new Vector2(right ? -Margin : Margin, top ? -Margin - 22f : Margin);

        if (_caption != null) {
            var rt = _caption.rectTransform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, top ? 0f : 1f);
            rt.pivot = new Vector2(0.5f, top ? 1f : 0f);
            rt.anchoredPosition = new Vector2(0f, top ? -4f : 4f);
            // Its two lines grow away from the frame
            _caption.alignment = top ? TextAnchor.UpperCenter : TextAnchor.LowerCenter;
        }
    }

    private void RebuildRooms(GameMap gameMap) {
        if (_cachedFor != gameMap) {
            CacheRooms(gameMap);
        }

        // Rebuild only when what the map shows has changed (rooms mapped, maps bought, the zone when only it shows)
        var signature = AddonSettings.ZoneOnly ? 31 + _currentZone : 17;
        foreach (var (renderer, _, parentInfo, zone) in _roomRenderers) {
            if (renderer == null || renderer.sprite == null || !IsUnlocked(parentInfo) || !ZoneShown(zone)) {
                continue;
            }

            signature = signature * 31 + renderer.GetInstanceID();
            signature = signature * 31 + renderer.sprite.GetInstanceID();
        }

        if (signature == _builtSignature && _roomImages.Count > 0) {
            return;
        }

        _builtSignature = signature;
        foreach (var image in _roomImages) {
            if (image != null) {
                Destroy(image);
            }
        }

        _roomImages.Clear();

        var mapTransform = gameMap.transform;
        foreach (var (renderer, scene, parentInfo, zone) in _roomRenderers) {
            if (renderer == null || renderer.sprite == null || scene == null || !IsUnlocked(parentInfo) ||
                !ZoneShown(zone)) {
                continue;
            }

            // The room goes where the game's own compass maths puts it (GameMap.GetSceneInfo and GetMapPosition):
            // centred on the room's position plus its area's position, as big as its bounds sprite times its scale.
            // The heads use the same maths, so a player in a room is always drawn inside that room.
            var t = renderer.transform;
            var parent = t.parent;
            var centre = (Vector2) t.localPosition + (parent != null ? (Vector2) parent.localPosition : Vector2.zero);
            var boundsSprite = scene.BoundsSprite != null ? scene.BoundsSprite : renderer.sprite;
            var size = (Vector2) boundsSprite.bounds.size * (Vector2) t.localScale;
            if (Mathf.Abs(size.x) < 0.001f || Mathf.Abs(size.y) < 0.001f) {
                continue;
            }

            if (!_loggedGeometry && renderer.name == _sceneName) {
                _loggedGeometry = true;
                Vector2 drawnAt = mapTransform.InverseTransformPoint(t.TransformPoint(renderer.sprite.bounds.center));
                var grandParent = parent != null ? parent.parent : null;
                _logger.Info($"CoopMinimap: room '{renderer.name}': compass maths centre {centre}, size {size}; " +
                             $"where Unity draws it {drawnAt}; area '{(parent != null ? parent.name : "-")}' scale " +
                             $"{(parent != null ? parent.localScale : Vector3.one)}, under " +
                             $"'{(grandParent != null ? grandParent.name : "-")}'; sprite centre " +
                             $"{renderer.sprite.bounds.center}, bounds sprite centre {boundsSprite.bounds.center}");
            }

            var go = new GameObject("Room_" + renderer.name, typeof(RectTransform));
            go.transform.SetParent(_content, false);
            var rt = go.GetComponent<RectTransform>();
            rt.anchoredPosition = centre * BaseUnit;
            rt.sizeDelta = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y)) * BaseUnit;
            rt.localScale = new Vector3(Mathf.Sign(size.x) * (renderer.flipX ? -1f : 1f),
                Mathf.Sign(size.y) * (renderer.flipY ? -1f : 1f), 1f);

            var image = go.AddComponent<Image>();
            image.sprite = renderer.sprite;
            var colour = renderer.color;
            colour.a = 1f;
            image.color = colour;
            image.raycastTarget = false;
            _roomImages.Add(go);
        }

        _decorations?.BringToFront();
        _logger.Info($"CoopMinimap: game-map minimap shows {_roomImages.Count} rooms");
    }

    /// <summary>Like the quick map, only the current zone's rooms are drawn, unless zone_only is off.</summary>
    private bool ZoneShown(int zone) {
        return !AddonSettings.ZoneOnly || _currentZone < 0 || zone == _currentZone;
    }

    /// <summary>
    /// Whether the minimap draws the area an object of the game map belongs to: unlocked, the current zone's when only
    /// that is asked for, and nothing switched off between the object and its area (the game switches the areas
    /// themselves on and off as its maps open and close). Objects outside any area (the placed markers) always are.
    /// </summary>
    private bool IsAreaShown(Transform t, bool currentZoneOnly) {
        var map = _cachedFor != null ? _cachedFor.transform : null;
        for (var x = t.parent; x != null && x != map; x = x.parent) {
            if (_areas.TryGetValue(x, out var area)) {
                return IsUnlocked(area.parentInfo) && (!currentZoneOnly || _currentZone < 0 || area.zone == _currentZone);
            }

            if (!x.gameObject.activeSelf) {
                return false;
            }
        }

        return true;
    }

    private void RefreshDecorations(GameMap gameMap) {
        if (_decorations == null || _decorationsBroken) {
            return;
        }

        try {
            _isAreaShown ??= IsAreaShown;
            _decorations.Refresh(gameMap, (GlobalEnums.MapZone) Mathf.Max(_currentZone, 0), _isAreaShown,
                AddonSettings.ZoneOnly);
            _decorationFailures = 0;
        } catch (Exception e) {
            // Pins and names are extras: a passing failure (a scene still loading) is tried again next second, and
            // only one that keeps happening turns them off, leaving the minimap itself running
            _decorationFailures++;
            if (_decorationFailures == 1) {
                _logger.Warn($"CoopMinimap: could not draw the map pins and area names, trying again:\n{e}");
            }

            if (_decorationFailures >= 5) {
                _decorationsBroken = true;
                _logger.Error("CoopMinimap: map pins and area names on the minimap stopped after repeated failures");
            }
        }
    }

    private void CacheRooms(GameMap gameMap) {
        _cachedFor = gameMap;
        _roomRenderers.Clear();
        _areas.Clear();
        _builtSignature = 0;
        if (_zoneInfoField?.GetValue(gameMap) is not Array zones) {
            return;
        }

        // mapZoneInfo is indexed by MapZone, as GameMap.TryOpenQuickMap reads it
        for (var zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++) {
            var zone = zones.GetValue(zoneIndex);
            if (zone?.GetType().GetField("Parents")?.GetValue(zone) is not Array parents) {
                continue;
            }

            foreach (var parentInfo in parents) {
                if (parentInfo?.GetType().GetField("Parent")?.GetValue(parentInfo) is not GameObject parent ||
                    parent == null) {
                    continue;
                }

                // Only the area's direct children, as GameMap.GetSceneInfo looks rooms up (and so places the compass)
                var area = parent.transform;
                _areas[area] = (parentInfo, zoneIndex);
                for (var i = 0; i < area.childCount; i++) {
                    var child = area.GetChild(i);
                    if (child.TryGetComponent<GameMapScene>(out var scene) &&
                        child.TryGetComponent<SpriteRenderer>(out var renderer)) {
                        _roomRenderers.Add((renderer, scene, parentInfo, zoneIndex));
                    }
                }
            }
        }

        _logger.Info($"CoopMinimap: the game map has {_roomRenderers.Count} rooms");
    }

    private bool IsUnlocked(object parentInfo) {
        _isUnlocked ??= parentInfo.GetType().GetProperty("IsUnlocked");
        if (_isUnlocked == null) {
            if (!_warnedNoUnlock) {
                _warnedNoUnlock = true;
                _logger.Warn("CoopMinimap: could not read which map areas are unlocked; showing all of them");
            }

            return true;
        }

        return _isUnlocked.GetValue(parentInfo) is true;
    }

    private void UpdateView(GameMap gameMap) {
        if (_content == null || _viewport == null || _own == null) {
            return;
        }

        var gm = GameManager.instance;
        var sceneName = gm != null ? gm.sceneName : null;
        if (sceneName != _sceneName || Time.unscaledTime >= _nextSceneUpdate) {
            _sceneName = sceneName;
            _nextSceneUpdate = Time.unscaledTime + 1f;
            RefreshScene(gameMap);
        }

        var hero = HeroController.instance;
        if (hero == null || !TryGetMapPosition(hero.transform.position, out var own)) {
            _own.gameObject.SetActive(false);
            return;
        }

        _own.gameObject.SetActive(true);

        if (Time.unscaledTime >= _nextMapScale) {
            _nextMapScale = Time.unscaledTime + 1f;
            UpdateQuickMapScale(gameMap);
            if (!_compassDone) {
                UseCompassIcon(gameMap);
            }
        }

        // The quick map's own scale, so the minimap looks just like it; Page Up / Page Down multiply it
        var target = Mathf.Clamp(_quickMapPpu * _zoom, 15f, 600f);
        _ppu = _ppu < 0f ? target : Mathf.Lerp(_ppu, target, 1f - Mathf.Exp(-8f * Time.unscaledDeltaTime));

        _content.localScale = new Vector3(_ppu / BaseUnit, _ppu / BaseUnit, 1f);
        _content.anchoredPosition = -own * _ppu;
        if (_ownMapSize != Vector2.zero) {
            _own.sizeDelta = _ownMapSize * _ppu;
            _own.anchoredPosition = _ownMapOffset * _ppu;
        }

        _own.SetAsLastSibling();

        UpdatePartners(gameMap, own);
        UpdateObjective(own);
    }

    private void UpdatePartners(GameMap gameMap, Vector2 own) {
        var clientManager = _api.ClientManager;
        var seen = _seen;
        seen.Clear();
        foreach (var player in clientManager.Players) {
            if (player.Username == clientManager.Username) {
                continue;
            }

            Vector2 mapPos;
            if (player.IsInLocalScene && player.PlayerObject != null &&
                TryGetMapPosition(player.PlayerObject.transform.position, out var here)) {
                mapPos = here;
            } else if (clientManager.MapManager.TryGetEntry(player.Id, out var entry) && entry != null &&
                       entry.HasMapIcon) {
                mapPos = new Vector2(entry.Position.X, entry.Position.Y);
            } else {
                continue;
            }

            seen.Add(player.Id);
            if (!_partners.TryGetValue(player.Id, out var head) || head == null) {
                _partners[player.Id] = head = CreatePartnerHead(player.Username);
            }

            // A downed partner is drawn at their cocoon, red and blinking
            var down = PartnerDownAlert.Downed.TryGetValue(player.Id, out var downInfo);
            if (down && downInfo.scene == _sceneName && TryGetMapPosition(downInfo.position, out var cocoon)) {
                mapPos = cocoon;
            }

            var colour = UiParts.PartnerColour;
            if (down) {
                colour = PartnerDownAlert.DownColour;
                colour.a = PartnerDownAlert.Pulse;
            }

            if (head.TryGetComponent<Image>(out var headImage)) {
                headImage.color = colour;
            }

            var nameLabel = head.transform.Find("Name");
            if (nameLabel != null && nameLabel.TryGetComponent<Text>(out var nameText)) {
                nameText.text = down ? player.Username + Lang.T(" (CAÍDO)", " (DOWN)") : player.Username;
                nameText.color = down ? PartnerDownAlert.DownColour : UiParts.PartnerColour;
            }

            var inView = (mapPos - own) * _ppu;
            var visible = PlaceInView(head.GetComponent<RectTransform>(), inView);
            head.SetActive(visible);
            head.transform.SetAsLastSibling();
            PlaceEdge("p" + player.Id, inView, colour, player.Username, !visible);
        }

        foreach (var entry in _partners) {
            if (!seen.Contains(entry.Key) && entry.Value != null) {
                entry.Value.SetActive(false);
                PlaceEdge("p" + entry.Key, Vector2.zero, UiParts.PartnerColour, null, false);
            }
        }
    }

    private void UpdateObjective(Vector2 own) {
        var main = ObjectiveHighlighter.CurrentMain;
        if (_caption != null && (!ReferenceEquals(_captionZone, _zoneName) || !ReferenceEquals(_captionObjective, main))) {
            // Rebuilt only when the area or the objective changes
            _captionZone = _zoneName;
            _captionObjective = main;
            var objective = main != null ? Lang.T("Principal: ", "Main: ") + main.title : "";
            var zone = _zoneName.Length > 0 ? "<color=#FFFFFF>" + _zoneName + "</color>" : "";
            _caption.text = zone.Length > 0 && objective.Length > 0
                ? zone + "\n<size=12>" + objective + "</size>"
                : zone + objective;
            _caption.gameObject.SetActive(_caption.text.Length > 0);
        }

        if (main == null) {
            if (_objectiveRing != null) {
                _objectiveRing.SetActive(false);
            }

            PlaceEdge("objective", Vector2.zero, UiParts.MainObjectiveColour, null, false);
            return;
        }

        // The objective's place comes from Interactive Map's picture, turned into the game map's coordinates
        var local = MapPositionHelper.ConvertLocationCoordinatesToMapPosition(main);
        var mapPos = InteractiveMapAccess.Calibrator.TransformLocalToMap(local);
        var inView = (mapPos - own) * _ppu;

        if (_objectiveRing == null) {
            _objectiveRing = new GameObject("Objective", typeof(RectTransform));
            _objectiveRing.transform.SetParent(_viewport, false);
            _objectiveRing.GetComponent<RectTransform>().sizeDelta = new Vector2(26f, 26f);
            UiParts.AddImage(_objectiveRing, UiParts.Ring, UiParts.MainObjectiveColour);
        }

        var visible = PlaceInView(_objectiveRing.GetComponent<RectTransform>(), inView);
        _objectiveRing.SetActive(visible);
        PlaceEdge("objective", inView, UiParts.MainObjectiveColour, null, !visible);
    }

    /// <summary>Puts a marker at its place in the minimap's window; false when that place is outside the window.</summary>
    private bool PlaceInView(RectTransform marker, Vector2 inView) {
        marker.anchoredPosition = inView;
        var half = _viewport!.rect.size * 0.5f;
        return Mathf.Abs(inView.x) <= half.x && Mathf.Abs(inView.y) <= half.y;
    }

    private void PlaceEdge(string key, Vector2 inView, Color colour, string? label, bool show) {
        if (!_edges.TryGetValue(key, out var edge) || edge == null) {
            if (!show) {
                return;
            }

            edge = new GameObject("Edge_" + key, typeof(RectTransform));
            edge.transform.SetParent(_viewport, false);
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

            _edges[key] = edge;
        }

        if (!show) {
            edge.SetActive(false);
            return;
        }

        var half = _viewport!.rect.size * 0.5f - new Vector2(EdgeMargin, EdgeMargin);
        var t = Mathf.Min(half.x / Mathf.Max(Mathf.Abs(inView.x), 0.001f),
            half.y / Mathf.Max(Mathf.Abs(inView.y), 0.001f));
        var onEdge = inView * Mathf.Min(t, 1f);
        edge.GetComponent<RectTransform>().anchoredPosition = onEdge;
        var arrowTransform = edge.transform.Find("Arrow");
        arrowTransform.localEulerAngles = new Vector3(0f, 0f, Mathf.Atan2(inView.y, inView.x) * Mathf.Rad2Deg - 90f);
        if (arrowTransform.TryGetComponent<Image>(out var arrowImage)) {
            arrowImage.color = colour;
        }
        var name = edge.transform.Find("Label");
        if (name != null) {
            name.GetComponent<RectTransform>().anchoredPosition = -onEdge.normalized * 18f;
        }

        edge.SetActive(true);
        edge.transform.SetAsLastSibling();
    }

    private GameObject CreatePartnerHead(string name) {
        var go = new GameObject("Partner_" + name, typeof(RectTransform));
        go.transform.SetParent(_viewport, false);
        go.GetComponent<RectTransform>().sizeDelta = new Vector2(HeadSize, HeadSize);
        _head ??= IconLoader.LoadCategoryIcon("me") ?? UiParts.Dot;
        UiParts.AddImage(go, _head, UiParts.PartnerColour);
        UiParts.AddRim(go);
        var label = UiParts.AddLabel(go.transform, "Name", 12, UiParts.PartnerColour, new Vector2(0f, HeadSize * 0.5f + 8f));
        if (label != null) {
            label.text = name;
        }

        return go;
    }

    /// <summary>
    /// Reads where the current room sits on the game map (the values GameMap.GetMapPosition works from).
    /// </summary>
    private void RefreshScene(GameMap gameMap) {
        _sceneValid = false;
        _currentZone = CurrentZone(gameMap);
        var zoneName = ZoneName(gameMap, _currentZone);
        if (zoneName != _zoneName) {
            _zoneName = zoneName;
        }

        if (_currentScene == null || _currentSceneObj == null || _currentScenePos == null || _currentSceneSize == null) {
            return;
        }

        gameMap.UpdateCurrentScene();
        var sceneObj = _currentSceneObj.GetValue(gameMap) as GameObject;
        if (sceneObj == null) {
            return;
        }

        _scenePos = (Vector2) _currentScenePos.GetValue(gameMap);
        _sceneSize = (Vector2) _currentSceneSize.GetValue(gameMap);
        var scene = _currentScene.GetValue(gameMap) as GameMapScene;
        _sceneHasBounds = scene != null && scene.BoundsSprite != null;
        if (_sceneHasBounds) {
            _sceneSpriteSize = (Vector2) scene!.BoundsSprite.bounds.size * (Vector2) scene.transform.localScale;
        }

        _sceneValid = !_sceneHasBounds || (_sceneSize.x > 0f && _sceneSize.y > 0f);
    }

    /// <summary>The zone the player is in (a MapZone value), or -1 when there is none.</summary>
    private static int CurrentZone(GameMap gameMap) {
        try {
            var zone = gameMap.GetCurrentMapZone();
            return zone == GlobalEnums.MapZone.NONE ? -1 : (int) zone;
        } catch (Exception) {
            return -1;
        }
    }

    /// <summary>The zone's name as the quick map titles it (GameMap.TryOpenQuickMap); empty if unknown.</summary>
    private string ZoneName(GameMap gameMap, int zoneIndex) {
        try {
            if (zoneIndex < 0) {
                return "";
            }

            var zone = (GlobalEnums.MapZone) zoneIndex;
            if (_zoneInfoField?.GetValue(gameMap) is Array zones && zoneIndex < zones.Length) {
                var info = zones.GetValue(zoneIndex);
                if (info?.GetType().GetField("NameOverride")?.GetValue(info) is TeamCherry.Localization.LocalisedString
                        nameOverride && !nameOverride.IsEmpty) {
                    return (string) nameOverride;
                }
            }

            var name = TeamCherry.Localization.Language.Get(zone.ToString(), "Map Zones").Replace("<br>", string.Empty);
            return name.StartsWith("#!#") ? "" : name;
        } catch (Exception) {
            // Only the caption's title; the minimap works without it
            return "";
        }
    }

    /// <summary>
    /// Measures how big one game map unit is on the game's quick map, in this canvas's units: the quick map's scale
    /// (GameMap.TryOpenQuickMap) taken through the map's parents and the camera that draws the map.
    /// </summary>
    private void UpdateQuickMapScale(GameMap gameMap) {
        if (_canvas == null || _canvas.scaleFactor <= 0f) {
            return;
        }

        if (_mapCamera == null || !_mapCamera.isActiveAndEnabled) {
            _mapCamera = FindMapCamera(gameMap);
            if (_mapCamera == null) {
                if (!_loggedMapScale) {
                    _loggedMapScale = true;
                    _logger.Warn("CoopMinimap: could not find the camera that draws the game map; using a fixed zoom");
                }

                return;
            }
        }

        var parent = gameMap.transform.parent;
        var up = new Vector3(0f, QuickMapScale, 0f);
        var from = parent != null ? parent.TransformPoint(Vector3.zero) : Vector3.zero;
        var to = parent != null ? parent.TransformPoint(up) : up;
        // Through the camera's viewport, so it is right even when the camera draws into a texture shown full screen
        var shown = _mapCamera.targetTexture == null
            ? _mapCamera.pixelRect.size
            : new Vector2(Screen.width, Screen.height);
        var step = (Vector2) (_mapCamera.WorldToViewportPoint(to) - _mapCamera.WorldToViewportPoint(from));
        var pixels = (step * shown).magnitude;
        if (pixels < 1f) {
            return;
        }

        var ppu = pixels / _canvas.scaleFactor;
        if (!_loggedMapScale || Mathf.Abs(ppu - _quickMapPpu) > 1f) {
            _loggedMapScale = true;
            _logger.Info($"CoopMinimap: on the quick map a map unit is {pixels:F1} screen pixels ({ppu:F1} on the " +
                         $"minimap), camera '{_mapCamera.name}', orthographic size {_mapCamera.orthographicSize}");
        }

        _quickMapPpu = ppu;
    }

    /// <summary>The camera that draws the game map: the HUD camera, or else the orthographic camera that sees its layer.</summary>
    private Camera? FindMapCamera(GameMap gameMap) {
        var layer = gameMap.gameObject.layer;
        foreach (var (renderer, _, _, _) in _roomRenderers) {
            if (renderer != null) {
                layer = renderer.gameObject.layer;
                break;
            }
        }

        var mask = 1 << layer;
        var cameras = GameCameras.SilentInstance;
        if (cameras != null && cameras.hudCamera != null && cameras.hudCamera.orthographic &&
            (cameras.hudCamera.cullingMask & mask) != 0) {
            return cameras.hudCamera;
        }

        Camera? best = null;
        foreach (var camera in Camera.allCameras) {
            if (camera.orthographic && (camera.cullingMask & mask) != 0 && (best == null || camera.depth > best.depth)) {
                best = camera;
            }
        }

        return best;
    }

    /// <summary>Draws the player with the game's own compass icon, as big as on the map and pinned at the same point.</summary>
    private void UseCompassIcon(GameMap gameMap) {
        if (_own == null) {
            return;
        }

        if (_compassIconField == null) {
            _compassDone = true;
            return;
        }

        if (_compassIconField.GetValue(gameMap) is not GameObject compass || compass == null) {
            // Not set up yet; tried again next second
            return;
        }

        _compassDone = true;
        var renderer = compass.GetComponentInChildren<SpriteRenderer>(true);
        if (renderer == null || renderer.sprite == null) {
            _logger.Warn("CoopMinimap: the game's compass icon has no sprite; the minimap keeps its own icon");
            return;
        }

        var sprite = renderer.sprite;
        Vector2 size = gameMap.transform.InverseTransformVector(renderer.transform.TransformVector(sprite.bounds.size));
        size = new Vector2(Mathf.Abs(size.x), Mathf.Abs(size.y));
        if (size.x < 0.001f || size.y < 0.001f) {
            return;
        }

        // The game places the compass object at the player; its picture may sit offset or turned inside it
        var mapTransform = gameMap.transform;
        _ownMapOffset = mapTransform.InverseTransformPoint(renderer.transform.position) -
                        mapTransform.InverseTransformPoint(compass.transform.position);
        _own.localEulerAngles = new Vector3(0f, 0f, renderer.transform.eulerAngles.z - mapTransform.eulerAngles.z);

        _ownMapSize = size;
        var image = _own.GetComponent<Image>();
        image.sprite = sprite;
        var colour = renderer.color;
        colour.a = 1f;
        image.color = colour;
        _own.pivot = new Vector2(sprite.pivot.x / sprite.rect.width, sprite.pivot.y / sprite.rect.height);
        foreach (var rim in _own.GetComponents<Outline>()) {
            Destroy(rim);
        }

        _logger.Info($"CoopMinimap: the player is drawn with the game's compass icon '{sprite.name}', {size} map units");
    }

    /// <summary>
    /// The game's own GameMap.GetMapPosition, computed from the values read in RefreshScene: where a point of the
    /// current room is on the game map. (Its scale terms cancel out, so they are left out here.)
    /// </summary>
    private bool TryGetMapPosition(Vector2 worldPos, out Vector2 mapPos) {
        mapPos = Vector2.zero;
        if (!_sceneValid) {
            return false;
        }

        if (!_sceneHasBounds) {
            mapPos = _scenePos;
            return true;
        }

        mapPos = new Vector2(
            _scenePos.x - _sceneSpriteSize.x / 2f + worldPos.x / _sceneSize.x * _sceneSpriteSize.x,
            _scenePos.y - _sceneSpriteSize.y / 2f + worldPos.y / _sceneSize.y * _sceneSpriteSize.y);
        return true;
    }

    private void LoadSettings() {
        _gameStyle = AddonSettings.GameStyle;
        _visible = AddonSettings.Visible;
        _zoom = AddonSettings.Zoom;
    }

    private void SaveSettings() {
        AddonSettings.GameStyle = _gameStyle;
        AddonSettings.Visible = _visible;
        AddonSettings.Zoom = _zoom;
        AddonSettings.Save();
    }

    private void Say(string message) {
        try {
            _api.UiManager.ChatBox.AddMessage(message);
        } catch {
            // Feedback only
        }
    }
}
