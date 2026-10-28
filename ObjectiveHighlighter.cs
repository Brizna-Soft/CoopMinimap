using System;
using System.Collections.Generic;
using SilkSongMapInteractive;
using UnityEngine;
using UnityEngine.UI;
using ILogger = SSMP.Logging.ILogger;
using Object = UnityEngine.Object;

namespace CoopMinimap;

/// <summary>
/// Marks the objectives Interactive Map considers current: an orange ring for the main story ("Objective" category)
/// and a purple ring for side quests (wishes and the like), on the minimap as well as the full map. Also keeps the
/// main objective nearest to the player, for the minimap's arrow and caption.
/// </summary>
internal class ObjectiveHighlighter : MonoBehaviour {
    private const int MainObjectiveCategory = 13588;
    private const string RingName = "CoopObjectiveRing";

    /// <summary>The current main objective closest to us, or null when there is none.</summary>
    public static MapLocation? CurrentMain { get; private set; }

    private ILogger _logger = null!;
    private float _next;

    public void Setup(ILogger logger) {
        _logger = logger;
    }

    private void Update() {
        if (Time.unscaledTime < _next) {
            return;
        }

        _next = Time.unscaledTime + 0.5f;
        try {
            Refresh();
        } catch (Exception e) {
            _logger.Error($"CoopMinimap: objective rings failed:\n{e}");
            _next = Time.unscaledTime + 10f;
        }
    }

    private void Refresh() {
        var data = MapRenderer._mapData;
        if (data?.locations == null) {
            CurrentMain = null;
            return;
        }

        var own = InteractiveMapAccess.OwnIconPosition;
        var kinds = new Dictionary<int, bool>(); // location id -> is main
        MapLocation? nearest = null;
        var best = float.MaxValue;
        foreach (var location in data.locations) {
            if (!location.isCurrentObjective || location.isCompleted) {
                continue;
            }

            var isMain = location.category_id == MainObjectiveCategory;
            kinds[location.id] = isMain;
            if (!isMain) {
                continue;
            }

            var d = (MapPositionHelper.ConvertLocationCoordinatesToMapPosition(location) - own).sqrMagnitude;
            if (d < best) {
                best = d;
                nearest = location;
            }
        }

        CurrentMain = nearest;

        foreach (var renderer in Object.FindObjectsByType<MapRenderer>(FindObjectsSortMode.None)) {
            foreach (var entry in renderer._locationMarkers) {
                var marker = entry.Value;
                if (marker == null) {
                    continue;
                }

                var ring = marker.transform.Find(RingName);
                if (kinds.TryGetValue(entry.Key, out var isMain)) {
                    if (ring == null) {
                        ring = CreateRing(marker);
                    }

                    ring.GetComponent<Image>().color = isMain ? UiParts.MainObjectiveColour : UiParts.SideObjectiveColour;
                    ring.gameObject.SetActive(true);
                } else if (ring != null && ring.gameObject.activeSelf) {
                    ring.gameObject.SetActive(false);
                }
            }
        }
    }

    private static Transform CreateRing(GameObject marker) {
        var ring = new GameObject(RingName, typeof(RectTransform));
        ring.transform.SetParent(marker.transform, false);
        var size = marker.GetComponent<RectTransform>().sizeDelta;
        ring.GetComponent<RectTransform>().sizeDelta = size * 2.2f;
        UiParts.AddImage(ring, UiParts.Ring, Color.white);
        return ring.transform;
    }
}
