using System;
using System.Globalization;
using System.IO;
using UnityEngine;
using ILogger = SSMP.Logging.ILogger;

namespace CoopMinimap;

/// <summary>
/// The addon's settings, kept in CoopMinimap.cfg next to the dll: whether the rosary and teleport keys work, and which
/// minimap is shown and how. The file is written with every setting and what it does, so players can change it with
/// a text editor. It is read when the game starts, so it is changed with the game closed.
/// </summary>
internal static class AddonSettings {
    /// <summary>F6 gives 1000 rosaries. Off unless the player turns it on, as it is a cheat.</summary>
    public static bool RosaryKey;

    /// <summary>F9 moves the player next to a partner in the same room, to get out of places they are stuck in.</summary>
    public static bool TeleportKey = true;

    /// <summary>The game-map minimap (true) or Interactive Map's MapGenie one (false).</summary>
    public static bool GameStyle = true;

    public static bool Visible = true;

    public static float Zoom = 1f;

    /// <summary>Like the game's quick map, only the current zone's rooms (false: every zone the player has mapped).</summary>
    public static bool ZoneOnly = true;

    private static ILogger? _logger;

    private static string FilePath =>
        Path.Combine(Path.GetDirectoryName(typeof(AddonSettings).Assembly.Location) ?? ".", "CoopMinimap.cfg");

    public static void Load(ILogger logger) {
        _logger = logger;
        try {
            if (File.Exists(FilePath)) {
                foreach (var line in File.ReadAllLines(FilePath)) {
                    var trimmed = line.Trim();
                    if (trimmed.Length == 0 || trimmed.StartsWith("#")) {
                        continue;
                    }

                    var parts = trimmed.Split(['='], 2);
                    if (parts.Length != 2) {
                        continue;
                    }

                    // A note after the value ("rosary_key=true  # F6") is not part of it
                    var value = parts[1].Split('#')[0].Trim();
                    switch (parts[0].Trim().ToLowerInvariant()) {
                        case "rosary_key":
                            RosaryKey = ParseBool(value, RosaryKey);
                            break;
                        case "teleport_key":
                            TeleportKey = ParseBool(value, TeleportKey);
                            break;
                        case "style":
                            GameStyle = value != "mapgenie";
                            break;
                        case "visible":
                            Visible = ParseBool(value, Visible);
                            break;
                        case "zone_only":
                            ZoneOnly = ParseBool(value, ZoneOnly);
                            break;
                        case "zoom":
                            if (float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var zoom)) {
                                Zoom = Mathf.Clamp(zoom, 0.25f, 4f);
                            }

                            break;
                    }
                }
            }
        } catch (Exception e) {
            logger.Info($"CoopMinimap: could not read settings: {e.Message}");
        }

        // Written back every start, so a new file or settings added by a newer version show up for players to edit
        Save();
        logger.Info($"CoopMinimap: settings: F6 rosaries {(RosaryKey ? "on" : "off")}, " +
                    $"F9 teleport {(TeleportKey ? "on" : "off")}");
    }

    public static void Save() {
        try {
            File.WriteAllLines(FilePath, [
                "# CoopMinimap settings / ajustes",
                "# Change values with the game closed. / Cambia los valores con el juego cerrado.",
                "#",
                "# rosary_key: F6 gives 1000 rosaries (true = on, false = off).",
                "#             F6 da 1000 rosarios (true = prendido, false = apagado).",
                "rosary_key=" + Format(RosaryKey),
                "# teleport_key: F9 takes you next to your partner when you are in the same room.",
                "#               F9 te lleva junto a tu compañero cuando están en la misma sala.",
                "teleport_key=" + Format(TeleportKey),
                "# style: game = minimap made from the game's map, mapgenie = Interactive Map's minimap (F8 switches).",
                "#        game = minimapa hecho con el mapa del juego, mapgenie = el de Interactive Map (F8 cambia).",
                "style=" + (GameStyle ? "game" : "mapgenie"),
                "# visible: show the minimap (F10 switches). / mostrar el minimapa (F10 cambia).",
                "visible=" + Format(Visible),
                "# zoom: 0.25 to 4 (Page Up / Page Down). / de 0.25 a 4 (Re Pág / Av Pág).",
                "zoom=" + Zoom.ToString(CultureInfo.InvariantCulture),
                "# zone_only: true = only the current zone's rooms, like the game's quick map; false = every zone mapped.",
                "#            true = solo las salas de la zona actual, como el mapa rápido; false = todas las zonas.",
                "zone_only=" + Format(ZoneOnly)
            ]);
        } catch (Exception e) {
            _logger?.Info($"CoopMinimap: could not save settings: {e.Message}");
        }
    }

    private static string Format(bool value) => value ? "true" : "false";

    private static bool ParseBool(string value, bool fallback) {
        switch (value.ToLowerInvariant()) {
            case "true":
            case "on":
            case "yes":
            case "si":
            case "sí":
            case "1":
                return true;
            case "false":
            case "off":
            case "no":
            case "0":
                return false;
            default:
                return fallback;
        }
    }
}
