using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace MoveDoors
{
    public static class DoorGroup
    {
        // Finds the paired half of a double door at an adjacent position.
        // Returns null when the door is single.
        public static BlockPos FindPaired(IBlockAccessor ba, BlockPos pos)
        {
            var here = ba.GetBlock(pos) as BlockDoor;
            if (here == null) return null;

            // Check four cardinal neighbors.
            foreach (var face in BlockFacing.HORIZONTALS)
            {
                var np = pos.AddCopy(face);
                if (!(ba.GetBlock(np) is BlockDoor neighbor)) continue;
                if (SameVariant(here, neighbor) && Mirrored(here, neighbor)) return np;
            }
            return null;
        }

        private static bool SameVariant(BlockDoor a, BlockDoor b)
        {
            // Same wood / material; differ only in hinge side.
            if (a.FirstCodePart() != b.FirstCodePart()) return false;
            int parts = System.Math.Min(a.Code.Path.Split('-').Length, b.Code.Path.Split('-').Length);
            for (int i = 0; i < parts - 1; i++)
            {
                if (a.Code.Path.Split('-')[i] != b.Code.Path.Split('-')[i]) return false;
            }
            return true;
        }

        private static bool Mirrored(BlockDoor a, BlockDoor b)
        {
            // A safe approximation: pair iff variants share material but final hinge token differs.
            var aLast = a.LastCodePart();
            var bLast = b.LastCodePart();
            return aLast != bLast;
        }
    }
}
