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

    // ---- BlockDoor ----

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

    // Door BE mesh translation. We translate the cached mesh by the stored offset on each re-tess,
    // so the rendered door tracks its shifted collision/selection.
    [HarmonyPatch(typeof(BlockEntityDoor), "OnTesselation")]
    public static class Patch_BlockEntityDoor_OnTesselation
    {
        public static void Postfix(BlockEntityDoor __instance)
        {
            var off = MoveDoorsModSystem.Offsets?.Get(__instance.Pos);
            if (off == null || (off.X == 0 && off.Y == 0 && off.Z == 0)) return;

            float dx = off.X / 16f, dy = off.Y / 16f, dz = off.Z / 16f;

            // Try common cached-mesh field names. VS 1.20 BlockEntityDoor uses "mesh" for the
            // generated MeshData. If the field name shifts in a point release, this no-ops gracefully.
            var fld = AccessTools.Field(typeof(BlockEntityDoor), "mesh")
                   ?? AccessTools.Field(typeof(BlockEntityDoor), "Mesh");
            if (fld == null) return;

            if (fld.GetValue(__instance) is MeshData md)
            {
                md.Translate(dx, dy, dz);
            }
        }
    }

    // ---- BlockTrapDoor ----

    [HarmonyPatch(typeof(BlockTrapDoor), nameof(BlockTrapDoor.GetCollisionBoxes))]
    public static class Patch_BlockTrapDoor_GetCollisionBoxes
    {
        public static void Postfix(IBlockAccessor blockAccessor, BlockPos pos, ref Cuboidf[] __result)
        {
            OffsetHelper.Apply(pos, ref __result);
        }
    }

    [HarmonyPatch(typeof(BlockTrapDoor), nameof(BlockTrapDoor.GetSelectionBoxes))]
    public static class Patch_BlockTrapDoor_GetSelectionBoxes
    {
        public static void Postfix(IBlockAccessor blockAccessor, BlockPos pos, ref Cuboidf[] __result)
        {
            OffsetHelper.Apply(pos, ref __result);
        }
    }

    // ---- BlockFenceGate ----

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
}
