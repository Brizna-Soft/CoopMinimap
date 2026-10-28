# CoopMinimap

A client add-on for the **Hollow Knight: Silksong** co-op mod (SSMP). It adds a minimap made from the game's own
map, shows your partner on every map, and warns you when your partner is down.

**0.6.3 is a beta:** it has not been played in a real co-op game yet (0.6.0 was). Download it from
[Releases](https://github.com/Brizna-Soft/CoopMinimap/releases). Page in Spanish, with pictures:
[brizna-soft.github.io/mods/coopminimap](https://brizna-soft.github.io/mods/coopminimap/).

*Español más abajo.*

## Features

- **Minimap from the game's map.** It looks like the game's quick map: the same scale, the game's compass icon for
  you, the area's name on top, and the same things drawn over the rooms (benches, shops and other map pins, quest
  markers, the markers you placed, and the names of the neighbouring areas at the exits, in the game's font and
  language). Like the quick map, it shows only the current area; set `zone_only=false` to see every area you have
  mapped. You and your partner are placed with the same maths the game uses for its compass, so you appear in the
  right room. F8 switches to Interactive Map's MapGenie minimap, F10 shows or hides it, Page Up / Page Down zoom.
- **Your partner on the maps**, with their name, on the minimap and on Interactive Map's full map. When they are out
  of view, an arrow on the minimap's edge points at them.
- **"Partner down" banner** while your partner waits in their cocoon: how many of the 5 hits it has had, and whether
  they are in another room. Their marker turns red.
- **Objectives:** orange ring = main objective, purple ring = side objective (from Interactive Map's data). The
  nearest main objective is named above the minimap, under the area's name.
- **Better player position on Interactive Map:** its calibration is refitted to the game's own map pins (benches,
  stations, vendors).
- **F9 — go to your partner** (on by default): moves you next to your partner when you are both in the same room, to
  get out of places you are stuck in.
- **F6 — +1000 rosaries** (off by default; turn it on in `CoopMinimap.cfg`).
- **Fixes:** an error in the game's `StopCrawler` action that co-op can trigger, and Interactive Map's invisible
  notification panel no longer blocks clicks in menus.
- Messages in Spanish when the game is in Spanish, in English otherwise.
- Sends nothing over the network: each player can use it or not.

## Requirements

- Hollow Knight: Silksong on PC (tested with game version 1.0.30000).
- SSMP, the multiplayer mod, in its co-op version by Candy-649
  ([silksong-coop-mod](https://github.com/Candy-649/SSMP/releases)), based on
  [SSMP by Extremelyd1](https://github.com/Extremelyd1/SSMP). CoopMinimap 0.6.3 is built for 0.4.135; 0.6.0 was
  played with 0.4.134.
- BepInEx 5, which the co-op mod's INSTALL.bat installs (tested with 5.4.23.4).
- [Interactive Map](https://www.nexusmods.com/hollowknightsilksong/mods/420) by Pilout (tested with 1.1.9).

## Install

1. Install the SSMP co-op mod (its INSTALL.bat also installs BepInEx) and Interactive Map first, following their own
   pages.
2. Close the game.
3. Download `CoopMinimap-<version>.zip` from [Releases](https://github.com/Brizna-Soft/CoopMinimap/releases) and
   extract it into the game's folder (the one with `Hollow Knight Silksong.exe`). `CoopMinimap.dll` must end up in
   the same folder as `SSMP.dll`, usually `BepInEx\plugins\SSMP\`.
4. Start the game. The add-on creates `CoopMinimap.cfg` next to the dll the first time. A few seconds after you
   connect, the chat lists its keys.

To see your partner when they are in another room, the host types `/set globalmapicons true` in the chat.

To uninstall, delete `CoopMinimap.dll` and `CoopMinimap.cfg`.

## Keys

| Key | What it does |
| --- | --- |
| F8 | Switch the minimap: game map / MapGenie |
| F10 | Show or hide the minimap |
| Page Up / Page Down (or keypad + / -) | Zoom the minimap |
| F9 | Go next to your partner (same room). Can be turned off |
| F6 | +1000 rosaries. Off unless you turn it on |

## Settings

`CoopMinimap.cfg`, next to the dll. Change it with the game closed (it is read when the game starts).

```
rosary_key=false     # F6 gives 1000 rosaries (true = on)
teleport_key=true    # F9 takes you to your partner in the same room
style=game           # game = minimap from the game's map, mapgenie = Interactive Map's minimap
visible=true         # show the minimap
zoom=1               # 0.25 to 4
zone_only=true       # only the current area's rooms, like the quick map (false = every area you have mapped)
```

## Building

You need the .NET SDK (8 or newer) and the game with BepInEx, SSMP and Interactive Map installed: the project
references their dlls, it does not include them.

```
dotnet build -c Release -p:GameDir="C:\path\to\Hollow Knight Silksong"
```

Or put your game folder in a `GameDir.props` file next to `CoopMinimap.csproj` (git ignores it):

```xml
<Project>
  <PropertyGroup>
    <GameDir>C:\path\to\Hollow Knight Silksong</GameDir>
  </PropertyGroup>
</Project>
```

The dll is written to `bin\Release\netstandard2.1\CoopMinimap.dll`.

## Credits

- **Team Cherry** for Hollow Knight: Silksong. This is a fan-made mod, not made or endorsed by Team Cherry.
- **Extremelyd1** and contributors for SSMP, and **Candy-649** for its co-op version.
- **Pilout** for Interactive Map (its MapGenie minimap and map data are part of Interactive Map).
- Made by **Brizna Soft**. The code was written with an AI assistant and tested by Brizna Soft in real co-op games.

## Questions

Open an [issue](https://github.com/Brizna-Soft/CoopMinimap/issues). Please don't take questions about CoopMinimap to
the authors of SSMP or Interactive Map.

## License

MIT (see [LICENSE](LICENSE)). The game, BepInEx, SSMP and Interactive Map have their own licenses and are not included.

---

# CoopMinimap (español)

Un complemento para el mod cooperativo de **Hollow Knight: Silksong** (SSMP). Agrega un minimapa hecho con el mapa
del propio juego, muestra a tu compañero en todos los mapas y te avisa cuando tu compañero cae.

**La 0.6.3 es beta:** todavía no se jugó en una partida cooperativa real (la 0.6.0 sí). Se baja en
[Releases](https://github.com/Brizna-Soft/CoopMinimap/releases). Página con fotos:
[brizna-soft.github.io/mods/coopminimap](https://brizna-soft.github.io/mods/coopminimap/).

## Qué hace

- **Minimapa con el mapa del juego.** Se ve como el mapa rápido del juego: la misma escala, el ícono de brújula del
  juego para ti, el nombre de la zona arriba y lo mismo dibujado sobre las salas (bancas, tiendas y otros pines,
  marcas de misión, las marcas que pusiste y los nombres de las zonas vecinas en las salidas, con la letra y el
  idioma del juego). Como el mapa rápido, muestra solo la zona actual; con `zone_only=false` se ven todas las zonas
  que tienes en el mapa. Te ubica a ti y a tu compañero con las mismas cuentas que usa el juego para su brújula, así
  salen en la sala correcta. F8 cambia al minimapa MapGenie de Interactive Map, F10 lo muestra u oculta, Re Pág /
  Av Pág hacen zoom.
- **Tu compañero en los mapas**, con su nombre, en el minimapa y en el mapa grande de Interactive Map. Si no se ve,
  una flecha en el borde del minimapa apunta hacia él.
- **Aviso de "compañero caído"** mientras espera en su capullo: cuántos de los 5 golpes lleva y si está en otra sala.
  Su marca se pone roja.
- **Objetivos:** anillo naranja = objetivo principal, morado = secundario (con los datos de Interactive Map). El
  objetivo principal más cercano sale escrito sobre el minimapa, debajo del nombre de la zona.
- **Mejor posición del jugador en Interactive Map:** su calibración se ajusta con los pines del mapa del juego
  (bancas, estaciones, tiendas).
- **F9: ir junto a tu compañero** (prendido de fábrica): te pone a su lado cuando están en la misma sala, para salir
  de un atasco.
- **F6: +1000 rosarios** (apagado de fábrica; se prende en `CoopMinimap.cfg`).
- **Arreglos:** un error de la acción `StopCrawler` del juego que el modo cooperativo puede provocar, y el panel
  invisible de avisos de Interactive Map ya no bloquea los clics en los menús.
- Mensajes en español si el juego está en español; si no, en inglés.
- No manda nada por la red: cada jugador decide si lo usa.

## Qué necesitas

- Hollow Knight: Silksong en PC (probado con la versión 1.0.30000 del juego).
- SSMP, el mod multijugador, en su versión cooperativa de Candy-649
  ([silksong-coop-mod](https://github.com/Candy-649/SSMP/releases)), basada en
  [SSMP de Extremelyd1](https://github.com/Extremelyd1/SSMP). CoopMinimap 0.6.3 está hecho para la 0.4.135; la 0.6.0
  se jugó con la 0.4.134.
- BepInEx 5, que instala el INSTALL.bat del mod cooperativo (probado con 5.4.23.4).
- [Interactive Map](https://www.nexusmods.com/hollowknightsilksong/mods/420) de Pilout (probado con 1.1.9).

## Cómo instalarlo

1. Instala primero el mod cooperativo de SSMP (su INSTALL.bat también instala BepInEx) e Interactive Map, siguiendo
   sus páginas.
2. Cierra el juego.
3. Baja `CoopMinimap-<versión>.zip` de [Releases](https://github.com/Brizna-Soft/CoopMinimap/releases) y
   descomprímelo en la carpeta del juego (la que tiene `Hollow Knight Silksong.exe`). `CoopMinimap.dll` tiene que
   quedar en la misma carpeta que `SSMP.dll`, normalmente `BepInEx\plugins\SSMP\`.
4. Abre el juego. La primera vez, el complemento crea `CoopMinimap.cfg` junto al dll. Unos segundos después de
   conectarte, el chat muestra sus teclas.

Para ver a tu compañero cuando está en otra sala, el anfitrión escribe en el chat `/set globalmapicons true`.

Para quitarlo, borra `CoopMinimap.dll` y `CoopMinimap.cfg`.

## Teclas

| Tecla | Qué hace |
| --- | --- |
| F8 | Cambia el minimapa: mapa del juego / MapGenie |
| F10 | Muestra u oculta el minimapa |
| Re Pág / Av Pág (o + / - del teclado numérico) | Zoom del minimapa |
| F9 | Ir junto a tu compañero (misma sala). Se puede apagar |
| F6 | +1000 rosarios. Apagado hasta que lo prendas |

## Ajustes

`CoopMinimap.cfg`, junto al dll. Cámbialo con el juego cerrado (se lee al abrir el juego).

```
rosary_key=false     # F6 da 1000 rosarios (true = prendido)
teleport_key=true    # F9 te lleva junto a tu compañero en la misma sala
style=game           # game = minimapa del mapa del juego, mapgenie = el de Interactive Map
visible=true         # mostrar el minimapa
zoom=1               # de 0.25 a 4
zone_only=true       # solo las salas de la zona actual, como el mapa rápido (false = todas las zonas)
```

## Créditos

- **Team Cherry**, por Hollow Knight: Silksong. Este es un mod hecho por fans; Team Cherry no lo hizo ni lo respalda.
- **Extremelyd1** y colaboradores, por SSMP, y **Candy-649**, por su versión cooperativa.
- **Pilout**, por Interactive Map (su minimapa MapGenie y los datos del mapa son parte de Interactive
  Map).
- Hecho por **Brizna Soft**. El código se escribió con un asistente de IA y Brizna Soft lo probó en partidas cooperativas reales.

## Preguntas

Abre un [issue](https://github.com/Brizna-Soft/CoopMinimap/issues). Por favor, no les preguntes por CoopMinimap a los
autores de SSMP ni de Interactive Map.

## Licencia

MIT (ver [LICENSE](LICENSE)). Las otras partes (el juego, BepInEx, SSMP, Interactive Map) tienen sus propias licencias y no vienen incluidas.
