using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace MoveDoors
{
    internal static class WrenchHeld
    {
        public static bool IsHolding(IPlayer player)
        {
            return player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Item is ItemDoorWrench;
        }
    }

    internal static class OffsetHelper
    {
        public static void Apply(BlockPos pos, ref Cuboidf[] boxes)
        {
            var off = MoveDoorsModSystem.Offsets?.Get(pos);
            if (off == null || (off.X == 0 && off.Y == 0 && off.Z == 0) || boxes == null) return;

            float dx = off.X / 16f, dy = off.Y / 16f, dz = off.Z / 16f;
            var shifted = new Cuboidf[boxes.Length];
            for (int i = 0; i < boxes.Length; i++)
            {
                var c = boxes[i];
                shifted[i] = new Cuboidf(c.X1 + dx, c.Y1 + dy, c.Z1 + dz, c.X2 + dx, c.Y2 + dy, c.Z2 + dz);
            }
            boxes = shifted;
        }
    }

    // All patches target the base Block class — subclasses (BlockDoor, BlockTrapdoor, BlockFenceGate)
    // inherit these methods without overriding them in VS 1.22. Each patch gates by IsMovable so we
    // only affect doors/trapdoors/fence gates.

    [HarmonyPatch(typeof(Block), nameof(Block.GetCollisionBoxes))]
    public static class Patch_Block_GetCollisionBoxes
    {
        public static void Postfix(Block __instance, BlockPos pos, ref Cuboidf[] __result)
        {
            if (!BlockOffsetManager.IsMovable(__instance)) return;
            OffsetHelper.Apply(pos, ref __result);
        }
    }

    [HarmonyPatch(typeof(Block), nameof(Block.GetSelectionBoxes))]
    public static class Patch_Block_GetSelectionBoxes
    {
        public static void Postfix(Block __instance, BlockPos pos, ref Cuboidf[] __result)
        {
            if (!BlockOffsetManager.IsMovable(__instance)) return;
            OffsetHelper.Apply(pos, ref __result);
        }
    }

    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockInteractStart))]
    public static class Patch_Block_OnBlockInteractStart
    {
        public static bool Prefix(Block __instance, IWorldAccessor world, IPlayer byPlayer, ref bool __result)
        {
            bool held = WrenchHeld.IsHolding(byPlayer);

            if (held)
            {
                // Log EVERY interact when the wrench is in hand, regardless of movable status,
                // so we can see exactly what class the target block is.
                bool movable = BlockOffsetManager.IsMovable(__instance);
                var behaviorNames = __instance.BlockEntityBehaviors == null
                    ? ""
                    : string.Join(",", __instance.BlockEntityBehaviors.Select(b => b?.Name ?? "?"));
                world.Logger.Notification("[movedoors] interact"
                    + " class=" + __instance.GetType().Name
                    + " code=" + __instance.Code
                    + " movable=" + movable
                    + " beBehaviors=[" + behaviorNames + "]");

                if (movable)
                {
                    __result = false;
                    return false;
                }
            }

            return true;
        }
    }
}
