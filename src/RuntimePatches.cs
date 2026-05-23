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
            // RayTraceForSelection patching turned out unreliable (the method isn't declared
            // where I expected). Selection retargeting is now done via per-frame tick callback
            // registered in MoveDoorsModSystem.StartClientSide → RetargetPlayerSelection.
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

        // ----- Per-frame selection retargeting (interact with shifted door from its new pos) -----
        // Called every client tick. If the player's gaze ray hits a shifted door's bounding box
        // closer than their current selection, we replace CurrentBlockSelection so the click
        // routes to the door's grid pos.

        private static bool selectionFieldResolved = false;
        private static FieldInfo selectionBackingField;
        private static PropertyInfo selectionProp;
        private static int retargetLogCount = 0;
        private static bool playerDumped = false;

        private static void DumpPlayerSelectionStructure(IPlayer player)
        {
            if (playerDumped) return;
            playerDumped = true;

            try
            {
                var t = player.GetType();
                MoveDoorsModSystem.Logger?.Notification("[movedoors] player class: " + t.FullName);
                MoveDoorsModSystem.Logger?.Notification("[movedoors] entity class: " + player.Entity?.GetType().FullName);

                // Dump every field on the class hierarchy whose TYPE is BlockSelection — that's
                // the storage backing the get-only IPlayer.CurrentBlockSelection property.
                Type cur = t;
                while (cur != null && cur != typeof(object))
                {
                    foreach (var f in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.FieldType.Name == "BlockSelection")
                        {
                            MoveDoorsModSystem.Logger?.Notification("[movedoors] " + cur.Name + ".field<BlockSelection> " + f.Name);
                        }
                    }
                    cur = cur.BaseType;
                }

                // Same scan on the entity class (selection might be stored on the EntityPlayer instead).
                if (player.Entity != null)
                {
                    cur = player.Entity.GetType();
                    while (cur != null && cur != typeof(object))
                    {
                        foreach (var f in cur.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                        {
                            if (f.FieldType.Name == "BlockSelection")
                            {
                                MoveDoorsModSystem.Logger?.Notification("[movedoors] entity " + cur.Name + ".field<BlockSelection> " + f.Name);
                            }
                        }
                        cur = cur.BaseType;
                    }
                }
            }
            catch (Exception ex)
            {
                MoveDoorsModSystem.Logger?.Warning("[movedoors] DumpPlayerSelectionStructure: " + ex.Message);
            }
        }

        public static void RetargetPlayerSelection(ICoreClientAPI capi)
        {
            if (capi == null) return;
            var mgr = MoveDoorsModSystem.Offsets;
            if (mgr == null || mgr.Offsets.Count == 0) return;

            var player = capi.World.Player;
            var entity = player?.Entity;
            if (entity == null) return;

            try
            {
                var entPos = entity.Pos;
                Vec3d eyePos = new Vec3d(entPos.X, entPos.Y + entity.LocalEyePos.Y, entPos.Z);

                double yaw = entPos.Yaw;
                double pitch = entPos.Pitch;
                // VS convention: yaw=0 points -Z, pitch positive looks up.
                double cy = Math.Cos(pitch);
                double dx = -Math.Sin(yaw) * cy;
                double dy = Math.Sin(pitch);
                double dz = -Math.Cos(yaw) * cy;

                double reach = (double)(player.WorldData?.PickingRange ?? 5f);
                Vec3d toPos = new Vec3d(eyePos.X + dx * reach, eyePos.Y + dy * reach, eyePos.Z + dz * reach);

                // Distance of player's current selection (to beat).
                double bestT = reach + 0.01;
                var cur = player.CurrentBlockSelection;
                if (cur?.HitPosition != null)
                {
                    double cdx = cur.Position.X + cur.HitPosition.X - eyePos.X;
                    double cdy = cur.Position.Y + cur.HitPosition.Y - eyePos.Y;
                    double cdz = cur.Position.Z + cur.HitPosition.Z - eyePos.Z;
                    bestT = Math.Sqrt(cdx * cdx + cdy * cdy + cdz * cdz);
                }

                BlockSelection bestNew = null;

                foreach (var kv in mgr.Offsets)
                {
                    var doorPos = kv.Key;
                    var off = kv.Value;
                    if (off.X == 0 && off.Y == 0 && off.Z == 0) continue;

                    // Cheap pre-filter: skip doors clearly outside reach.
                    double pdx = doorPos.X + 0.5 - eyePos.X;
                    double pdy = doorPos.Y + 0.5 - eyePos.Y;
                    double pdz = doorPos.Z + 0.5 - eyePos.Z;
                    if (pdx * pdx + pdy * pdy + pdz * pdz > (reach + 2) * (reach + 2)) continue;

                    var block = capi.World.BlockAccessor.GetBlock(doorPos);
                    if (!BlockOffsetManager.IsMovable(block)) continue;

                    var boxes = block.GetSelectionBoxes(capi.World.BlockAccessor, doorPos);
                    if (boxes == null) continue;

                    foreach (var box in boxes)
                    {
                        Vec3d bMin = new Vec3d(box.X1 + doorPos.X, box.Y1 + doorPos.Y, box.Z1 + doorPos.Z);
                        Vec3d bMax = new Vec3d(box.X2 + doorPos.X, box.Y2 + doorPos.Y, box.Z2 + doorPos.Z);

                        if (!TryRayAabbHit(eyePos, toPos, bMin, bMax, out double t, out BlockFacing face)) continue;
                        if (t >= bestT) continue;

                        Vec3d hitWorld = new Vec3d(
                            eyePos.X + (toPos.X - eyePos.X) * (t / reach),
                            eyePos.Y + (toPos.Y - eyePos.Y) * (t / reach),
                            eyePos.Z + (toPos.Z - eyePos.Z) * (t / reach)
                        );

                        bestT = t;
                        bestNew = new BlockSelection
                        {
                            Position = doorPos.Copy(),
                            Face = face,
                            HitPosition = new Vec3d(hitWorld.X - doorPos.X, hitWorld.Y - doorPos.Y, hitWorld.Z - doorPos.Z),
                            DidOffset = false,
                            Block = block
                        };
                    }
                }

                DumpPlayerSelectionStructure(player);

                if (bestNew != null)
                {
                    if (!selectionFieldResolved)
                    {
                        selectionFieldResolved = true;
                        // VS 1.22 stores the player's selected block on the EntityPlayer as a
                        // public field named "BlockSelection".
                        var eType = entity.GetType();
                        Type cur = eType;
                        while (cur != null && cur != typeof(object) && selectionBackingField == null)
                        {
                            selectionBackingField = cur.GetField("BlockSelection",
                                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                            cur = cur.BaseType;
                        }
                        MoveDoorsModSystem.Logger?.Notification("[movedoors] selection writer: "
                            + (selectionBackingField != null ? "entity field " + selectionBackingField.Name : "NONE FOUND"));
                    }

                    if (selectionBackingField != null)
                    {
                        selectionBackingField.SetValue(entity, bestNew);
                    }

                    if (retargetLogCount < 3)
                    {
                        retargetLogCount++;
                        MoveDoorsModSystem.Logger?.Notification("[movedoors] retargeted selection to shifted door at " + bestNew.Position);
                    }
                }
            }
            catch (Exception ex)
            {
                MoveDoorsModSystem.Logger?.Warning("[movedoors] RetargetPlayerSelection threw: " + ex.Message);
            }
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
