using System;
using System.Collections.Generic;
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
        // Caches the un-translated baseline mesh per (pos, opened-state). Re-tesselation triggered
        // by neighbor doors would otherwise read OUR already-translated clone and translate again,
        // compounding the offset. By always translating from a stored baseline, the result is stable.
        private static readonly Dictionary<string, MeshData> baselineMeshes = new Dictionary<string, MeshData>();

        public static void ClearBaseline(BlockPos pos)
        {
            if (pos == null) return;
            var prefix = pos.X + ":" + pos.Y + ":" + pos.Z + ":";
            var keys = baselineMeshes.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var k in keys) baselineMeshes.Remove(k);
        }

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

                    // Get animUtil's type directly from the field, not by guessing namespace.
                    var animUtilField = AccessTools.Field(type, "animUtil");
                    if (animUtilField != null)
                    {
                        var animType = animUtilField.FieldType;
                        logger.Notification("[movedoors] animUtil type: " + animType.FullName);
                        logger.Notification("[movedoors] --- " + animType.Name + " structure ---");
                        DumpFields(animType, logger);

                        foreach (var fi in animType.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                        {
                            var ft = fi.FieldType;
                            if (ft.Name.Contains("Renderer") || ft.Name.Contains("Animator") || ft.Name.Contains("Matrix"))
                            {
                                logger.Notification("[movedoors] --- nested " + ft.FullName + " (via field " + fi.Name + ") ---");
                                DumpFields(ft, logger);
                            }
                        }
                    }

                    // Also dump BlockEntityAnimationUtil — animations may render via that path,
                    // bypassing the BE's mesh field.
                    var animUtilType = ResolveType("Vintagestory.API.Common.BlockEntityAnimationUtil");
                    if (animUtilType != null)
                    {
                        logger.Notification("[movedoors] --- BlockEntityAnimationUtil structure ---");
                        DumpFields(animUtilType, logger);
                    }
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
            var type = __instance.GetType();
            BlockPos pos = AccessTools.Property(type, "Pos")?.GetValue(__instance) as BlockPos;
            if (pos == null) return;

            var animUtilField = AccessTools.Field(type, "animUtil");
            var animUtil = animUtilField?.GetValue(__instance);
            if (animUtil == null) return;

            var rendererField = AccessTools.Field(animUtil.GetType(), "renderer");
            var renderer = rendererField?.GetValue(animUtil);
            if (renderer == null) return;

            // Shift the renderer's world-space position by our offset. The renderer draws the
            // door at this position, and all of VS's rotation / animation / scale transforms
            // happen relative to it — so the entire visible door rides along, naturally matching
            // the hitbox for closed AND open states.
            var posField = AccessTools.Field(renderer.GetType(), "pos");
            if (posField == null) return;

            var off = MoveDoorsModSystem.Offsets?.Get(pos);
            bool hasOffset = off != null && (off.X != 0 || off.Y != 0 || off.Z != 0);

            // Doubled magnitude: /8 instead of /16. Empirically the renderer's pos shift lands
            // the visual at half the requested world distance — same scale factor pattern we hit
            // with mesh translation earlier. Doubling the input compensates so the visual lands
            // exactly at the hitbox position for both closed and open states.
            Vec3d target = hasOffset
                ? new Vec3d(pos.X + off.X / 8.0, pos.Y + off.Y / 8.0, pos.Z + off.Z / 8.0)
                : new Vec3d(pos.X, pos.Y, pos.Z);

            posField.SetValue(renderer, target);
        }
    }
}
