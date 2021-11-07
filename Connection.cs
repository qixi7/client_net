using Public.Net.Codec;
using Public.Net.KCP;
using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using Public.Log;
using Public.Net.RDP;

namespace Public.Net
{
    public class Connection
    {
        private const int CompressThreshold = 128; // 压缩阈值, 低于该值不用压缩

        private static readonly Stopwatch Clock;

        private Pool _codecBufferPool;

        private Socket _socket;

        private System.IO.Stream _stream;

        private NetQueue _sendChan;

        private NetQueue _recvChan;

        private volatile Thread _sendThread;

        private volatile Thread _recvThread;

        private volatile Thread _connThread;

        private volatile ConnectionState _connState;

        public ConnectHandler ConnectedEvent;

        public ConnectErrorHandler ConnectErrorEvent;

        public RecvMsgHandler RecvMsgEvent;

        private volatile Timer _heartTickTimer;

        private volatile int _heartOnTheAir;

        private long _lastSeen; // for optimize heart.

        private volatile ConnectionError _lastError;

        private volatile System.Exception _lastThreadException;

        private long _connectStartTime;

        private ProtocolReader _reader;

        private ProtocolWriter _writer;

        private long _rtt;

        public ConnectionParam InitParam { get; }

        public double RTT
        {
            get
            {
                long num = Interlocked.Read(ref _rtt);
                if (num < 0L)
                {
                    return  num;
                }

                return (double) num / (double) Stopwatch.Frequency * 1000.0;
            }
        }

        public long LastSeen => Interlocked.Read(ref _lastSeen);

        public System.Exception LastThreadException => _lastThreadException;

        public ICodec Codec { private get; set; }

        static Connection()
        {
            Clock = Stopwatch.StartNew();
        }

        public Connection(ConnectionParam param)
        {
            InitParam = param;
            _sendChan = new NetQueue(16777216);
            _recvChan = new NetQueue(16777216);
            _connState = ConnectionState.Undefined;
            _codecBufferPool = new Pool(InitParam.MaxPackSize, 16);
        }

        public static long GetClockMS()
        {
            return Clock.ElapsedMilliseconds;
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
                if (heartTickTimer == null)
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
                    if (heartTimeDiff > (long) InitParam.HeartTickTimeout)
                    {
                        _lastError = ConnectionError.HeartTimeout;
                    }
                    else if (InitParam.LazyHeartbeat)
                    {
                        if (heartTimeDiff > (long) InitParam.HeartTickTime)
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
                    else if (_connectStartTime + (long) InitParam.connectTimeout < NowMillis())
                    {
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

        public void SendData(GameNetPack pack)
        {
            if (_connState != ConnectionState.Connected)
            {
                throw new System.InvalidOperationException();
            }

            if (pack.GetByteSize() > InitParam.MaxPackSize)
            {
                LogHelper.ErrorF("SendDataFail! PackSize = {0}, SendPoolSize = {1}",
                    pack.GetByteSize(),
                    _sendChan.ByteSize);
                ConnectError(ConnectionError.DataOverLimit);
                return;
            }

            if (pack.body == null)
            {
                throw new System.ArgumentNullException();
            }

            if (!_sendChan.TryPush(pack))
            {
                ConnectError(ConnectionError.WriteBufferError);
            }
        }

        // force push to send queue
        public void Flush()
        {
            _sendChan.Push(default);
        }

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
            GameNetPack gameNetPack = new GameNetPack
            {
                msgID = InitParam.heartMsgID,
                body = new byte[8]
            };
            BigEndian.PutBytes(Slice<byte>.Make(gameNetPack.body), (ulong) Now());
            object writer = _writer;
            lock (writer)
            {
                GameProtocol.WritePack(ref gameNetPack, _writer);
                if (InitParam.AutoFlush)
                {
                    _writer.Flush();
                }
            }
        }

        private void ReadHeart(ref GameNetPack pack)
        {
            long time = (long) BigEndian.ToUInt64(Slice<byte>.Make(pack.body));
            Interlocked.Exchange(ref _rtt, Now() - time);
            Interlocked.Exchange(ref _heartOnTheAir, 0);
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
                    _lastError = ConnectionError.ConnectFailed;
                }
                else
                {
                    LogHelper.ErrorF("err={0}", ex);
                    _lastError = ConnectionError.ConnectFailed;
                }
            }
        }

        private void ConnectInternal()
        {
            _socket = InitParam.connType != ConnectionType.TCP
                ? new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)
                : new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

            _socket.Connect(InitParam.RemoteEndPoint);
            switch (InitParam.connType)
            {
                case ConnectionType.TCP:
                    _stream = new NetworkStream(_socket, true);
                    break;
                case ConnectionType.KCP:
                    _stream = new KcpStream(_socket, true);
                    break;
                case ConnectionType.RDP:
                    RdpStream baseStream = new RdpStream(_socket, true)
                    {
                        WriteTimeout = InitParam.HeartTickTimeout
                    };
                    _stream = new DatagramStream(baseStream, InitParam.MaxSplitPackSize, 1200);
                    break;
                default:
                    throw new InvalidEnumArgumentException("UnKnown ConnectionType");
            }

