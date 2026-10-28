using SSMP.Api.Client;
using UnityEngine;
using ILogger = SSMP.Logging.ILogger;

namespace CoopMinimap;

/// <summary>
/// Gives rosaries on a key press, for players who want to buy what they need without farming. Rosaries are each
/// player's own in co-op, so this only changes the local player's. Off unless turned on in the settings (rosary_key).
/// </summary>
internal class RosaryHelper : MonoBehaviour {
    private const KeyCode GiveKey = KeyCode.F6;
    private const int Amount = 1000;

    private IClientApi _api = null!;
    private ILogger _logger = null!;
    private float _readyAt;

    public void Setup(IClientApi api, ILogger logger) {
        _api = api;
        _logger = logger;
    }

    private void Update() {
        if (!AddonSettings.RosaryKey || !Input.GetKeyDown(GiveKey) || Time.unscaledTime < _readyAt) {
            return;
        }

        _readyAt = Time.unscaledTime + 0.4f;

        try {
            if (_api.UiManager.ChatBox.IsOpen) {
                return;
            }

            var gm = GameManager.instance;
            var hero = HeroController.instance;
            if (gm == null || hero == null || gm.IsInSceneTransition) {
                return;
            }

            hero.AddGeo(Amount);
            _logger.Info($"CoopMinimap: gave {Amount} rosaries");
            _api.UiManager.ChatBox.AddMessage(Lang.T($"+{Amount} rosarios (F6)", $"+{Amount} rosaries (F6)"));
        } catch (System.Exception e) {
            _logger.Error($"CoopMinimap: giving rosaries failed:\n{e}");
        }
    }
}
