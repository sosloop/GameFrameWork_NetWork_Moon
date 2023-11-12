using GameFramework;
using GameFramework.Network;

namespace Moon
{
    public class MoonPacketHeader : IPacketHeader , IReference
    {
        public int PacketLength { get; set; }
        public bool Fin { get; set; }
        
        public void Clear()
        {
            PacketLength = 0;
            Fin = false;
        }
    }
}