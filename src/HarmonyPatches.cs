using System.Linq;
using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace MoveDoors
{
    internal static class WrenchHeld
    {
        // Matches the base game wrench (game:wrench) by item code, plus any item whose class
        // (or an ancestor class) is named "ItemWrench" — that covers third-party mod wrenches
        // that subclass the base wrench.
        public static bool IsHolding(IPlayer player)
        {
            var item = player?.InventoryManager?.ActiveHotbarSlot?.Itemstack?.Item;
            if (item == null) return false;

            var code = item.Code?.ToString();
            if (code == "game:wrench") return true;

            var t = item.GetType();
            while (t != null)
            {
                if (t.Name == "ItemWrench") return true;
                t = t.BaseType;
            }
            return false;
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

            if (held && BlockOffsetManager.IsMovable(__instance))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    // When a door is broken, clear any stored offset for that position so a freshly placed
    // door doesn't inherit the previous owner's offset.
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockBroken))]
    public static class Patch_Block_OnBlockBroken
    {
        public static void Postfix(Block __instance, IWorldAccessor world, BlockPos pos)
        {
            if (world?.Side != EnumAppSide.Server) return;
            if (!BlockOffsetManager.IsMovable(__instance)) return;
            MoveDoorsModSystem.Offsets?.OnBlockRemoved(pos);
        }
    }

    // Belt-and-braces: any time a block is placed, if there's a stored offset at that position,
    // clear it. Covers chunk-unload, /editmode replace, fill commands, and any removal path
    // that didn't run through OnBlockBroken.
    [HarmonyPatch(typeof(Block), nameof(Block.OnBlockPlaced))]
    public static class Patch_Block_OnBlockPlaced
    {
        public static void Postfix(IWorldAccessor world, BlockPos blockPos)
        {
            if (world?.Side != EnumAppSide.Server) return;
            if (blockPos == null) return;
            if (MoveDoorsModSystem.Offsets?.Get(blockPos) != null)
            {
                MoveDoorsModSystem.Offsets.OnBlockRemoved(blockPos);
            }
        }
    }
}
