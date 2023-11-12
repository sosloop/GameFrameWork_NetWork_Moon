using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using Cysharp.Threading.Tasks;
using GameFramework;
using GameFramework.Network;
using NetMessage;
using UnityGameFramework.Runtime;

namespace Moon
{
    public enum SocketMessageType
    {
        Connect,
        Close,
    }
    public class MoonNetworkChannelHelper : INetworkChannelHelper
    {
        // size 大小
        public int PacketHeaderLength => sizeof(ushort);
        // 消息Id 大小
        private const int MessageOpcodeIndex = sizeof(ushort);
        
        // size + opcode + msgData
        private int PackHeadOpCodeSize => PacketHeaderLength + MessageOpcodeIndex;
        
        private const ushort MessageContinuedFlag = ushort.MaxValue;
        
        private readonly byte[] _sendCache = new byte[MessageOpcodeIndex];

        public bool IsAuth { get; set; }

        //
        private INetworkChannel m_NetworkChannel;
        //
        public Action<SocketMessageType> OnSocketStatue;
        
        private readonly Dictionary<ushort, Action<MoonPacket>> _actions;
        private readonly Dictionary<ushort, AutoResetUniTaskCompletionSource<MoonPacket>> _tasks;

        public MoonNetworkChannelHelper()
        {
            IsAuth = false;
            _actions = new Dictionary<ushort, Action<MoonPacket>>();
            _tasks = new Dictionary<ushort, AutoResetUniTaskCompletionSource<MoonPacket>>();
        }

        public void Initialize(INetworkChannel networkChannel)
        {
            m_NetworkChannel = networkChannel;
            
            m_NetworkChannel.HeartBeatInterval = 3;
            
            m_NetworkChannel.SetDefaultHandler(Dispatch);

            Flower.GameEntry.Event.Subscribe(UnityGameFramework.Runtime.NetworkConnectedEventArgs.EventId, OnNetworkConnected);
            Flower.GameEntry.Event.Subscribe(UnityGameFramework.Runtime.NetworkClosedEventArgs.EventId, OnNetworkClosed);
            Flower.GameEntry.Event.Subscribe(UnityGameFramework.Runtime.NetworkErrorEventArgs.EventId, OnNetworkError);
            Flower.GameEntry.Event.Subscribe(UnityGameFramework.Runtime.NetworkCustomErrorEventArgs.EventId, OnNetworkCustomError);
            Flower.GameEntry.Event.Subscribe(UnityGameFramework.Runtime.NetworkMissHeartBeatEventArgs.EventId, OnNetworkMissHeartBeat);
        }

        public void Shutdown()
        {
            Flower.GameEntry.Event.Unsubscribe(UnityGameFramework.Runtime.NetworkConnectedEventArgs.EventId, OnNetworkConnected);
            Flower.GameEntry.Event.Unsubscribe(UnityGameFramework.Runtime.NetworkClosedEventArgs.EventId, OnNetworkClosed);
            Flower.GameEntry.Event.Unsubscribe(UnityGameFramework.Runtime.NetworkErrorEventArgs.EventId, OnNetworkError);
            Flower.GameEntry.Event.Unsubscribe(UnityGameFramework.Runtime.NetworkCustomErrorEventArgs.EventId, OnNetworkCustomError);
            Flower.GameEntry.Event.Unsubscribe(UnityGameFramework.Runtime.NetworkMissHeartBeatEventArgs.EventId, OnNetworkMissHeartBeat);

            m_NetworkChannel = null;
            OnSocketStatue = null;
        }

        public void PrepareForConnecting()
        {
        }

        #region GF封包解包分发

        public bool SendHeartBeat()
        {
            if (IsAuth)
            {
                
                CallHeartBeat().Forget();
            }
            
            return true;
        }

        private async UniTask CallHeartBeat()
        {
            C2SPing c2SPing = new C2SPing();
            ushort opCode = nameof(C2SPing).GetOpCode();
            MoonPacket moonPacket = MoonPacket.Create(c2SPing,opCode);


            S2CPong s2CPong = await Call<S2CPong>(moonPacket);
        }

