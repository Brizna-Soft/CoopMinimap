using System;
using HarmonyLib;
using UnityEngine;
using ILogger = SSMP.Logging.ILogger;

namespace CoopMinimap;

/// <summary>
/// Fixes for game code that co-op makes fail.
/// </summary>
internal static class CoopGameFixes {
    private static bool _applied;
    private static ILogger? _logger;
    private static int _stopCrawlerSkips;

    public static void Apply(ILogger logger) {
        if (_applied) {
            return;
        }

        _applied = true;
        _logger = logger;
        try {
            var harmony = new Harmony("coopminimap.gamefixes");
            harmony.Patch(AccessTools.Method(typeof(StopCrawler), nameof(StopCrawler.OnEnter)),
                prefix: new HarmonyMethod(typeof(CoopGameFixes), nameof(StopCrawlerOnEnterPrefix)));
            logger.Info("CoopMinimap: game fix applied (StopCrawler without a Crawler)");
        } catch (Exception e) {
            logger.Error($"CoopMinimap: could not apply game fixes:\n{e}");
        }
    }

    /// <summary>
    /// StopCrawler.OnEnter calls Finish() when its target has no Crawler, but then goes on and uses the missing
    /// Crawler anyway (NullReferenceException). That never happens in the normal game; it does when the co-op mod
    /// replays a partner's hit on its copy of the enemy, which has no Crawler. Here the action just finishes, which
    /// is what the original meant to do; with a Crawler the original runs unchanged.
    /// </summary>
    private static bool StopCrawlerOnEnterPrefix(StopCrawler __instance) {
        try {
            var target = __instance.Target.GetSafe(__instance);
            if (target != null && target.GetComponent<Crawler>() != null) {
                return true;
            }

            __instance.Finish();
            if (_stopCrawlerSkips++ < 5) {
                _logger?.Info($"CoopMinimap: skipped StopCrawler on '{(target != null ? target.name : "nothing")}' " +
                              "(no Crawler), which would have thrown");
            }

            return false;
        } catch {
            // Never make things worse than the original: let it run
            return true;
        }
    }
}
