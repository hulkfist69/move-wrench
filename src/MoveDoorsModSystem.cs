using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace MoveDoors
{
    public enum WrenchMode { Rotate, Move1, Move2, Move4 }

    public class MoveDoorsModSystem : ModSystem
    {
        public const string HarmonyId = "movedoors.patches";
        public const string NetworkChannel = "movedoors";
        public const string StepHotkey = "movedoors:stepmode";

        private Harmony harmony;
        private ICoreClientAPI capi;
        private GuiDialogMoveStep stepDialog;

        public static BlockOffsetManager Offsets { get; private set; }
        public static ILogger Logger { get; private set; }

        private static WrenchMode clientMode = WrenchMode.Rotate;
        public static WrenchMode GetClientMode() => clientMode;
        public static void SetClientMode(WrenchMode m) => clientMode = m;

        public static int GetClientStep() => clientMode switch
        {
            WrenchMode.Move1 => 1,
            WrenchMode.Move2 => 2,
            WrenchMode.Move4 => 4,
            _ => 0
        };

        public override void Start(ICoreAPI api)
        {
            Logger = api.Logger;
            api.Logger.Notification("[movedoors] starting v" + BuildInfo.Version + " " + BuildInfo.Sha + " (" + BuildInfo.Stamp + ") side=" + api.Side);

            harmony = new Harmony(HarmonyId);
            try
            {
                harmony.PatchAll(typeof(MoveDoorsModSystem).Assembly);
                api.Logger.Notification("[movedoors] PatchAll succeeded");
            }
            catch (System.Exception ex)
            {
                api.Logger.Error("[movedoors] PatchAll FAILED: " + ex);
            }

            RuntimePatches.Apply(harmony, api.Logger);
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            Offsets = new BlockOffsetManager();
            Offsets.InitServer(sapi);
        }

        private long retargetTickId;

        public override void StartClientSide(ICoreClientAPI capi)
        {
            this.capi = capi;
            Offsets = new BlockOffsetManager();
            Offsets.InitClient(capi);

            capi.Input.RegisterHotKey(StepHotkey, Lang.Get("movedoors:hotkey-stepmode"), GlKeys.F, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler(StepHotkey, ToggleStepDialog);

            // Per-frame retargeting of the player's CurrentBlockSelection: if their gaze ray
            // hits a shifted door's bounding box closer than VS's current selection, replace it.
            retargetTickId = capi.Event.RegisterGameTickListener(_ => RuntimePatches.RetargetPlayerSelection(capi), 0);
        }

        private bool ToggleStepDialog(KeyCombination _)
        {
            if (capi == null) return false;
            if (!WrenchHeld.IsHolding(capi.World.Player)) return false;

            if (stepDialog == null) stepDialog = new GuiDialogMoveStep(capi);
            if (stepDialog.IsOpened()) stepDialog.TryClose();
            else stepDialog.TryOpen();
            return true;
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            if (capi != null && retargetTickId != 0)
            {
                capi.Event.UnregisterGameTickListener(retargetTickId);
                retargetTickId = 0;
            }
            stepDialog?.TryClose();
            stepDialog = null;
            RuntimePatches.ClearAll();
            Offsets = null;
        }
    }
}
