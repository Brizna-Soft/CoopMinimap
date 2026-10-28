using SSMP.Api.Client;
using UnityEngine;
using ILogger = SSMP.Logging.ILogger;

namespace CoopMinimap;

/// <summary>
/// Moves the local player next to a partner in the same room when the teleport key is pressed. Meant for getting
/// out of places the player is stuck in, such as outside the room's walls. Each player has their own key, so the
/// partner can come to us the same way. Can be turned off in the settings (teleport_key).
/// </summary>
internal class PartnerTeleporter : MonoBehaviour {
    private const KeyCode TeleportKey = KeyCode.F9;

    // Short wait between presses so holding the key does not keep moving the player
    private const float Cooldown = 1.5f;

    private IClientApi _api = null!;
    private ILogger _logger = null!;
    private float _readyAt;

    public void Setup(IClientApi api, ILogger logger) {
        _api = api;
        _logger = logger;
    }

    private void Update() {
        if (!AddonSettings.TeleportKey || !Input.GetKeyDown(TeleportKey) || Time.unscaledTime < _readyAt) {
            return;
        }

        _readyAt = Time.unscaledTime + Cooldown;

        try {
            TryTeleport();
        } catch (System.Exception e) {
            _logger.Error($"CoopMinimap: teleport failed:\n{e}");
            Say(Lang.T("Teletransporte: algo falló, revisa el log.", "Teleport: something went wrong, see the log."));
        }
    }

    private void TryTeleport() {
        var ui = _api.UiManager;
        if (ui.ChatBox.IsOpen) {
            return;
        }

        var gm = GameManager.instance;
        var hero = HeroController.instance;
        if (gm == null || hero == null || gm.IsInSceneTransition || hero.cState.transitioning) {
            Say(Lang.T("Teletransporte: espera a que termine de cargar la sala.",
                "Teleport: wait for the room to finish loading."));
            return;
        }

        var clientManager = _api.ClientManager;
        IClientPlayer? target = null;
        foreach (var player in clientManager.Players) {
            if (player.Username == clientManager.Username) {
                continue;
            }

            if (player.IsInLocalScene && player.PlayerObject != null) {
                target = player;
                break;
            }
        }

        if (target == null) {
            Say(Lang.T("Teletransporte: tu compañero no está en tu sala.", "Teleport: your partner is not in your room."));
            return;
        }

        var destination = (Vector2) target.PlayerObject!.transform.position + new Vector2(0f, 0.5f);

        var body = hero.Body;
        if (body != null) {
            body.linearVelocity = Vector2.zero;
            body.position = destination;
        }

        var heroTransform = hero.transform;
        heroTransform.position = new Vector3(destination.x, destination.y, heroTransform.position.z);

        // Being stuck often comes with the game having taken control away; give it back so the player can move
        if (hero.controlReqlinquished) {
            hero.RegainControl();
        }

        var cameras = GameCameras.instance;
        if (cameras != null && cameras.cameraController != null) {
            cameras.cameraController.PositionToHero(false);
        }

        _logger.Info($"CoopMinimap: teleported to {target.Username} at {destination}");
        Say(Lang.T($"Teletransportado junto a {target.Username}.", $"Teleported next to {target.Username}."));
    }

    private void Say(string message) {
        try {
            _api.UiManager.ChatBox.AddMessage(message);
        } catch {
            // The chat is only for feedback; the log already has what happened
        }
    }
}
