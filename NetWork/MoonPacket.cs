using GameFramework;
using GameFramework.Network;

namespace Moon
{
    public class MoonPacket : Packet
    {
        public override void Clear()
        {
            Message = null;
            OpCode = 0;
        }

        public override int Id => OpCode;

        public ushort OpCode { get; private set; }

        public object Message;
        
        public static MoonPacket Create(object msg,ushort opCode)
        {
            MoonPacket moonPacket = ReferencePool.Acquire<MoonPacket>();
            moonPacket.Message = msg;
            moonPacket.OpCode = opCode;
            return moonPacket;
        }
        
    }
}