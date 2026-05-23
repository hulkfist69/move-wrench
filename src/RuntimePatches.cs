using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace MoveDoors
{
    // Reflection-driven Harmony patches for VS classes whose names vary between versions.
    // Each TryPatch logs and continues on failure, so a missing class never blocks the mod.
    public static class RuntimePatches
    {
        public static void Apply(Harmony harmony, ILogger logger)
        {
            TryPatchBoxes(harmony, logger, "Vintagestory.GameContent.BlockTrapDoor");
            TryPatchBoxes(harmony, logger, "Vintagestory.GameContent.BlockTrapdoor");
            TryPatchDoorMesh(harmony, logger);

            // Doors may open via a BlockBehavior (different namespace/name across versions).
            // Try each candidate; silently no-op if not present.
            string[] behaviorCandidates = {
                "Vintagestory.GameContent.BlockBehaviorDoor",
                "Vintagestory.GameContent.BehaviorDoor",
                "Vintagestory.GameContent.BlockBehaviorOpenableContainer",
                "Vintagestory.GameContent.BlockBehaviorDoorFence",
            };
            foreach (var t in behaviorCandidates) TryPatchBehaviorInteract(harmony, logger, t);
        }

        private static void TryPatchBehaviorInteract(Harmony harmony, ILogger logger, string typeName)
        {
            try
            {
                var type = ResolveType(typeName);
                if (type == null) return;

                // Behavior signature is OnBlockInteractStart(IWorldAccessor, IPlayer, BlockSelection, ref EnumHandling).
                var method = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .FirstOrDefault(m => m.Name == "OnBlockInteractStart");
                if (method == null) return;

                var prefix = new HarmonyMethod(typeof(RuntimePatches).GetMethod(nameof(BehaviorInteractPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(method, prefix: prefix);
                logger.Notification("[movedoors] patched " + typeName + ".OnBlockInteractStart");
            }
            catch (Exception ex)
            {
                logger.Warning("[movedoors] TryPatchBehaviorInteract(" + typeName + ") failed: " + ex.Message);
            }
        }

        // Generic prefix: drops out early if wrench is held.
        private static bool BehaviorInteractPrefix(IPlayer byPlayer)
        {
            return !WrenchHeld.IsHolding(byPlayer);
        }

        private static void TryPatchBoxes(Harmony harmony, ILogger logger, string typeName)
        {
            try
            {
                var type = ResolveType(typeName);
                if (type == null) return;

                foreach (var methodName in new[] { "GetCollisionBoxes", "GetSelectionBoxes" })
                {
                    var method = AccessTools.Method(type, methodName,
                        new[] { typeof(IBlockAccessor), typeof(BlockPos) });
                    if (method == null) continue;

                    var postfix = new HarmonyMethod(typeof(RuntimePatches).GetMethod(nameof(BoxesPostfix),
                        BindingFlags.Static | BindingFlags.NonPublic));
                    harmony.Patch(method, postfix: postfix);
                    logger.Notification("[movedoors] patched " + typeName + "." + methodName);
                }
            }
            catch (Exception ex)
            {
                logger.Warning("[movedoors] TryPatchBoxes(" + typeName + ") failed: " + ex.Message);
            }
        }

        private static void TryPatchDoorMesh(Harmony harmony, ILogger logger)
        {
            // VS 1.20 may expose door rendering via BlockEntityDoor, BlockEntityBehaviorDoor, or
            // an inline mesh on BlockDoor. We probe a few likely candidates.
            string[] candidates = {
                "Vintagestory.GameContent.BlockEntityDoor",
                "Vintagestory.GameContent.BEBehaviorDoor",
                "Vintagestory.GameContent.BlockEntityBehaviorDoor"
            };

            foreach (var typeName in candidates)
            {
                try
                {
                    var type = ResolveType(typeName);
                    if (type == null) continue;

                    var method = AccessTools.Method(type, "OnTesselation")
                              ?? AccessTools.Method(type, "OnTesselated");
                    if (method == null) continue;

                    var postfix = new HarmonyMethod(typeof(RuntimePatches).GetMethod(nameof(DoorMeshPostfix),
                        BindingFlags.Static | BindingFlags.NonPublic));
                    harmony.Patch(method, postfix: postfix);
                    logger.Notification("[movedoors] patched " + typeName + ".OnTesselation");
                    return;
                }
                catch (Exception ex)
                {
                    logger.Warning("[movedoors] TryPatchDoorMesh(" + typeName + ") failed: " + ex.Message);
                }
            }
        }

        private static Type ResolveType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }

        private static void BoxesPostfix(BlockPos pos, ref Cuboidf[] __result)
        {
            OffsetHelper.Apply(pos, ref __result);
        }

        private static void DoorMeshPostfix(object __instance)
        {
            // __instance is a BlockEntity-ish object with .Pos and a "mesh" / "Mesh" field.
            var posField = AccessTools.Property(__instance.GetType(), "Pos");
            if (posField?.GetValue(__instance) is not BlockPos pos) return;

            var off = MoveDoorsModSystem.Offsets?.Get(pos);
            if (off == null || (off.X == 0 && off.Y == 0 && off.Z == 0)) return;

            float dx = off.X / 16f, dy = off.Y / 16f, dz = off.Z / 16f;
            foreach (var fname in new[] { "mesh", "Mesh", "doorMesh" })
            {
                var f = AccessTools.Field(__instance.GetType(), fname);
                if (f?.GetValue(__instance) is MeshData md)
                {
                    md.Translate(dx, dy, dz);
                    return;
                }
            }
        }
    }
}