        public bool Serialize<T>(T packet, Stream destination) where T : Packet
        {
            MoonPacket packetImpl = packet as MoonPacket;

            if (packetImpl == null || packetImpl.Message == null)
            {
                throw new MoonNetworkException("Serialize Err");
            }
            
            // 长度 | (id+消息)

            #region moon实现
            // 消息ID
            // Moon.Buffer buffer = new Moon.Buffer();
            // ushort opcode = Convert.ToUInt16(packetImpl.Id);
            // buffer.Write(opcode);
            //
            // // 消息
            // using MemoryStream memory = new MemoryStream();
            // ProtoBuf.Serializer.Serialize(memory, packetImpl.Message);
            // byte[] sData = memory.ToArray();
            // buffer.Write(sData, 0, sData.Length);
            //
            // // 写入大小
            // short len = (short)buffer.Count;
            //
            // Log.Info($"Serialize1 opcode={opcode} messageSize={len} Count={buffer.Count}");
            //
            // len = IPAddress.HostToNetworkOrder(len);
            // buffer.WriteFront(len);
            //
            // destination.Write(buffer.Data, buffer.Index, buffer.Count);
            // PrintBytes((MemoryStream)destination);
            // Log.Info($"Serialize2 opcode={opcode} messageSize={len} Count={buffer.Count}");
            #endregion

            #region 无需new

            ushort opcode = Convert.ToUInt16(packetImpl.Id);
            
            MemoryStream stream = (MemoryStream)destination;
            
            // 消息Id
            stream.Seek(PackHeadOpCodeSize, SeekOrigin.Begin);
            stream.SetLength(PackHeadOpCodeSize);
            stream.GetBuffer().WriteBigTo(PacketHeaderLength,opcode);
            //
            ProtoBuf.Serializer.Serialize(stream, packetImpl.Message);
            //
            stream.Seek(0, SeekOrigin.Begin);
            // 大小 - size
            short messageSize =  (short)(stream.Length - stream.Position - PacketHeaderLength);
            
            // Log.Info($"Serialize1 opcode={opcode} messageSize={messageSize}");
            
            messageSize = IPAddress.HostToNetworkOrder(messageSize);
            _sendCache.WriteBigTo(0, messageSize);
            stream.Write(_sendCache, 0, MessageOpcodeIndex);
            
            // messageSize =  (short)(stream.Length - stream.Position);
            // Log.Info($"Serialize2 opcode={opcode} messageSize={messageSize}");
            // PrintBytes((MemoryStream)destination);
            #endregion
            
            ReferencePool.Release(packetImpl);
            
            return true;
        }

        void PrintBytes(MemoryStream memoryStream)
        {
            StringBuilder sb = new StringBuilder();
            var buffer = memoryStream.GetBuffer();
            for (int i = 0; i < buffer.Length; i++)
            {
                sb.Append(buffer[i].ToString("x2")).Append(" ");
            }
            Log.Info(sb);
        }

        public IPacketHeader DeserializePacketHeader(Stream source, out object customErrorData)
        {
            customErrorData = null;

            MemoryStream memoryStream = (MemoryStream)source;
            
            ushort packetSize = (ushort)IPAddress.NetworkToHostOrder(BitConverter.ToInt16(memoryStream.GetBuffer(), 0));
            bool fin = packetSize < MessageContinuedFlag;

            MoonPacketHeader header = ReferencePool.Acquire<MoonPacketHeader>();
            header.PacketLength = packetSize;
            header.Fin = fin;
            
            // Log.Info($"header packetSize={packetSize} fin={fin}");
            
            return header;
        }

        public Packet DeserializePacket(IPacketHeader packetHeader, Stream source, out object customErrorData)
        {
        
            MoonPacketHeader packetHeaderImpl = packetHeader as MoonPacketHeader;

            if(packetHeaderImpl == null)
            {
                customErrorData = "Packet header is null.";
                return null;
            }

            customErrorData = null;
            
            MemoryStream memoryStream = (MemoryStream)source;
            memoryStream.Seek(MessageOpcodeIndex, SeekOrigin.Begin);
            
            ushort opCode = BitConverter.ToUInt16(memoryStream.GetBuffer(), 0);

            Type type = MoonCmdHelp.TypeOpcodes[opCode];
            
            // Log.Info($"packet opcode={opCode} messageName={messageName} type={type}");
            
            object deserialize = ProtoBuf.Serializer.Deserialize(type, source);
            MoonPacket moonPacket = MoonPacket.Create(deserialize,opCode);

            ReferencePool.Release(packetHeaderImpl);

            return moonPacket;
        }
        
