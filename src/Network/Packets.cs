using ProtoBuf;

namespace MoveDoors
{
    [ProtoContract]
    public class BlockOffsetEntry
    {
        [ProtoMember(1)] public int X;
        [ProtoMember(2)] public int Y;
        [ProtoMember(3)] public int Z;
        [ProtoMember(4)] public sbyte OffX;
        [ProtoMember(5)] public sbyte OffY;
        [ProtoMember(6)] public sbyte OffZ;
    }

    [ProtoContract]
    public class BlockOffsetSyncPacket
    {
        [ProtoMember(1)] public BlockOffsetEntry[] Entries;
    }

    [ProtoContract]
    public class BlockOffsetUpdatePacket
    {
        [ProtoMember(1)] public BlockOffsetEntry Entry;
        [ProtoMember(2)] public bool Remove;
    }

    [ProtoContract]
    public class MoveWrenchInteractPacket
    {
        [ProtoMember(1)] public int X;
        [ProtoMember(2)] public int Y;
        [ProtoMember(3)] public int Z;
        [ProtoMember(4)] public int FaceIndex;
        [ProtoMember(5)] public bool Reset;
        [ProtoMember(6)] public int StepVoxels;
    }
}
