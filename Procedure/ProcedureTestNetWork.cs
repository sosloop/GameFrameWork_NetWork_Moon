using System.Net;
using GameFramework.Fsm;
using GameFramework.Network;
using GameFramework.Procedure;
using Moon;
using NetMessage;
using UnityEngine;
using UnityGameFramework.Runtime;

namespace Flower
{
    public class ProcedureTestMoonNetWork : ProcedureBase
    {
        private INetworkChannel _networkChannel;
        private MoonNetworkChannelHelper _moonNetworkChannelHelper;

        protected override void OnInit(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnInit(procedureOwner);
        }

        protected override void OnEnter(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnEnter(procedureOwner);

            if (IPAddress.TryParse("127.0.0.1",out IPAddress ipAddress))
            {
                _moonNetworkChannelHelper = new MoonNetworkChannelHelper();
                _networkChannel = GameEntry.Network.CreateNetworkChannel("Test",ServiceType.Tcp,_moonNetworkChannelHelper);
                _networkChannel.Connect(ipAddress,12345);
                Log.Info("开始连接");
                _moonNetworkChannelHelper.OnSocketStatue = (socketType) =>
                {
                    if (socketType == SocketMessageType.Connect)
                    {
                        ConnectionSuccess();
                    }
                };
            }

            
            
        }

        async void ConnectionSuccess()
        {
            var c2SLogin = new C2SLogin() { Openid = "A1" };
            ushort opCode = nameof(C2SLogin).GetOpCode();
            MoonPacket moonPacket = MoonPacket.Create(c2SLogin,opCode);
                        
            // _networkChannel.Send(moonPacket);

            Log.Info("连接登录");
            S2CLogin s2CLogin = await _moonNetworkChannelHelper.Call<S2CLogin>(moonPacket);
            _moonNetworkChannelHelper.IsAuth = true;
            Log.Info("登录成功");
            _moonNetworkChannelHelper.SendHeartBeat();
        }

        async void CallBagItem()
        {
            C2SItemList c2SItemList = new C2SItemList();
            ushort opCode = nameof(C2SItemList).GetOpCode();
            S2CItemList s2CItemList = await _moonNetworkChannelHelper.Call<S2CItemList>(MoonPacket.Create(c2SItemList,opCode));
            Log.Info(s2CItemList.List.Count);
        }

        protected override void OnUpdate(IFsm<IProcedureManager> procedureOwner, float elapseSeconds, float realElapseSeconds)
        {
            base.OnUpdate(procedureOwner, elapseSeconds, realElapseSeconds);

            if (Input.GetKeyUp(KeyCode.C))
            {
                CallBagItem();
            }
        }

        protected override void OnLeave(IFsm<IProcedureManager> procedureOwner, bool isShutdown)
        {
            base.OnLeave(procedureOwner, isShutdown);
        }

        protected override void OnDestroy(IFsm<IProcedureManager> procedureOwner)
        {
            base.OnDestroy(procedureOwner);
            
            _networkChannel?.Close();
            _moonNetworkChannelHelper = null;
        }
    }
}