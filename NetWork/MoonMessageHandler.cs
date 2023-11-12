using GameFramework.Network;

namespace Moon
{
    public class MoonMessageHandler:IPacketHandler
    {
        public int Id { get; }
        public void Handle(object sender, Packet packet)
        {
            
        }
    }
}