using System;
using System.Collections.Generic;
using System.IO;
using ProtoBuf;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace MoveDoors
{
    public class BlockOffsetManager
    {
        private const string SaveDataKey = "movedoors:offsets";
        private const sbyte MinOff = -8;
        private const sbyte MaxOff = 8;

        public readonly Dictionary<BlockPos, Vec3i> Offsets = new Dictionary<BlockPos, Vec3i>();

        private ICoreServerAPI sapi;
        private ICoreClientAPI capi;
        private IServerNetworkChannel serverChannel;
        private IClientNetworkChannel clientChannel;

        public void InitServer(ICoreServerAPI api)
        {
            sapi = api;
            serverChannel = api.Network
                .RegisterChannel(MoveDoorsModSystem.NetworkChannel)
                .RegisterMessageType<BlockOffsetSyncPacket>()
                .RegisterMessageType<BlockOffsetUpdatePacket>()
                .RegisterMessageType<MoveWrenchInteractPacket>()
                .SetMessageHandler<MoveWrenchInteractPacket>(OnInteractReceived);

            api.Event.SaveGameLoaded += OnSaveLoaded;
            api.Event.GameWorldSave += OnSave;
            api.Event.PlayerJoin += OnPlayerJoin;
        }

        public void InitClient(ICoreClientAPI api)
        {
            capi = api;
            clientChannel = api.Network
                .RegisterChannel(MoveDoorsModSystem.NetworkChannel)
                .RegisterMessageType<BlockOffsetSyncPacket>()
                .RegisterMessageType<BlockOffsetUpdatePacket>()
                .RegisterMessageType<MoveWrenchInteractPacket>()
                .SetMessageHandler<BlockOffsetSyncPacket>(OnSync)
                .SetMessageHandler<BlockOffsetUpdatePacket>(OnUpdate);
        }

        public Vec3i Get(BlockPos pos)
        {
            return pos != null && Offsets.TryGetValue(pos, out var v) ? v : null;
        }

        public static bool IsMovable(Block block)
        {
            if (block == null) return false;
            return block is BlockDoor
                || block is BlockTrapDoor
                || block is BlockFenceGate;
        }

        // ----- Client → Server -----

        public void ClientSendInteract(BlockPos pos, BlockFacing face, bool reset, int stepVoxels)
        {
            clientChannel?.SendPacket(new MoveWrenchInteractPacket
            {
                X = pos.X, Y = pos.Y, Z = pos.Z,
                FaceIndex = face.Index,
                Reset = reset,
                StepVoxels = stepVoxels
            });
        }

        private void OnInteractReceived(IServerPlayer player, MoveWrenchInteractPacket pkt)
        {
            var pos = new BlockPos(pkt.X, pkt.Y, pkt.Z);
            var block = sapi.World.BlockAccessor.GetBlock(pos);
            if (!IsMovable(block)) return;

            // Basic reach sanity check.
            var eyePos = player.Entity.SidedPos.XYZ.Add(0, player.Entity.LocalEyePos.Y, 0);
            if (eyePos.DistanceTo(pos.X + 0.5, pos.Y + 0.5, pos.Z + 0.5) > 8) return;

            var positions = CollectGroup(pos, block);

            if (pkt.Reset)
            {
                foreach (var p in positions) RemoveOffset(p);
            }
            else
            {
                var face = BlockFacing.ALLFACES[pkt.FaceIndex & 0x07];
                int step = ClampStep(pkt.StepVoxels);
                int dx = face.Normali.X * step;
                int dy = face.Normali.Y * step;
                int dz = face.Normali.Z * step;

                foreach (var p in positions) AddDelta(p, dx, dy, dz);
            }

            foreach (var p in positions)
            {
                sapi.World.BlockAccessor.MarkBlockDirty(p);
                sapi.World.BlockAccessor.MarkBlockEntityDirty(p);
            }
        }

        private static int ClampStep(int s) => s == 1 || s == 2 || s == 4 || s == 8 ? s : 1;

        private List<BlockPos> CollectGroup(BlockPos pos, Block block)
        {
            var list = new List<BlockPos> { pos };

            // Double doors: include the paired half if present.
            if (block is BlockDoor)
            {
                var paired = DoorGroup.FindPaired(sapi.World.BlockAccessor, pos);
                if (paired != null && !list.Contains(paired)) list.Add(paired);

                // Two-block-tall door: include the second block.
                var be = sapi.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityDoor;
                if (be != null)
                {
                    var up = pos.UpCopy();
                    if (sapi.World.BlockAccessor.GetBlock(up) is BlockDoor) list.Add(up);
                    var down = pos.DownCopy();
                    if (sapi.World.BlockAccessor.GetBlock(down) is BlockDoor && !list.Contains(down)) list.Add(down);
                }
            }
            return list;
        }

        private void AddDelta(BlockPos pos, int dx, int dy, int dz)
        {
            var cur = Get(pos) ?? new Vec3i(0, 0, 0);
            int nx = Clamp(cur.X + dx, MinOff, MaxOff);
            int ny = Clamp(cur.Y + dy, MinOff, MaxOff);
            int nz = Clamp(cur.Z + dz, MinOff, MaxOff);
            var next = new Vec3i(nx, ny, nz);

            if (next.X == 0 && next.Y == 0 && next.Z == 0)
            {
                RemoveOffset(pos);
                return;
            }

            Offsets[pos.Copy()] = next;
            Broadcast(pos, next, remove: false);
        }

        private void RemoveOffset(BlockPos pos)
        {
            BlockPos key = null;
            foreach (var k in Offsets.Keys)
            {
                if (k.X == pos.X && k.Y == pos.Y && k.Z == pos.Z) { key = k; break; }
            }
            if (key != null) Offsets.Remove(key);
            Broadcast(pos, new Vec3i(0, 0, 0), remove: true);
        }

        private void Broadcast(BlockPos pos, Vec3i v, bool remove)
        {
            var entry = new BlockOffsetEntry
            {
                X = pos.X, Y = pos.Y, Z = pos.Z,
                OffX = (sbyte)v.X, OffY = (sbyte)v.Y, OffZ = (sbyte)v.Z
            };
            serverChannel?.BroadcastPacket(new BlockOffsetUpdatePacket { Entry = entry, Remove = remove });
        }

        private static int Clamp(int v, int lo, int hi) => v < lo ? lo : (v > hi ? hi : v);

        // ----- Client receives -----

        private void OnSync(BlockOffsetSyncPacket pkt)
        {
            Offsets.Clear();
            if (pkt?.Entries == null) return;
            foreach (var e in pkt.Entries)
            {
                Offsets[new BlockPos(e.X, e.Y, e.Z)] = new Vec3i(e.OffX, e.OffY, e.OffZ);
                capi?.World.BlockAccessor.MarkBlockDirty(new BlockPos(e.X, e.Y, e.Z));
            }
        }

        private void OnUpdate(BlockOffsetUpdatePacket pkt)
        {
            if (pkt?.Entry == null) return;
            var pos = new BlockPos(pkt.Entry.X, pkt.Entry.Y, pkt.Entry.Z);

            BlockPos key = null;
            foreach (var k in Offsets.Keys)
            {
                if (k.X == pos.X && k.Y == pos.Y && k.Z == pos.Z) { key = k; break; }
            }

            if (pkt.Remove)
            {
                if (key != null) Offsets.Remove(key);
            }
            else
            {
                Offsets[key ?? pos] = new Vec3i(pkt.Entry.OffX, pkt.Entry.OffY, pkt.Entry.OffZ);
            }

            capi?.World.BlockAccessor.MarkBlockDirty(pos);
            capi?.World.BlockAccessor.MarkBlockEntityDirty(pos);
        }

        // ----- Persistence -----

        private void OnSaveLoaded()
        {
            var data = sapi.WorldManager.SaveGame.GetData(SaveDataKey);
            if (data == null || data.Length == 0) return;
            try
            {
                using var ms = new MemoryStream(data);
                var pkt = Serializer.Deserialize<BlockOffsetSyncPacket>(ms);
                Offsets.Clear();
                if (pkt?.Entries != null)
                {
                    foreach (var e in pkt.Entries)
                        Offsets[new BlockPos(e.X, e.Y, e.Z)] = new Vec3i(e.OffX, e.OffY, e.OffZ);
                }
            }
            catch (Exception ex)
            {
                sapi.Logger.Warning("[movedoors] Failed to load offsets: " + ex.Message);
            }
        }

        private void OnSave()
        {
            var entries = new BlockOffsetEntry[Offsets.Count];
            int i = 0;
            foreach (var kv in Offsets)
            {
                entries[i++] = new BlockOffsetEntry
                {
                    X = kv.Key.X, Y = kv.Key.Y, Z = kv.Key.Z,
                    OffX = (sbyte)kv.Value.X, OffY = (sbyte)kv.Value.Y, OffZ = (sbyte)kv.Value.Z
                };
            }
            using var ms = new MemoryStream();
            Serializer.Serialize(ms, new BlockOffsetSyncPacket { Entries = entries });
            sapi.WorldManager.SaveGame.StoreData(SaveDataKey, ms.ToArray());
        }

        private void OnPlayerJoin(IServerPlayer player)
        {
            var entries = new BlockOffsetEntry[Offsets.Count];
            int i = 0;
            foreach (var kv in Offsets)
            {
                entries[i++] = new BlockOffsetEntry
                {
                    X = kv.Key.X, Y = kv.Key.Y, Z = kv.Key.Z,
                    OffX = (sbyte)kv.Value.X, OffY = (sbyte)kv.Value.Y, OffZ = (sbyte)kv.Value.Z
                };
            }
            serverChannel.SendPacket(new BlockOffsetSyncPacket { Entries = entries }, player);
        }
    }
}