        void Dispatch(object sender, Packet packet)
        {
            if (sender != m_NetworkChannel)
            {
                return;
            }
            
            MoonPacket moonPacket = (MoonPacket)packet;
            ushort opCode = moonPacket.OpCode;

            bool isHandler = false;
            if (_tasks.TryGetValue(opCode,out var tcs))
            {
                isHandler = true;
                tcs.TrySetResult(moonPacket);
                _tasks.Remove(opCode);
            }

            if (_actions.TryGetValue(opCode,out var action))
            {
                isHandler = true;
                action(moonPacket);
            }

            if (!isHandler)
            {
                throw new MoonNetworkException($"Msg = {opCode.GetCmdCodeName()} is not handler !!!");
            }
            Log.Info($"[NET REC] {opCode} | {opCode.GetCmdCodeName()} ");
            
        }
        #endregion
        
        #region SocketLife

        private void OnNetworkMissHeartBeat(object sender, GameFramework.Event.GameEventArgs e)
        {
            UnityGameFramework.Runtime.NetworkMissHeartBeatEventArgs ne = (UnityGameFramework.Runtime.NetworkMissHeartBeatEventArgs) e;
            if (ne.NetworkChannel != m_NetworkChannel)
            {
                return;
            }

            if (ne.MissCount > 2)
            {
                ne.NetworkChannel.Close();
            }
        }

        private void OnNetworkCustomError(object sender, GameFramework.Event.GameEventArgs e)
        {
            UnityGameFramework.Runtime.NetworkCustomErrorEventArgs ne = (UnityGameFramework.Runtime.NetworkCustomErrorEventArgs) e;
            if (ne.NetworkChannel != m_NetworkChannel)
            {
                return;
            }

            if (ne.CustomErrorData != null)
            {
                Log.Error("Network Packet {0} CustomError : {1}.", ne.Id, ne.CustomErrorData);
                
                ne.NetworkChannel.Close();
            }
        }

        private void OnNetworkError(object sender, GameFramework.Event.GameEventArgs e)
        {
            UnityGameFramework.Runtime.NetworkErrorEventArgs ne = (UnityGameFramework.Runtime.NetworkErrorEventArgs) e;
            if (ne.NetworkChannel != m_NetworkChannel)
            {
                return;
            }

            Log.Error("Network Packet {0} Error : {1}.", ne.Id, ne.ErrorMessage);
            
            ne.NetworkChannel.Close();
        }

        private void OnNetworkClosed(object sender, GameFramework.Event.GameEventArgs e)
        {
            UnityGameFramework.Runtime.NetworkClosedEventArgs ne = (UnityGameFramework.Runtime.NetworkClosedEventArgs) e;
            if (ne.NetworkChannel != m_NetworkChannel)
            {
                return;
            }
            
            OnSocketStatue?.Invoke(SocketMessageType.Close);
            
            Log.Error("NetworkChannel {0} is closed.", ne.NetworkChannel.Name);
        }

        private void OnNetworkConnected(object sender, GameFramework.Event.GameEventArgs e)
        {
            UnityGameFramework.Runtime.NetworkConnectedEventArgs ne = (UnityGameFramework.Runtime.NetworkConnectedEventArgs) e;
            if (ne.NetworkChannel != m_NetworkChannel)
            {
                return;
            }

            OnSocketStatue?.Invoke(SocketMessageType.Connect);
            
            Log.Info("NetworkChannel {0} is connected. IP: {1}", ne.NetworkChannel.Name, ne.NetworkChannel.Socket.RemoteEndPoint.ToString());
        }

        #endregion

        #region 自定义发送接收

        public void Register<TResponse>(Action<TResponse> callback)
        {
            ushort responseId = MoonCmdHelp.OpcodeTypes[typeof(TResponse)];
            _actions[responseId] = msg =>
            {
                callback((TResponse)msg.Message);
                
            };
        }

        public void Send(MoonPacket msg)
        {
            Log.Info($"[NET SEND] {msg.OpCode} | {msg.OpCode.GetCmdCodeName()}");
            
            m_NetworkChannel.Send(msg);
        }

        public async UniTask<TResponse> Call<TResponse>(MoonPacket msg)
        {
            ushort responseId = MoonCmdHelp.OpcodeTypes[typeof(TResponse)];

            if (_actions.ContainsKey(responseId))
            {
                throw new MoonNetworkException($"Call Msg = {nameof(msg)} responseId={responseId} is Register!!!");
            }
            
            Send(msg);
            
            var task = AutoResetUniTaskCompletionSource<MoonPacket>.Create();
            _tasks[responseId] = task;
            MoonPacket res = await task.Task;

            TResponse response = (TResponse)res.Message;
            
            return response;
        }

        #endregion
    }
}