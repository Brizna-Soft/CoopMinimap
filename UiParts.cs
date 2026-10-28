using UnityEngine;
using UnityEngine.UI;

namespace CoopMinimap;

/// <summary>
/// Sprites, colours and labels shared by the map overlays. Sprites are drawn in code so the addon ships as one dll.
/// </summary>
internal static class UiParts {
    public static readonly Color OwnColour = new(1f, 0.9f, 0.2f, 1f);
    public static readonly Color PartnerColour = new(0.3f, 0.85f, 1f, 1f);
    public static readonly Color MainObjectiveColour = new(1f, 0.55f, 0.1f, 1f);
    public static readonly Color SideObjectiveColour = new(0.8f, 0.5f, 1f, 1f);

    private static Sprite? _dot;
    private static Sprite? _ring;
    private static Sprite? _arrow;
    private static Font? _font;
    private static bool _fontLooked;

    /// <summary>A filled circle with soft edges.</summary>
    public static Sprite Dot => _dot ??= Draw(32, (x, y) => {
        var d = Vector2.Distance(new Vector2(x, y), new Vector2(15.5f, 15.5f));
        return Mathf.Clamp01(15f - d);
    });

    /// <summary>A hollow circle, to put around a marker without covering it.</summary>
    public static Sprite Ring => _ring ??= Draw(64, (x, y) => {
        var d = Vector2.Distance(new Vector2(x, y), new Vector2(31.5f, 31.5f));
        return Mathf.Clamp01(31f - d) * Mathf.Clamp01(d - 24f);
    });

    /// <summary>A triangle pointing up, rotated to point at something outside the minimap.</summary>
    public static Sprite Arrow => _arrow ??= Draw(32, (x, y) => {
        if (y < 3f || y > 29f) {
            return 0f;
        }

        var halfWidth = 13f * (29f - y) / 26f;
        return Mathf.Clamp01(halfWidth - Mathf.Abs(x - 15.5f) + 0.5f);
    });

    /// <summary>Unity's built-in UI font, or null when the game has none (labels are then left out).</summary>
    public static Font? Font {
        get {
            if (_fontLooked) {
                return _font;
            }

            _fontLooked = true;
            foreach (var name in new[] { "LegacyRuntime.ttf", "Arial.ttf" }) {
                try {
                    _font = Resources.GetBuiltinResource<Font>(name);
                } catch {
                    _font = null;
                }

                if (_font != null) {
                    break;
                }
            }

            return _font;
        }
    }

    public static Image AddImage(GameObject go, Sprite sprite, Color colour) {
        var image = go.AddComponent<Image>();
        image.sprite = sprite;
        image.color = colour;
        image.raycastTarget = false;
        image.preserveAspect = true;
        return image;
    }

    public static void AddRim(GameObject go) {
        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.9f);
        outline.effectDistance = new Vector2(2f, -2f);
    }

    public static Text? AddLabel(Transform parent, string name, int size, Color colour, Vector2 position) {
        var font = Font;
        if (font == null) {
            return null;
        }

        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var rt = go.GetComponent<RectTransform>();
        rt.sizeDelta = new Vector2(240f, 22f);
        rt.anchoredPosition = position;

        var text = go.AddComponent<Text>();
        text.font = font;
        text.fontSize = size;
        text.color = colour;
        text.alignment = TextAnchor.MiddleCenter;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        text.raycastTarget = false;

        var outline = go.AddComponent<Outline>();
        outline.effectColor = new Color(0f, 0f, 0f, 0.95f);
        outline.effectDistance = new Vector2(1.5f, -1.5f);
        return text;
    }

    private static Sprite Draw(int size, System.Func<float, float, float> alphaAt) {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear };
        for (var y = 0; y < size; y++) {
            for (var x = 0; x < size; x++) {
                texture.SetPixel(x, y, new Color(1f, 1f, 1f, alphaAt(x, y)));
            }
        }

        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, size, size), new Vector2(0.5f, 0.5f));
    }
}
