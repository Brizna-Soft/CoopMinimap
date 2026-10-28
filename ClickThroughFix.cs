using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using ILogger = SSMP.Logging.ILogger;

namespace CoopMinimap;

/// <summary>
/// Interactive Map keeps an invisible notification panel (and its minimap) on top of every other canvas with clicks
/// switched on, so they swallow clicks meant for whatever is under them, such as the multiplayer menu's buttons.
/// Neither has anything to click, so they are made click-through here. Also logs, for the first clicks in menus,
/// which interface element received them, to confirm what blocks a button.
/// </summary>
internal class ClickThroughFix : MonoBehaviour {
    private const int MaxClickLogs = 12;

    private ILogger _logger = null!;
    private float _next;
    private bool _notificationDone;
    private bool _minimapDone;
    private int _clickLogs;
    private readonly List<RaycastResult> _hits = new();

    public void Setup(ILogger logger) {
        _logger = logger;
    }

    private void Update() {
        if (_clickLogs < MaxClickLogs && Input.GetMouseButtonDown(0) && HeroController.instance == null) {
            LogClick();
        }

        if ((_notificationDone && _minimapDone) || Time.unscaledTime < _next) {
            return;
        }

        _next = Time.unscaledTime + 2f;

        if (!_notificationDone) {
            var canvas = GameObject.Find("NotificationCanvas");
            if (canvas != null) {
                foreach (var group in canvas.GetComponentsInChildren<CanvasGroup>(true)) {
                    group.blocksRaycasts = false;
                    group.interactable = false;
                }

                foreach (var graphic in canvas.GetComponentsInChildren<Graphic>(true)) {
                    graphic.raycastTarget = false;
                }

                _notificationDone = true;
                _logger.Info("CoopMinimap: Interactive Map's notification panel no longer blocks clicks");
            }
        }

        if (!_minimapDone) {
            var minimap = GameObject.Find("MinimapUI");
            if (minimap != null && minimap.TryGetComponent<GraphicRaycaster>(out var raycaster)) {
                raycaster.enabled = false;
                _minimapDone = true;
                _logger.Info("CoopMinimap: Interactive Map's minimap no longer blocks clicks");
            }
        }
    }

    private void LogClick() {
        var eventSystem = EventSystem.current;
        if (eventSystem == null) {
            return;
        }

        _clickLogs++;
        _hits.Clear();
        eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = Input.mousePosition }, _hits);

        var described = new List<string>();
        for (var i = 0; i < _hits.Count && i < 3; i++) {
            var go = _hits[i].gameObject;
            var canvas = go.GetComponentInParent<Canvas>();
            described.Add($"{Path(go.transform)} (canvas order {(canvas != null ? canvas.sortingOrder : 0)})");
        }

        _logger.Info($"CoopMinimap: click at {(Vector2) Input.mousePosition} hit " +
                     (described.Count == 0 ? "nothing" : string.Join(" | ", described)));
    }

    private static string Path(Transform t) {
        var parts = new List<string>();
        for (var i = 0; t != null && i < 4; i++, t = t.parent) {
            parts.Insert(0, t.name);
        }

        return string.Join("/", parts);
    }
}
