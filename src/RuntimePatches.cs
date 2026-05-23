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
            TryPatchRayTrace(harmony, logger);
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

        // ----- Ray-trace selection postfix (interact with shifted door from its new position) -----
        // VS walks grid cells along the player's gaze ray and asks each cell's block whether the
        // ray hits its selection boxes. A door shifted out of its grid cell can't be interacted
        // with by looking at the new hitbox location, because that cell's block is air. We
        // postfix the ray-trace to also test each shifted door's bounding box against the ray,
        // and replace the result if a door is hit closer than whatever VS found.

        private static void TryPatchRayTrace(Harmony harmony, ILogger logger)
        {
            try
            {
                var iface = ResolveType("Vintagestory.API.Common.IBlockAccessor");
                if (iface == null) return;

                int count = 0;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    Type[] types;
                    try { types = asm.GetTypes(); }
                    catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray(); }

                    foreach (var t in types)
                    {
                        if (t == null || t.IsInterface || t.IsAbstract) continue;
                        if (!iface.IsAssignableFrom(t)) continue;

                        var method = t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                            .FirstOrDefault(m => m.Name == "RayTraceForSelection");
                        if (method == null) continue;

                        try
                        {
                            var postfix = new HarmonyMethod(typeof(RuntimePatches).GetMethod(nameof(RayTracePostfix),
                                BindingFlags.Static | BindingFlags.NonPublic));
                            harmony.Patch(method, postfix: postfix);
                            count++;
                        }
                        catch (Exception ex)
                        {
                            logger.Warning("[movedoors] couldn't patch " + t.FullName + ".RayTraceForSelection: " + ex.Message);
                        }
                    }
                }
                logger.Notification("[movedoors] patched " + count + " RayTraceForSelection overloads");
            }
            catch (Exception ex)
            {
                logger.Warning("[movedoors] TryPatchRayTrace failed: " + ex.Message);
            }
        }

        private static void RayTracePostfix(Vec3d fromPos, Vec3d toPos, ref BlockSelection blockSel, IBlockAccessor __instance)
        {
            try
            {
                var mgr = MoveDoorsModSystem.Offsets;
                if (mgr == null || mgr.Offsets.Count == 0) return;
                if (fromPos == null || toPos == null || __instance == null) return;

                double rayLen = toPos.SubCopy(fromPos).Length();
                double bestT = double.MaxValue;
                if (blockSel?.HitPosition != null)
                {
                    bestT = new Vec3d(
                        blockSel.Position.X + blockSel.HitPosition.X,
                        blockSel.Position.Y + blockSel.HitPosition.Y,
                        blockSel.Position.Z + blockSel.HitPosition.Z
                    ).SubCopy(fromPos).Length();
                }

                foreach (var kv in mgr.Offsets)
                {
                    var doorPos = kv.Key;
                    var off = kv.Value;
                    if (off.X == 0 && off.Y == 0 && off.Z == 0) continue;

                    // Skip if door is far from the ray's general area (cheap pre-filter).
                    double cdx = doorPos.X + 0.5 - (fromPos.X + toPos.X) / 2;
                    double cdy = doorPos.Y + 0.5 - (fromPos.Y + toPos.Y) / 2;
                    double cdz = doorPos.Z + 0.5 - (fromPos.Z + toPos.Z) / 2;
                    if (cdx * cdx + cdy * cdy + cdz * cdz > (rayLen + 2) * (rayLen + 2)) continue;

                    var block = __instance.GetBlock(doorPos);
                    if (!BlockOffsetManager.IsMovable(block)) continue;

                    var boxes = block.GetSelectionBoxes(__instance, doorPos);
                    if (boxes == null || boxes.Length == 0) continue;

                    foreach (var box in boxes)
                    {
                        Vec3d boxMin = new Vec3d(box.X1 + doorPos.X, box.Y1 + doorPos.Y, box.Z1 + doorPos.Z);
                        Vec3d boxMax = new Vec3d(box.X2 + doorPos.X, box.Y2 + doorPos.Y, box.Z2 + doorPos.Z);

                        if (!TryRayAabbHit(fromPos, toPos, boxMin, boxMax, out double t, out BlockFacing face)) continue;
                        if (t >= bestT) continue;

                        Vec3d hitWorld = new Vec3d(
                            fromPos.X + (toPos.X - fromPos.X) * (t / rayLen),
                            fromPos.Y + (toPos.Y - fromPos.Y) * (t / rayLen),
                            fromPos.Z + (toPos.Z - fromPos.Z) * (t / rayLen)
                        );

                        bestT = t;
                        blockSel = new BlockSelection
                        {
                            Position = doorPos.Copy(),
                            Face = face,
                            HitPosition = new Vec3d(hitWorld.X - doorPos.X, hitWorld.Y - doorPos.Y, hitWorld.Z - doorPos.Z),
                            DidOffset = false,
                            Block = block
                        };
                    }
                }
            }
            catch (Exception ex)
            {
                MoveDoorsModSystem.Logger?.Warning("[movedoors] RayTracePostfix threw: " + ex.Message);
            }
        }

        // Ray-AABB intersection. fromPos / toPos define the ray's bounded segment in world space.
        // boxMin/boxMax are the world-space AABB. Returns nearest hit distance (t, in world units
        // measured from fromPos along the ray segment) and which face was entered.
        private static bool TryRayAabbHit(Vec3d fromPos, Vec3d toPos, Vec3d boxMin, Vec3d boxMax,
            out double tHit, out BlockFacing face)
        {
            tHit = 0;
            face = BlockFacing.UP;

            Vec3d dir = toPos.SubCopy(fromPos);
            double rayLen = dir.Length();
            if (rayLen < 1e-6) return false;

            double tNear = 0;
            double tFar = rayLen;
            int hitAxis = -1;
            int hitSign = 0;

            for (int axis = 0; axis < 3; axis++)
            {
                double o = axis == 0 ? fromPos.X : axis == 1 ? fromPos.Y : fromPos.Z;
                double d = axis == 0 ? dir.X : axis == 1 ? dir.Y : dir.Z;
                double bMin = axis == 0 ? boxMin.X : axis == 1 ? boxMin.Y : boxMin.Z;
                double bMax = axis == 0 ? boxMax.X : axis == 1 ? boxMax.Y : boxMax.Z;

                if (Math.Abs(d) < 1e-9)
                {
                    if (o < bMin || o > bMax) return false;
                    continue;
                }

                double t1 = (bMin - o) / d;
                double t2 = (bMax - o) / d;
                int signEntering = -1;
                if (t1 > t2) { (t1, t2) = (t2, t1); signEntering = 1; }

                if (t1 > tNear) { tNear = t1; hitAxis = axis; hitSign = signEntering; }
                if (t2 < tFar) tFar = t2;
                if (tNear > tFar) return false;
            }

            if (tNear < 0 || tNear > rayLen) return false;

            tHit = tNear;
            switch (hitAxis)
            {
                case 0: face = hitSign > 0 ? BlockFacing.EAST : BlockFacing.WEST; break;
                case 1: face = hitSign > 0 ? BlockFacing.UP : BlockFacing.DOWN; break;
                case 2: face = hitSign > 0 ? BlockFacing.SOUTH : BlockFacing.NORTH; break;
            }
            return true;
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
