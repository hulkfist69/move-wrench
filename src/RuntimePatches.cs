using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace MoveDoors
{
    public static class RuntimePatches
    {
        // Caches the un-translated baseline mesh per (pos, opened-state). Re-tesselation triggered
        // by neighbor doors would otherwise read OUR already-translated clone and translate again.
        private static readonly Dictionary<string, MeshData> baselineMeshes = new Dictionary<string, MeshData>();

        public static void Apply(Harmony harmony, ILogger logger)
        {
            TryPatchDoorTess(harmony, logger);
            TryPatchColSelBoxes(harmony, logger);
            TryPatchWrenchInteract(harmony, logger);
        }

        public static void ClearAll()
        {
            baselineMeshes.Clear();
        }

        public static void ClearBaseline(BlockPos pos)
        {
            if (pos == null) return;
            var prefix = pos.X + ":" + pos.Y + ":" + pos.Z + ":";
            var keys = baselineMeshes.Keys.Where(k => k.StartsWith(prefix)).ToList();
            foreach (var k in keys) baselineMeshes.Remove(k);
        }

        // ----- BE door tesselation postfix -----

        private static void TryPatchDoorTess(Harmony harmony, ILogger logger)
        {
            try
            {
                var type = ResolveType("Vintagestory.GameContent.BEBehaviorDoor");
                if (type == null) return;
                var method = AccessTools.Method(type, "OnTesselation");
                if (method == null) return;

                var postfix = new HarmonyMethod(typeof(RuntimePatches).GetMethod(nameof(DoorTessPostfix),
                    BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(method, postfix: postfix);
                logger.Notification("[movedoors] patched BEBehaviorDoor.OnTesselation");
            }
            catch (Exception ex)
            {
                logger.Warning("[movedoors] TryPatchDoorTess failed: " + ex.Message);
            }
        }

        private static void DoorTessPostfix(object __instance)
        {
            try
            {
                ApplyOffsetToDoorBehavior(__instance);
            }
            catch (Exception ex)
            {
                MoveDoorsModSystem.Logger?.Warning("[movedoors] DoorTessPostfix threw: " + ex.Message);
            }
        }

        // Reads the offset for this door's position and applies it via two paths:
        //   - closed-state chunk batch render: translate BE.mesh
        //   - open-state animUtil render: shift renderer.pos
        // Either path is gated on the door's current state so they never compound.
        public static void ApplyOffsetToDoorBehavior(object behavior)
        {
            if (behavior == null) return;
            var type = behavior.GetType();

            BlockPos pos = AccessTools.Property(type, "Pos")?.GetValue(behavior) as BlockPos;
            if (pos == null) return;

            var off = MoveDoorsModSystem.Offsets?.Get(pos);
            bool hasOffset = off != null && (off.X != 0 || off.Y != 0 || off.Z != 0);

            var openedField = AccessTools.Field(type, "opened");
            bool opened = openedField?.GetValue(behavior) is bool ob && ob;

            // ----- renderer.pos shift (open / animation path) -----
            var animUtilField = AccessTools.Field(type, "animUtil");
            var animUtil = animUtilField?.GetValue(behavior);
            if (animUtil != null)
            {
                var rendererField = AccessTools.Field(animUtil.GetType(), "renderer");
                var renderer = rendererField?.GetValue(animUtil);
                if (renderer != null)
                {
                    var posField = AccessTools.Field(renderer.GetType(), "pos");
                    if (posField != null)
                    {
                        Vec3d target = hasOffset
                            ? new Vec3d(pos.X + off.X / 8.0, pos.Y + off.Y / 8.0, pos.Z + off.Z / 8.0)
                            : new Vec3d(pos.X, pos.Y, pos.Z);
                        posField.SetValue(renderer, target);
                    }
                }
            }

            // ----- BE.mesh translation (closed-state chunk batch path) -----
            // Only do this when the door is in the CLOSED state, so the open mesh isn't
            // double-shifted (open uses renderer.pos above). Defensive: never write a null or
            // zero-vertex mesh back to the field.
            if (!opened)
            {
                var meshField = AccessTools.Field(type, "mesh");
                if (meshField?.GetValue(behavior) is MeshData currentMesh && currentMesh.VerticesCount > 0)
                {
                    string closedKey = pos.X + ":" + pos.Y + ":" + pos.Z + ":closed";
                    if (!baselineMeshes.TryGetValue(closedKey, out var baseline) || baseline == null || baseline.VerticesCount == 0)
                    {
                        baseline = currentMesh.Clone();
                        baselineMeshes[closedKey] = baseline;
                    }

                    if (hasOffset)
                    {
                        var translated = baseline.Clone();
                        translated.Translate(off.X / 8f, off.Y / 8f, off.Z / 8f);
                        if (translated.VerticesCount > 0) meshField.SetValue(behavior, translated);
                    }
                    else
                    {
                        var fresh = baseline.Clone();
                        if (fresh.VerticesCount > 0) meshField.SetValue(behavior, fresh);
                    }
                }
            }
        }

        // ----- ColSelBoxes (hitbox) postfix -----

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

        // ----- Base game wrench interact prefix -----

        private static void TryPatchWrenchInteract(Harmony harmony, ILogger logger)
        {
            try
            {
                var type = ResolveType("Vintagestory.GameContent.ItemWrench");
                if (type == null)
                {
                    logger.Warning("[movedoors] ItemWrench class not found — base wrench integration disabled");
                    return;
                }
                var method = AccessTools.Method(type, "OnHeldInteractStart");
                if (method == null) return;

                var prefix = new HarmonyMethod(typeof(RuntimePatches).GetMethod(nameof(WrenchInteractPrefix),
                    BindingFlags.Static | BindingFlags.NonPublic));
                harmony.Patch(method, prefix: prefix);
                logger.Notification("[movedoors] patched ItemWrench.OnHeldInteractStart");
            }
            catch (Exception ex)
            {
                logger.Warning("[movedoors] TryPatchWrenchInteract failed: " + ex.Message);
            }
        }

        // Dispatched per the client's current WrenchMode:
        //   - Rotate mode: pass through, let vanilla wrench rotation run.
        //   - Move1/2/4: if target is movable, apply offset and skip vanilla rotation.
        //                if target is not movable, pass through (vanilla rotation still applies).
        private static bool WrenchInteractPrefix(ItemSlot slot, EntityAgent byEntity,
            BlockSelection blockSel, EntitySelection entitySel, bool firstEvent,
            ref EnumHandHandling handling)
        {
            if (!firstEvent || blockSel == null) return true;

            var mode = MoveDoorsModSystem.GetClientMode();
            if (mode == WrenchMode.Rotate) return true;

            var world = byEntity.World;
            var block = world.BlockAccessor.GetBlock(blockSel.Position);
            if (!BlockOffsetManager.IsMovable(block)) return true;

            bool reset = byEntity.Controls.Sneak;
            var face = blockSel.Face ?? BlockFacing.UP;

            if (world.Side == EnumAppSide.Client)
            {
                int step = MoveDoorsModSystem.GetClientStep();
                if (step > 0)
                {
                    MoveDoorsModSystem.Offsets?.ClientSendInteract(blockSel.Position, face, reset, step);
                    (world.Api as ICoreClientAPI)?.World.Player.TriggerFpAnimation(EnumHandInteract.HeldItemInteract);
                }
            }

            handling = EnumHandHandling.PreventDefault;
            return false;
        }

        // ----- Helper to apply offset directly to a BE (used after world-load sync) -----

        public static void ForceApplyAt(IWorldAccessor world, BlockPos pos)
        {
            if (world == null || pos == null) return;
            var be = world.BlockAccessor.GetBlockEntity(pos);
            if (be?.Behaviors == null) return;
            foreach (var beh in be.Behaviors)
            {
                if (beh?.GetType().Name == "BEBehaviorDoor")
                {
                    ApplyOffsetToDoorBehavior(beh);
                    break;
                }
            }
        }

        // ----- Utilities -----

        private static Type ResolveType(string fullName)
        {
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                var t = asm.GetType(fullName);
                if (t != null) return t;
            }
            return null;
        }
    }
}
