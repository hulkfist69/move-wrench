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

            var meshField = AccessTools.Field(type, "mesh");
            if (meshField == null) return;
            if (meshField.GetValue(__instance) is not MeshData currentMesh) return;

            var openedField = AccessTools.Field(type, "opened");
            bool opened = openedField?.GetValue(__instance) is bool ob && ob;

            string key = pos.X + ":" + pos.Y + ":" + pos.Z + ":" + (opened ? "1" : "0");

            var off = MoveDoorsModSystem.Offsets?.Get(pos);
            bool hasOffset = off != null && (off.X != 0 || off.Y != 0 || off.Z != 0);

            if (!hasOffset)
            {
                baselineMeshes.Remove(key);
                return;
            }

            if (!baselineMeshes.TryGetValue(key, out var baseline))
            {
                baseline = currentMesh.Clone();
                baselineMeshes[key] = baseline;
            }

            float dx = off.X / 8f;
            float dy = off.Y / 8f;
            float dz = off.Z / 8f;

            var rotateField = AccessTools.Field(type, "RotateYRad");
            float angle = rotateField?.GetValue(__instance) is float r ? r : 0f;
            float cos = (float)Math.Cos(angle);
            float sin = (float)Math.Sin(angle);
            float invDx = dx * cos - dz * sin;
            float invDz = dx * sin + dz * cos;

            // Also look up the facing — VS doors carry facingWhenClosed/Opened on the BE, and
            // mesh-local axes might be oriented relative to the door's facing rather than world.
            string facingStr = "?";
            try
            {
                var openedField2 = AccessTools.Field(type, "opened");
                bool isOpen = openedField2?.GetValue(__instance) is bool ob2 && ob2;
                var facingProp = AccessTools.Property(type, isOpen ? "facingWhenOpened" : "facingWhenClosed");
                var facing = facingProp?.GetValue(__instance);
                facingStr = facing?.ToString() ?? "?";
            }
            catch { }

            MoveDoorsModSystem.Logger?.Notification("[movedoors] mesh translate pos=" + pos
                + " off=" + off
                + " state=" + (opened ? "open" : "closed")
                + " facing=" + facingStr
                + " RotateYRad=" + angle.ToString("0.###")
                + " input(dx,dy,dz)=(" + dx.ToString("0.###") + "," + dy.ToString("0.###") + "," + dz.ToString("0.###") + ")"
                + " applied(invDx,dy,invDz)=(" + invDx.ToString("0.###") + "," + dy.ToString("0.###") + "," + invDz.ToString("0.###") + ")");

            var translated = baseline.Clone();
            translated.Translate(invDx, dy, invDz);
            meshField.SetValue(__instance, translated);
        }
    }
}
