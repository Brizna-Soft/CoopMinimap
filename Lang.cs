using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CoopMinimap;

/// <summary>
/// Picks what the addon says in the game's language: Spanish when the game is set to Spanish, English otherwise.
/// </summary>
internal static class Lang {
    private static bool _spanish;
    private static float _checkedAt = float.MinValue;
    private static bool _unavailable;

    public static string T(string spanish, string english) => IsSpanish ? spanish : english;

    private static bool IsSpanish {
        get {
            // Looked up at most once a second, as labels ask every frame; the player may change it in the options
            if (_unavailable || Time.unscaledTime - _checkedAt < 1f) {
                return _spanish;
            }

            _checkedAt = Time.unscaledTime;
            try {
                _spanish = GameLanguageCode().StartsWith("ES", StringComparison.Ordinal);
            } catch {
                // The game keeps its language some other way now: stay in English
                _unavailable = true;
                _spanish = false;
            }

            return _spanish;
        }
    }

    // Kept apart so that, should a game update change this call, the error is caught above instead of breaking callers
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static string GameLanguageCode() => TeamCherry.Localization.Language.CurrentLanguage().ToString();
}
