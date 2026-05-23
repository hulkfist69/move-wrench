using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MoveDoors
{
    internal static class WrenchHeld
    {
        public static bool IsHolding(IPlayer player)
        {
            return player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Item is ItemDoorWrench;
        }
    }

    // Helper for offset application to cuboid arrays.
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

    // BlockDoor: collision + selection offset.
    [HarmonyPatch(typeof(BlockDoor), nameof(BlockDoor.GetCollisionBoxes))]
    public static class Patch_BlockDoor_GetCollisionBoxes
    {
        public static void Postfix(IBlockAccessor blockAccessor, BlockPos pos, ref Cuboidf[] __result)
        {
            OffsetHelper.Apply(pos, ref __result);
        }
    }

    [HarmonyPatch(typeof(BlockDoor), nameof(BlockDoor.GetSelectionBoxes))]
    public static class Patch_BlockDoor_GetSelectionBoxes
    {
        public static void Postfix(IBlockAccessor blockAccessor, BlockPos pos, ref Cuboidf[] __result)
        {
            OffsetHelper.Apply(pos, ref __result);
        }
    }

    // BlockFenceGate: collision + selection offset.
    [HarmonyPatch(typeof(BlockFenceGate), nameof(BlockFenceGate.GetCollisionBoxes))]
    public static class Patch_BlockFenceGate_GetCollisionBoxes
    {
        public static void Postfix(IBlockAccessor blockAccessor, BlockPos pos, ref Cuboidf[] __result)
        {
            OffsetHelper.Apply(pos, ref __result);
        }
    }

    [HarmonyPatch(typeof(BlockFenceGate), nameof(BlockFenceGate.GetSelectionBoxes))]
    public static class Patch_BlockFenceGate_GetSelectionBoxes
    {
        public static void Postfix(IBlockAccessor blockAccessor, BlockPos pos, ref Cuboidf[] __result)
        {
            OffsetHelper.Apply(pos, ref __result);
        }
    }

    // Global short-circuit on Block.OnBlockInteractStart.
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockInteractStart))]
    public static class Patch_Block_OnBlockInteractStart
    {
        public static bool Prefix(Block __instance, IPlayer byPlayer, ref bool __result)
        {
            if (!WrenchHeld.IsHolding(byPlayer)) return true;
            if (!BlockOffsetManager.IsMovable(__instance)) return true;

            (byPlayer as Vintagestory.API.Server.IServerPlayer)?.SendMessage(
                Vintagestory.API.Config.GlobalConstants.GeneralChatGroup,
                "[movedoors] suppressed " + __instance.GetType().Name + " interact",
                Vintagestory.API.Common.EnumChatType.Notification);

            __result = false;
            return false;
        }
    }

    // Belt-and-braces: BlockDoor often overrides OnBlockInteractStart, so the base-class patch
    // above may not fire for door instances. Patch the override directly too.
    [HarmonyPatch(typeof(BlockDoor), nameof(BlockDoor.OnBlockInteractStart))]
    public static class Patch_BlockDoor_OnBlockInteractStart_Override
    {
        public static bool Prefix(IPlayer byPlayer, ref bool __result)
        {
            if (!WrenchHeld.IsHolding(byPlayer)) return true;
            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(BlockFenceGate), nameof(BlockFenceGate.OnBlockInteractStart))]
    public static class Patch_BlockFenceGate_OnBlockInteractStart_Override
    {
        public static bool Prefix(IPlayer byPlayer, ref bool __result)
        {
            if (!WrenchHeld.IsHolding(byPlayer)) return true;
            __result = false;
            return false;
        }
    }

    // NOTE: Door mesh translation is wired via runtime reflection in RuntimePatches.Apply(...)
    // so the build doesn't fail when BE class names differ between VS versions.
}