            if ((InitParam.connType == ConnectionType.TCP || InitParam.connType == ConnectionType.KCP) &&
                InitParam.secure)
            {
                X509Certificate2 cert = new X509Certificate2(Certificate.Default);
                // 暂时为了省事, 直接比较证书二进制内容
                SslStream sslStream = new SslStream(_stream, false,
                    (object sender, System.Security.Cryptography.X509Certificates.X509Certificate c, X509Chain chain,
                        SslPolicyErrors sslPolicyErrors) => c.Equals(cert));
                sslStream.AuthenticateAsClient("ignored", new X509CertificateCollection
                {
                    cert
                }, SslProtocols.Tls, false);
                if (!sslStream.IsAuthenticated)
                {
                    throw new AuthenticationException("认证失败");
                }

                if (!sslStream.IsEncrypted)
                {
                    throw new AuthenticationException("连接未加密");
                }

                if (!sslStream.IsSigned)
                {
                    throw new AuthenticationException("连接未签名");
                }

                _stream = sslStream;
            }

            _reader = new ProtocolReader(_stream, false);
            _writer = new ProtocolWriter(_stream, false);
        }

        private void RunSend()
        {
            do
            {
                try
                {
                    GameNetPack gameNetPack = _sendChan.Pop();
                    do
                    {
                        if (gameNetPack.body == null)
                        {
                            object writer = _writer;
                            lock (writer)
                            {
                                _writer.Flush();
                            }
                        }
                        else
                        {
                            EncodePack(ref gameNetPack);
                            object writer2 = _writer;
                            int num;
                            lock (writer2)
                            {
                                num = GameProtocol.WritePack(ref gameNetPack, _writer);
                            }

                            NetStatistics.OnSend((long) num);
                        }
                    } while (_sendChan.TryPop(ref gameNetPack));

                    if (InitParam.AutoFlush)
                    {
                        object writer3 = _writer;
                        lock (writer3)
                        {
                            _writer.Flush();
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    System.Exception baseException = ex.GetBaseException();
                    if (baseException is ThreadAbortException)
                    {
                        Thread.ResetAbort();
                        break;
                    }

                    if (baseException is ThreadInterruptedException)
                    {
                        break;
                    }

                    if (ex is System.IO.IOException)
                    {
                        _lastError = ConnectionError.ServerClose;
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
                    LogHelper.ErrorF("RunSend err={0}", ex);
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
                    int num;
                    lock (reader)
                    {
                        num = GameProtocol.ReadPack(_reader, ref pack);
                    }

                    DecodePack(ref pack);
                    // todo. check Whether heart pack or not
                    if (pack.msgID == InitParam.heartMsgID)
                    {
                        ReadHeart(ref pack);
                    }
                    else
                    {
                        _recvChan.Push(pack);
                    }

                    NetStatistics.OnReceive((long) num);
                    Interlocked.Exchange(ref _lastSeen, NowMillis());
                }
                catch (System.Exception ex)
                {
                    System.Exception baseException = ex.GetBaseException();
                    if (baseException is ThreadAbortException)
                    {
                        Thread.ResetAbort();
                        break;
                    }

                    if (baseException is ThreadInterruptedException)
                    {
                        break;
                    }

                    if (ex is System.IO.IOException)
                    {
                        _lastError = ConnectionError.ServerClose;
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
                    LogHelper.ErrorF("RunReceive, error={0}", ex);
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

                else if (_socket != null && _socket.Connected)
                {
                    _socket.Shutdown(SocketShutdown.Both);
                    _socket.Close();
                }
            }
            catch (System.Exception message)
            {
                LogHelper.WarnF("CloseInternal get Exception, err={0}", message);
            }

            _stream = null;
            _socket = null;
        }

        private void EncodePack(ref GameNetPack pack)
        {
            if (Codec == null) return;
            if (pack.body.Length <= CompressThreshold) return;
            var buf = Pools.Get(pack.body.Length);
            try
            {
                var n = Codec.Encode(pack.body, buf);
                if (n <= 0) return;
                pack.body = null; // early dereference
                var b = new byte[n];
                System.Array.Copy(buf, b, n);
                pack.flag |= (byte) GameNetPackFlag.Compressed;
                pack.body = b;
            }
            finally
            {
                Pools.Put(buf);
            }
        }

        private void DecodePack(ref GameNetPack pack)
        {
            if ((pack.flag & (byte) GameNetPackFlag.Compressed) == 0) return;
            if (Codec != null)
            {
                var buf = _codecBufferPool.Get();
                try
                {
                    var n = Codec.Decode(pack.body, buf);
                    pack.body = new byte[n];
                    System.Array.Copy(buf, pack.body, n);
                }
                catch (System.Exception ex)
                {
                    LogHelper.ErrorF("cannot decompress packet: {0}", ex.Message);
                    throw;
                }
                finally
                {
                    _codecBufferPool.Put(buf);
                }
            }
            else
            {
                LogHelper.ErrorF("received a compressed packet but codec is not set");
            }
        }
    }
}