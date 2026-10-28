using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using SSMP.Api.Client;
using UnityEngine;
using UnityEngine.UI;
using ILogger = SSMP.Logging.ILogger;

namespace CoopMinimap;

/// <summary>
/// Shows a big pulsing banner while a partner lies downed in their cocoon waiting to be broken out, with how many of
/// the 5 hits it has had and whether they are in another room; the minimaps colour that partner red meanwhile. The
/// co-op mod keeps who is waiting in CoopSave._waitingMembers (scene, position, hits), read here by reflection.
/// </summary>
internal class PartnerDownAlert : MonoBehaviour {
    public static readonly Color DownColour = new(1f, 0.25f, 0.2f, 1f);

    /// <summary>Partners lying downed now, by player id.</summary>
    public static readonly Dictionary<ushort, (string scene, Vector2 position, int hits)> Downed = new();

    /// <summary>A 0..1 pulse shared by everything that shows a downed partner, so they blink together.</summary>
    public static float Pulse => 0.55f + 0.45f * Mathf.Sin(Time.unscaledTime * 7f);

    private IClientApi _api = null!;
    private ILogger _logger = null!;
    private float _next;
    private bool _broken;

    private FieldInfo? _coopSaveField;
    private FieldInfo? _waitingField;
    private PropertyInfo? _sceneProp;
    private PropertyInfo? _positionProp;
    private PropertyInfo? _hitsProp;

    private Canvas? _canvas;
    private Text? _title;
    private Text? _detail;

    public void Setup(IClientApi api, ILogger logger) {
        _api = api;
        _logger = logger;
    }

    private void Update() {
        if (_broken) {
            return;
        }

        try {
            if (Time.unscaledTime >= _next) {
                _next = Time.unscaledTime + 0.2f;
                try {
                    ReadDowned();
                } catch (InvalidOperationException) {
                    // The co-op mod changed the list while it was read; skip this read, the next one will do
                    Downed.Clear();
                }
            }

            UpdateBanner();
        } catch (Exception e) {
            _broken = true;
            Downed.Clear();
            if (_canvas != null) {
                _canvas.gameObject.SetActive(false);
            }

            _logger.Error($"CoopMinimap: the downed-partner alert stopped:\n{e}");
        }
    }

    private void ReadDowned() {
        Downed.Clear();

        var clientManager = _api.ClientManager;
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        _coopSaveField ??= clientManager.GetType().GetField("_coopSave", flags);
        var coopSave = _coopSaveField?.GetValue(clientManager);
        if (coopSave == null) {
            return;
        }

        _waitingField ??= coopSave.GetType().GetField("_waitingMembers", flags);
        if (_waitingField?.GetValue(coopSave) is not IDictionary waiting || waiting.Count == 0) {
            return;
        }

        foreach (DictionaryEntry entry in waiting) {
            if (entry.Key is not ushort id || entry.Value == null) {
                continue;
            }

            // Only partners: the local player's own cocoon is the co-op mod's to show
            var partner = false;
            foreach (var player in clientManager.Players) {
                if (player.Id == id && player.Username != clientManager.Username) {
                    partner = true;
                    break;
                }
            }

            if (!partner) {
                continue;
            }

            var type = entry.Value.GetType();
            _sceneProp ??= type.GetProperty("Scene");
            _positionProp ??= type.GetProperty("Position");
            _hitsProp ??= type.GetProperty("Hits");
            var scene = _sceneProp?.GetValue(entry.Value) as string ?? "";
            var position = _positionProp?.GetValue(entry.Value) is Vector2 p ? p : Vector2.zero;
            var hits = _hitsProp?.GetValue(entry.Value) is int h ? h : 0;
            Downed[id] = (scene, position, hits);
        }
    }

    private void UpdateBanner() {
        if (Downed.Count == 0) {
            if (_canvas != null && _canvas.gameObject.activeSelf) {
                _canvas.gameObject.SetActive(false);
            }

            return;
        }

        EnsureBanner();
        if (_canvas == null || _title == null) {
            return;
        }

        _canvas.gameObject.SetActive(true);

        string? name = null;
        var info = default((string scene, Vector2 position, int hits));
        foreach (var entry in Downed) {
            foreach (var player in _api.ClientManager.Players) {
                if (player.Id == entry.Key) {
                    name = player.Username;
                    break;
                }
            }

            info = entry.Value;
            break;
        }

        var currentScene = GameManager.instance != null ? GameManager.instance.sceneName : "";
        _title.text = Lang.T($"¡{(name ?? "TU COMPAÑERO").ToUpperInvariant()} ESTÁ CAÍDO!",
            $"{(name ?? "YOUR PARTNER").ToUpperInvariant()} IS DOWN!");
        var colour = DownColour;
        colour.a = Pulse;
        _title.color = colour;

        if (_detail != null) {
            var hits = Mathf.Min(info.hits, 5);
            _detail.text = info.scene == currentScene
                ? Lang.T($"Golpea su capullo para levantarlo ({hits}/5)", $"Hit their cocoon to get them up ({hits}/5)")
                : Lang.T($"Está en otra sala ({info.scene}). Ve a buscarlo y golpea su capullo ({hits}/5)",
                    $"They are in another room ({info.scene}). Go find them and hit their cocoon ({hits}/5)");
        }
    }

    private void EnsureBanner() {
        if (_canvas != null) {
            return;
        }

        var root = new GameObject("CoopDownAlert");
        DontDestroyOnLoad(root);
        _canvas = root.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = 1500;
        var scaler = root.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight = 0.5f;
        // No GraphicRaycaster: never takes clicks

        var panel = new GameObject("Panel", typeof(RectTransform));
        panel.transform.SetParent(root.transform, false);
        var rt = panel.GetComponent<RectTransform>();
        rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
        rt.pivot = new Vector2(0.5f, 1f);
        rt.anchoredPosition = new Vector2(0f, -110f);
        rt.sizeDelta = new Vector2(900f, 90f);
        var background = panel.AddComponent<Image>();
        background.color = new Color(0f, 0f, 0f, 0.55f);
        background.raycastTarget = false;

        _title = UiParts.AddLabel(panel.transform, "Title", 40, DownColour, new Vector2(0f, 14f));
        _detail = UiParts.AddLabel(panel.transform, "Detail", 20, Color.white, new Vector2(0f, -26f));
        if (_title != null) {
            _title.fontStyle = FontStyle.Bold;
        }
    }
}
