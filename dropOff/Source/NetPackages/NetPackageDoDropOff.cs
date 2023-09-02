using System.Collections.Generic;

// Server => Client
// Informs client it is safe to drop-off
// To a list of containers
class NetPackageDoDropOff : NetPackageInvManageAction
{
    public override NetPackageDirection PackageDirection => NetPackageDirection.ToClient;

    public NetPackageDoDropOff Setup(Vector3i _center, List<Vector3i> _containerEntities, DropOffType _type)
    {
        Setup(_center, _containerEntities);
        type = _type;
        return this;
    }

    public override int GetLength()
    {
        return base.GetLength() + 1;
    }

    public override void ProcessPackage(World _world, GameManager _callbacks)
    {
        if (containerEntities == null || _world == null || containerEntities.Count == 0)
        {
            return;
        }

        DropOff.ClientMoveDropOff(center, containerEntities);
        ConnectionManager.Instance.SendToServer(NetPackageManager.GetPackage<NetPackageUnlockContainers>().Setup(center, containerEntities));
    }

    public override void read(PooledBinaryReader _reader)
    {
        base.read(_reader);
        type = (DropOffType)_reader.ReadByte();
    }

    public override void write(PooledBinaryWriter _writer)
    {
        base.write(_writer);
        _writer.Write((byte)type);
    }

    protected DropOffType type;
}
