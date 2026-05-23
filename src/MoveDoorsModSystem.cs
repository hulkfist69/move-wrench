using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace MoveDoors
{
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

        private static int clientStep = 1;
        public static int GetClientStep() => clientStep;
        public static void SetClientStep(int s)
        {
            if (s == 1 || s == 2 || s == 4) clientStep = s;
        }

        public override void Start(ICoreAPI api)
        {
            Logger = api.Logger;
            api.RegisterItemClass("ItemDoorWrench", typeof(ItemDoorWrench));

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

            foreach (var m in harmony.GetPatchedMethods())
            {
                api.Logger.Notification("[movedoors] patched method: " + m.DeclaringType?.FullName + "." + m.Name);
            }
        }

        public override void StartServerSide(ICoreServerAPI sapi)
        {
            Offsets = new BlockOffsetManager();
            Offsets.InitServer(sapi);
        }

        public override void StartClientSide(ICoreClientAPI capi)
        {
            this.capi = capi;
            Offsets = new BlockOffsetManager();
            Offsets.InitClient(capi);

            capi.Input.RegisterHotKey(StepHotkey, Lang.Get("movedoors:hotkey-stepmode"), GlKeys.F, HotkeyType.GUIOrOtherControls);
            capi.Input.SetHotKeyHandler(StepHotkey, ToggleStepDialog);
        }

        private bool ToggleStepDialog(KeyCombination _)
        {
            if (capi == null) return false;

            var active = capi.World.Player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Item;
            if (active is not ItemDoorWrench) return false;

            if (stepDialog == null) stepDialog = new GuiDialogMoveStep(capi);
            if (stepDialog.IsOpened()) stepDialog.TryClose();
            else stepDialog.TryOpen();
            return true;
        }

        public override void Dispose()
        {
            harmony?.UnpatchAll(HarmonyId);
            harmony = null;
            stepDialog?.TryClose();
            stepDialog = null;
            Offsets = null;
        }
    }
}
