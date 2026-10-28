using System.Collections.Generic;
using SSMP.Api.Client;
using UnityEngine;
using ILogger = SSMP.Logging.ILogger;

namespace CoopMinimap;

/// <summary>
/// After connecting, says in the chat what the addon's keys and colours are, and how to keep seeing the partner when
/// they are in another room.
/// </summary>
internal class ConnectHints : MonoBehaviour {
    private IClientApi _api = null!;
    private ILogger _logger = null!;
    private float _showAt = -1f;

    public void Setup(IClientApi api, ILogger logger) {
        _api = api;
        _logger = logger;
        api.ClientManager.ConnectEvent += () => _showAt = Time.unscaledTime + 8f;
    }

    private void Update() {
        if (_showAt < 0f || Time.unscaledTime < _showAt) {
            return;
        }

        _showAt = -1f;
        try {
            // Only the keys that are on
            var keys = new List<string>();
            if (AddonSettings.TeleportKey) {
                keys.Add(Lang.T("F9 = ir junto a tu compañero", "F9 = go to your partner"));
            }

            if (AddonSettings.RosaryKey) {
                keys.Add(Lang.T("F6 = +1000 rosarios", "F6 = +1000 rosaries"));
            }

            keys.Add(Lang.T("F8 = estilo del minimapa", "F8 = minimap style"));
            keys.Add(Lang.T("F10 = mostrar u ocultar", "F10 = show or hide"));

            var chat = _api.UiManager.ChatBox;
            chat.AddMessage("CoopMinimap: " + string.Join(", ", keys) + ".");
            chat.AddMessage(Lang.T("Mapa: naranja = objetivo principal, morado = secundario, celeste = tu compañero.",
                "Map: orange = main objective, purple = side objective, light blue = your partner."));

            var settings = _api.ClientManager.ServerSettings;
            var global = settings?.GetType().GetProperty("AlwaysShowMapIcons")?.GetValue(settings) as bool?;
            if (global == false) {
                chat.AddMessage(Lang.T(
                    "Para ver a tu compañero en otras salas, el anfitrión escribe: /set globalmapicons true",
                    "To see your partner in other rooms, the host types: /set globalmapicons true"));
            }
        } catch (System.Exception e) {
            _logger.Error($"CoopMinimap: hints failed:\n{e}");
        }
    }
}
