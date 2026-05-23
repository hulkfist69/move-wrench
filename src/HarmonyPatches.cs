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
        private static int interactLogCount = 0;
        private static int postfixLogCount = 0;
        // Dedup: when a click triggers Block.OnBlockInteractStart multiple times in rapid
        // succession for the same pos (we see 4-8 fires per click in logs), the door toggles
        // open/close repeatedly and nets to no change. Skip the duplicates.
        private static long lastInteractTickMs = 0;
        private static long lastInteractPosHash = 0;
        private const long DedupWindowMs = 100;

        public static bool Prefix(Block __instance, IWorldAccessor world, IPlayer byPlayer, BlockSelection blockSel, ref bool __result)
        {
            bool movable = BlockOffsetManager.IsMovable(__instance);

            if (movable && blockSel?.Position != null)
            {
                long now = System.Environment.TickCount64;
                long posHash = ((long)blockSel.Position.X << 40) ^ ((long)blockSel.Position.Y << 20) ^ blockSel.Position.Z;
                if (posHash == lastInteractPosHash && now - lastInteractTickMs < DedupWindowMs)
                {
                    // Treat as already-handled and skip the real call entirely.
                    __result = true;
                    return false;
                }
                lastInteractTickMs = now;
                lastInteractPosHash = posHash;
            }

            if (movable && interactLogCount < 8)
            {
                interactLogCount++;
                MoveDoorsModSystem.Logger?.Notification("[movedoors] Block.OnBlockInteractStart fired: "
                    + __instance.Code + " side=" + world.Side
                    + " wrenchHeld=" + WrenchHeld.IsHolding(byPlayer)
                    + " sel.Pos=" + blockSel?.Position?.ToString()
                    + " sel.HitPos=" + blockSel?.HitPosition?.ToString());
            }

            bool held = WrenchHeld.IsHolding(byPlayer);
            if (held && movable)
            {
                __result = false;
                return false;
            }

            return true;
        }

        public static void Postfix(Block __instance, IWorldAccessor world, IPlayer byPlayer, ref bool __result)
        {
            if (BlockOffsetManager.IsMovable(__instance) && postfixLogCount < 8)
            {
                postfixLogCount++;
                MoveDoorsModSystem.Logger?.Notification("[movedoors] Block.OnBlockInteractStart returned: "
                    + __result + " (door=" + __instance.Code + " side=" + world.Side + ")");
            }
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
