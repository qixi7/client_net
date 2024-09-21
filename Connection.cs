using System;
using System.Collections.Generic;
using NetModule.Kcp;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using Google.Protobuf;
using NetModule.Log;
using NetModule.RDP;

namespace NetModule
{
    public class Connection
    {
        // 时钟
        private static readonly Stopwatch Clock;

        // socket
        private Socket _socket;
        private System.IO.Stream _stream;

        // 连接、收发线程 & 收发队列
        private NetQueue _sendChan;
        private NetQueue _recvChan;
        private volatile Thread _sendThread;
        private volatile Thread _recvThread;
        private volatile Thread _connThread;

        // 连接状态 & 网络回调事件
        private volatile ConnectionState _connState;
        public ConnectHandler ConnectedEvent;
        public ConnectErrorHandler ConnectErrorEvent;
        public RecvMsgHandler RecvMsgEvent;

        // 心跳相关
        private volatile Timer _heartTickTimer;
        private volatile int _heartOnTheAir;
        private long _lastSeen; // 最近一次收到服务器消息时间
        private long _rtt;  // RTT

        // error
        private volatile ConnectionError _lastError;
        private volatile System.Exception _lastThreadException;
        private long _connectStartTime; // 连接发起时间

        // 网络协议
        private ProtocolReader _reader;
        private ProtocolWriter _writer;
        private GameProtocol _marshaler;
        
        // buf
        private byte[] _recvBuf;

        // 数据统计
        public NetStatistics _netStat;
        
        public static bool HeartDebugLog = false;
        
        public ConnectionParam InitParam { get; }

        public double RTT => Interlocked.Read(ref _rtt);
        
        public long LastSeen => Interlocked.Read(ref _lastSeen);

        public System.Exception LastThreadException => _lastThreadException;

        static Connection()
        {
            Clock = Stopwatch.StartNew();
        }

        public Connection(ConnectionParam param)
        {
            InitParam = param;
            _sendChan = new NetQueue(16 * 1024 * 1024);
            _recvChan = new NetQueue(16 * 1024 * 1024);
            _connState = ConnectionState.Undefined;
            _marshaler = new GameProtocol();
            _netStat = new NetStatistics();
        }

        public static long Now()
        {
            return Clock.ElapsedTicks;
        }

        public static long NowMillis()
        {
            return Clock.ElapsedMilliseconds;
        }

        public ConnectionState GetConnectState()
        {
            return _connState;
        }

        private void Start()
        {
            _recvThread = new Thread(RunReceive)
            {
                Priority = ThreadPriority.AboveNormal
            };
            _sendThread = new Thread(RunSend)
            {
                Priority = ThreadPriority.AboveNormal
            };
            _heartTickTimer = new Timer(delegate
            {
                Timer heartTickTimer = _heartTickTimer;
                if (heartTickTimer == null || _lastError != ConnectionError.None)
                {
                    return;
                }

                if (!Monitor.TryEnter(heartTickTimer))
                {
                    return;
                }

                try
                {
                    long heartTimeDiff = NowMillis() - LastSeen;
                    if (HeartDebugLog)
                    {
                        LogHelper.InfoF("Will WriteHeart nowMs={0}, LastSeen={1}, heartTimeDiff={2}",
                            NowMillis(), LastSeen, heartTimeDiff);
                    }
                    if (heartTimeDiff > InitParam.HeartTickTimeout)
                    {
                        LogHelper.InfoF("heartTimeout! nowMs={0}, LastSeen={1}, heartTimeDiff={2}",
                        NowMillis(), LastSeen, heartTimeDiff);
                        _lastError = ConnectionError.HeartTimeout;
                    }
                    else if (InitParam.LazyHeartbeat)
                    {
                        if (heartTimeDiff > InitParam.HeartTickTime)
                        {
                            WriteHeart();
                        }
                    }
                    else if (Interlocked.CompareExchange(ref _heartOnTheAir, 1, 0) == 0)
                    {
                        WriteHeart();
                    }
                    else
                    {
                        Interlocked.Exchange(ref _rtt, -1L);
                    }
                }
                finally
                {
                    Monitor.Exit(heartTickTimer);
                }
            }, null, InitParam.HeartTickTime, InitParam.HeartTickTime);
            _recvThread.Start();
            _sendThread.Start();
        }

        public void Connect()
        {
            if (_connState != ConnectionState.Undefined)
            {
                throw new System.InvalidOperationException();
            }

            _connThread = new Thread(RunConnect);
            _connThread.Start();
            _connectStartTime = NowMillis();
            _connState = ConnectionState.PrepareConnect;
        }

        public void ConnectError(ConnectionError error)
        {
            CloseInternal();
            if (ConnectErrorEvent != null)
            {
                ConnectErrorEvent(this, error);
            }
        }

