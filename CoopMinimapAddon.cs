using SSMP.Api.Client;
using UnityEngine;

namespace CoopMinimap;

/// <summary>
/// SSMP client addon that improves Interactive Map for co-op: partners on the minimap and full map, a better
/// calibration of where the player is drawn, objectives marked as main or side, a teleport to the partner and a
/// rosary key (both can be turned off in CoopMinimap.cfg). Loaded by SSMP from its own plugin folder; it sends
/// nothing over the network.
/// </summary>
public class CoopMinimapAddon : ClientAddon {
    protected override string Name => "CoopMinimap";

    protected override string Version => "0.6.3";

    public override bool NeedsNetwork => false;

    public override uint ApiVersion => 1;

    public override void Initialize(IClientApi clientApi) {
        AddonSettings.Load(Logger);
        CoopGameFixes.Apply(Logger);

        var host = new GameObject("CoopMinimap");
        Object.DontDestroyOnLoad(host);

        host.AddComponent<MapCalibrationImprover>().Setup(Logger);
        host.AddComponent<ObjectiveHighlighter>().Setup(Logger);
        host.AddComponent<PartnerMarkerDrawer>().Setup(clientApi, Logger);
        host.AddComponent<PartnerTeleporter>().Setup(clientApi, Logger);
        host.AddComponent<RosaryHelper>().Setup(clientApi, Logger);
        host.AddComponent<ConnectHints>().Setup(clientApi, Logger);
        host.AddComponent<ClickThroughFix>().Setup(Logger);
        host.AddComponent<VanillaMinimap>().Setup(clientApi, Logger);
        host.AddComponent<PartnerDownAlert>().Setup(clientApi, Logger);

        Logger.Info("CoopMinimap 0.6.3 loaded: game-map minimap (F8 style, F10 show/hide), partners and objectives, " +
                    "downed-partner alert, calibration, F9 teleport and F6 rosaries (if on), click-through overlays");
    }
}
