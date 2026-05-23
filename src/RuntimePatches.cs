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
    // Each Try* logs and continues on failure, so a missing class never blocks the mod.
    public static class RuntimePatches
    {
        public static void Apply(Harmony harmony, ILogger logger)
        {
            TryPatchDoorMesh(harmony, logger);
            TryPatchColSelBoxes(harmony, logger);
        }

        private static void TryPatchColSelBoxes(Harmony harmony, ILogger logger)
        {
            try
            {
                var type = ResolveType("Vintagestory.GameContent.BEBehaviorDoor");
                if (type == null) return;
                var prop = type.GetProperty("ColSelBoxes", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var getter = prop?.GetGetMethod(true);
                if (getter == null) return;

                var postfix = new HarmonyMethod(typeof(RuntimePatches).GetMethod(nameof(ColSelBoxesPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(getter, postfix: postfix);
                logger.Notification("[movedoors] patched BEBehaviorDoor.ColSelBoxes getter");
            }
            catch (Exception ex)
            {
                logger.Warning("[movedoors] TryPatchColSelBoxes failed: " + ex.Message);
            }
        }

        private static void ColSelBoxesPostfix(object __instance, ref Cuboidf[] __result)
        {
            var posProp = AccessTools.Property(__instance.GetType(), "Pos");
            BlockPos pos = posProp?.GetValue(__instance) as BlockPos;
            if (pos == null) return;
            var off = MoveDoorsModSystem.Offsets?.Get(pos);
            if (off == null || (off.X == 0 && off.Y == 0 && off.Z == 0)) return;
            if (__result == null) return;

            float dx = off.X / 16f, dy = off.Y / 16f, dz = off.Z / 16f;
            var shifted = new Cuboidf[__result.Length];
            for (int i = 0; i < __result.Length; i++)
            {
                var c = __result[i];
                shifted[i] = new Cuboidf(c.X1 + dx, c.Y1 + dy, c.Z1 + dz, c.X2 + dx, c.Y2 + dy, c.Z2 + dz);
            }
            __result = shifted;
        }

        private static void TryPatchDoorMesh(Harmony harmony, ILogger logger)
        {
            string[] candidates = {
                "Vintagestory.GameContent.BEBehaviorDoor",
                "Vintagestory.GameContent.BlockEntityDoor",
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

                    // One-time field dump so we can see the actual shape of the BE behavior.
                    DumpFields(type, logger);
                    return;
                }
                catch (Exception ex)
                {
                    logger.Warning("[movedoors] TryPatchDoorMesh(" + typeName + ") failed: " + ex.Message);
                }
            }
        }

        private static void DumpFields(Type type, ILogger logger)
        {
            var flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static;
            foreach (var f in type.GetFields(flags))
            {
                logger.Notification("[movedoors] BE field: " + f.Name + " : " + f.FieldType.Name
                    + (f.IsStatic ? " [STATIC]" : ""));
            }
            foreach (var p in type.GetProperties(flags))
            {
                logger.Notification("[movedoors] BE property: " + p.Name + " : " + p.PropertyType.Name);
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

        private static void DoorMeshPostfix(object __instance)
        {
            var posProp = AccessTools.Property(__instance.GetType(), "Pos");
            BlockPos pos = posProp?.GetValue(__instance) as BlockPos;
            if (pos == null) return;

            var off = MoveDoorsModSystem.Offsets?.Get(pos);
            if (off == null || (off.X == 0 && off.Y == 0 && off.Z == 0)) return;

            var f = AccessTools.Field(__instance.GetType(), "mesh");
            if (f?.GetValue(__instance) is not MeshData md) return;

            float dx = off.X / 16f, dy = off.Y / 16f, dz = off.Z / 16f;
            var clone = md.Clone();
            clone.Translate(dx, dy, dz);
            f.SetValue(__instance, clone);
        }
    }
}