        public void Update()
        {
            switch (_connState)
            {
                case ConnectionState.Undefined:
                    break;
                case ConnectionState.PrepareConnect:
                    if (!_connThread.IsAlive)
                    {
                        if (_lastError != ConnectionError.None)
                        {
                            ConnectError(_lastError);
                            return;
                        }

                        _connThread = null;
                        _connState = ConnectionState.Connected;
                        Start();
                        Interlocked.Exchange(ref _lastSeen, NowMillis());
                        if (ConnectedEvent != null)
                        {
                            ConnectedEvent(this);
                        }
                    }
                    else if (_connectStartTime + InitParam.ConnectTimeout < NowMillis())
                    {
                        LogHelper.WarnF("RunConnect Timeout!");
                        ConnectError(ConnectionError.ConnectFailed);
                    }

                    break;
                case ConnectionState.Connected:
                    if (_lastError != ConnectionError.None)
                    {
                        RecvData();
                        ConnectError(_lastError);
                        return;
                    }

                    RecvData();
                    break;
                case ConnectionState.Closed:
                    break;
                default:
                    throw new System.NotImplementedException();
            }
        }

        public void SendData(GameNetPack pack) {
            if (_connState != ConnectionState.Connected) {
                throw new System.InvalidOperationException();
            }

            if (pack.GetByteSize() > ConnectionParam.MaxPackSize) {
                LogHelper.ErrorF("SendDataFail! PackSize = {0}, SendPoolSize = {1}",
                    pack.GetByteSize(),
                    _sendChan.ByteSize);
                ConnectError(ConnectionError.DataOverLimit);
                return;
            }

            if (pack.body == null) {
                throw new System.ArgumentNullException();
            }

            if (!_sendChan.TryPush(pack)) {
                ConnectError(ConnectionError.WriteBufferError);
            }
        }

        // force push to send queue
        // public void Flush()
        // {
        //     _sendChan.Push(default);
        // }

        private void RecvData()
        {
            GameNetPack pack = default;
            while (_recvChan.TryPop(ref pack))
            {
                if (RecvMsgEvent != null)
                {
                    RecvMsgEvent(this, pack);
                }

                if (_connState != ConnectionState.Connected)
                {
                    break;
                }
            }
        }

        private void WriteHeart()
        {
            try
            {
                var gameNetPack = new GameNetPack
                {
                    msgID = (ushort)InitParam.HeartMsgID,
                    body = new Heartbeat{ TimestampMs = NowMillis() }.ToByteArray(),
                };
                object writer = _writer;
                lock (writer)
                {
                    _marshaler.WritePack(ref gameNetPack, _writer);
                    if (InitParam.AutoFlush)
                    {
                        _writer.Flush();
                    }
                    if (HeartDebugLog)
                    {
                        LogHelper.InfoF("WriteHeart once! Now={0}", NowMillis());
                    }
                }
            }
            catch (Exception e)
            {
                LogHelper.ErrorF("WriteHeart err={0}", e);
                _lastError = ConnectionError.SendBroken;
            }
        }

        private void ReadHeart(ref GameNetPack pack)
        {
            var heart = new Heartbeat();
            heart.MergeFrom(pack.body);
            
            Interlocked.Exchange(ref _rtt, NowMillis() - heart.TimestampMs);
            Interlocked.Exchange(ref _heartOnTheAir, 0);
            if (HeartDebugLog)
            {
                LogHelper.InfoF("Recv HeartOnce! NowMs={0}", NowMillis());
            }
        }

        private void RunConnect()
        {
            try
            {
                ConnectInternal();
            }
            catch (System.Exception ex)
            {
                if (ex.GetBaseException() is ThreadAbortException)
                {
                    Thread.ResetAbort();
                    LogHelper.WarnF("RunConnect ThreadAbort err={0}", ex);
                    _lastError = ConnectionError.ConnectFailed;
                }
                else
                {
                    LogHelper.WarnF("RunConnect err={0}", ex);
                    _lastError = ConnectionError.ConnectFailed;
                }
            }
        }

        private void ConnectInternal()
        {
            _socket = InitParam.connType != ConnectionType.Tcp
                ? new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                : new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            bool isDataMode = InitParam.connType == ConnectionType.Rdp;

            _socket.Connect(InitParam.RemoteEndPoint);
            switch (InitParam.connType)
            {
                case ConnectionType.Tcp:
                    _stream = new NetworkStream(_socket, true);
                    break;
                case ConnectionType.Kcp:
                    _stream = new KcpStream(_socket, true);
                    break;
                case ConnectionType.Rdp:
                    _stream = new RdpStream(_socket, true);
                    break;
                default:
                    throw new InvalidEnumArgumentException("UnKnown ConnectionType");
            }

            _reader = new ProtocolReader(_stream, isDataMode);
            _writer = new ProtocolWriter(_stream, isDataMode);
        }

