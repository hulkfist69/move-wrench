using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MoveDoors
{
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

    // BlockDoor: collision + selection offset (compile-safe baseline).
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

    // NOTE: Door visual mesh translation and trapdoor support are wired via runtime reflection in
    // RuntimePatches.Apply(...) rather than [HarmonyPatch] attributes, so the build doesn't fail
    // when class names differ between VS versions.
}