        private void RunSend()
        {
            do {
                try {
                    GameNetPack gameNetPack = _sendChan.Pop();
                    do {
                        if (gameNetPack.body == null) {
                            object writer = _writer;
                            lock (writer) {
                                _writer.Flush();
                            }
                        }
                        else {
                            object writer = _writer;
                            int sendSize;
                            lock (writer) {
                                sendSize = _marshaler.WritePack(ref gameNetPack, _writer);
                            }
                            _netStat.OnSend(sendSize);
                        }
                    } while (_sendChan.TryPop(ref gameNetPack));

                    if (InitParam.AutoFlush) {
                        object writer = _writer;
                        lock (writer) {
                            _writer.Flush();
                        }
                    }
                }
                catch (System.Exception ex) {
                    System.Exception baseException = ex.GetBaseException();
                    if (baseException is ThreadAbortException) {
                        Thread.ResetAbort();
                        _lastError = ConnectionError.SendThreadAbort;
                        break;
                    }

                    if (baseException is ThreadInterruptedException) {
                        _lastError = ConnectionError.SendThreadInterrupted;
                        break;
                    }

                    if (ex is System.IO.IOException) {
                        _lastError = ConnectionError.SendBroken;
                        break;
                    }

                    if (ex is ProtocolViolationException) {
                        _lastError = ConnectionError.ProtocolViolation;
                        break;
                    }

                    if (_connState == ConnectionState.Closed) {
                        break;
                    }

                    _lastError = ConnectionError.WorkThreadException;
                    _lastThreadException = ex;
                    LogHelper.WarnF("RunSend err={0}", ex);
                    break;
                }
            } while (_sendThread != null);
        }

        private void RunReceive()
        {
            GameNetPack pack = default;
            do
            {
                try
                {
                    object reader = _reader;
                    int recvSize;
                    lock (reader)
                    {
                        recvSize = _marshaler.ReadPack(_reader, ref pack);
                    }

                    // check Whether heart pack or not
                    if (pack.msgID == InitParam.HeartMsgID)
                    {
                        ReadHeart(ref pack);
                    }
                    else
                    {
                        _recvChan.Push(pack);
                    }

                    _netStat.OnReceive(recvSize);
                    Interlocked.Exchange(ref _lastSeen, NowMillis());
                }
                catch (System.Exception ex)
                {
                    System.Exception baseException = ex.GetBaseException();
                    if (baseException is ThreadAbortException)
                    {
                        _lastError = ConnectionError.RecvThreadAbort;
                        break;
                    }

                    if (baseException is ThreadInterruptedException)
                    {
                        _lastError = ConnectionError.RecvThreadInterrupted;
                        break;
                    }

                    if (ex is System.IO.IOException)
                    {
                        _lastError = ConnectionError.RecvBroken;
                        break;
                    }

                    if (ex is ProtocolViolationException)
                    {
                        _lastError = ConnectionError.ProtocolViolation;
                        break;
                    }

                    if (_connState == ConnectionState.Closed)
                    {
                        break;
                    }

                    _lastError = ConnectionError.WorkThreadException;
                    _lastThreadException = ex;
                    LogHelper.WarnF("RunReceive, err={0}", ex);
                    break;
                }
            } while (_recvThread != null);
        }

        public void Close()
        {
            if (_connState == ConnectionState.Closed)
            {
                return;
            }

            CloseInternal();
        }

        private void CloseInternal()
        {
            LogHelper.DebugF("CloseInternal Closing");
            _connState = ConnectionState.Closed;
            if (_heartTickTimer != null)
            {
                _heartTickTimer.Dispose();
                _heartTickTimer = null;
            }

            _sendThread = null;
            _recvThread = null;
            if (_connThread != null)
            {
                _connThread.Abort();
                _connThread = null;
            }

            if (_sendChan != null)
            {
                _sendChan.Interrupt();
                _sendChan.Dispose();
                _sendChan = null;
            }

            if (_recvChan != null)
            {
                _recvChan.Interrupt();
                _recvChan.Dispose();
                _recvChan = null;
            }

            try
            {
                if (_stream != null)
                {
                    _stream.Close();
                }

                if (_socket != null && _socket.Connected)
                {
                    _socket.Shutdown(SocketShutdown.Both);
                }
            }
            catch (System.Exception message)
            {
                LogHelper.WarnF("CloseInternal get Exception, err={0}", message);
            }
            finally
            {
                if (_socket != null)
                {
                    _socket.Close();
                    _socket = null;
                }
                _stream = null;
            }
        }
    }
}