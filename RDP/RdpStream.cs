using System;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using NetModule.Log;

/***
		RDP - Redundant Data Protocol
一、概述
	冗余传输协议。
	1).基于UDP. 算法实现
	2).通过冗余包实现可靠传输
	3).协议层摒弃定时器以更大提升性能, 依赖外部定时器
	4).更短的RTO. (TCP->指数增长, KCP->1.5倍增长)
	5).Ack+Select Ack更快确认机制
	6).支持链路探路探测控制冗余包粒度

二、缺点
	1).为了更快到达对端, 没有实现拥塞控制, 因此使用在小包传输场景。比如帧同步

三、TODO.
	1).根据探测结果动态调整冗余个数

四、协议序
	ACK包: [协议头(1字节) + 包类型(1字节) + ack(4字节) + ackBit(4字节)]
	数据包: [协议头(1字节) + 包类型(1字节) + seq(4字节) + bodySize(2字节) + 是否子包(1字节) + body]
	其他包: [协议头(1字节) + 包类型(1字节)]
*/

namespace NetModule.RDP
{
	public class RdpStream : Stream
	{
		// 可改 Begin
		static public uint QueueSize = 256;
		static public int RedundantNum = 3;	// 冗余包数
		static public bool ConnectLog = false;
		static public bool BufLog = false;
		static public bool PackLog = false;
		static public bool PackTypeLog = false;
		static public bool SeqLog = false;
		static public bool AckLog = false;
		static public bool QueueLog = false;
		static public bool SendErrLog = false;
		// 可改 End

		private const int MaxLoadSize = Datagram.Mtu - (2+PacketLoad.Overhead+Datagram.CheckSumSize);
		private readonly bool _ownsSocket;

		private readonly Socket _socket;

		private volatile bool _closed;

		private readonly object _readMutex = new object();

		private readonly object _writeMutex = new object();

		private readonly AutoResetEvent _writeBlockEvent = new AutoResetEvent(true);

		private readonly Window _window = new Window();

		private readonly ISendQueue _send = new Queue(QueueSize, "send");

		private readonly IRecvQueue _recv = new Queue(QueueSize, " recv");
        
        // private byte[] _buffer = new byte[Datagram.Mtu];
        private byte[] _buffer = new byte[65536];

		private uint _seq;

		private volatile Timer _resendTimer;

		private volatile bool _sent;

		public override int WriteTimeout
		{
			get;
			set;
		}

		public override bool CanRead => !_closed;

		public override bool CanSeek => false;

		public override bool CanWrite => !_closed;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}
		
		// for log
		static StringBuilder recvPackLog = new StringBuilder();
		static StringBuilder sendPackLog = new StringBuilder();
		static StringBuilder flushPackLog = new StringBuilder();

		public RdpStream(Socket socket, bool ownsSocket = false)
		{
			if (socket == null)
			{
				throw new ArgumentNullException(nameof(socket));
			}
			_socket = socket;
			_ownsSocket = ownsSocket;
			Slice<byte> sendBuf = Slice<byte>.Make(BufferPool.Get());
			Slice<byte> recvBuf = Slice<byte>.Make(BufferPool.Get());
			try
			{
				// dial.
				sendBuf = sendBuf.Cut(0, new PacketHeader
				{
					Kind = PacketKind.Dial
				}.WriteTo(sendBuf));
				using (new Timer(delegate
				       {
					       try
					       {
							   if (_closed)
                               {
                                   return;
                               }
						       SendPacket(sendBuf);
						       _RdpConnectLog("send RDP dial buffer.");
					       }
					       catch (Exception exception)
					       {
						       LogHelper.ErrorF("send RDP dial err={0}", exception);
					       }
				       }, null, 0, 50))
			
					// wait for handshake.
					while (true)
					{
						int byteSize = socket.Receive(recvBuf.BaseArray, recvBuf.From, recvBuf.Length, SocketFlags.None);
						byteSize = Datagram.Open(recvBuf.BaseArray, byteSize);
						if (byteSize >= 1)
						{
							PacketHeader packetHeader = default;
							byteSize = packetHeader.ReadFrom(recvBuf.Cut(0, byteSize));
							_RdpConnectLog("wait DialAck Read packetHeader PacketKind={0}.",packetHeader.Kind.ToString());
							if (byteSize > 0 && packetHeader.Kind == PacketKind.DialAck)
							{
								break;
							}
						}
					}
				_resendTimer = new Timer(RunSend, null, 10, 10);
			}
			catch (Exception e)
			{
				LogHelper.ErrorF("NewRdpStream err={0}", e);
			}
			finally
			{
				BufferPool.Put(sendBuf.BaseArray);
				BufferPool.Put(recvBuf.BaseArray);
			}
		}

		private void RunSend(object _)
        {
			if (_closed)
			{
				return;
			}
			if (_sent)
			{
				_sent = false;
				return;
			}

            object writeMutex = _writeMutex;
            lock (writeMutex)
            {
	            FlushInternal();
	            // var minRtt = _window.HistoryMin();
                // _resendTimer.Change(minRtt,minRtt);
            }
        }
        
		// 对UDP来说, 并不知道一次Read能读多少count, 所以先不用这个count
		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_closed)
			{
				throw new ObjectDisposedException("RdpStream");
			}
			Slice<byte> readBuf = Slice<byte>.Make(buffer, offset);
			object readMutex = _readMutex;
			int readCount = 0;
			lock (readMutex)
			{
				bool recvAny = false;
				while (true)
				{
					if (_closed)
					{
						throw new ObjectDisposedException("RdpStream closed"); 
					}
					PacketLoad packetLoad = default;
					if (_recv.Read(ref packetLoad))
					{
						try
						{
							SendAck();
							// 把packetLoad copy 到 b[readCount:]
							packetLoad.Buffer.CopyTo(readBuf.Cut(readCount));
							readCount += packetLoad.Size;
							// 判断是否分包
							if (packetLoad.SubPacket)
							{
								// LogHelper.InfoF("SubPacket wait.");
								continue;
							}
							// LogHelper.InfoF("Final Packet arrive.");
							break;
						}
						catch (Exception e)
						{
							LogHelper.ErrorF("Read err={0}", e);
						}
						finally
						{
							BufferPool.Put(packetLoad.Buffer.BaseArray);
						}
					}
					// 写到这里是为了保证只SendAck一次, 上面Read后也会SendAck
					if (recvAny)
					{
						SendAck();
					}
					recvAny = TransferToQueue();
				}
			}
			return readCount;
		}

		private bool TransferToQueue()
		{
			// byte[] array = BufferPool.Get();
			bool result = false;

			int byteSize;
			Slice<byte> data;
			while (true)
			{
				if (_closed)
				{
					return false;
				}
				int size = _socket.Receive(_buffer);
				byteSize = Datagram.Open(_buffer, size);
				if (byteSize > 0)
				{
					data = Slice<byte>.Make(_buffer, 0, byteSize);
					
					if (BufLog)
					{
						var readLogStr = new StringBuilder();
						readLogStr.AppendFormat("transfer data Read. binLen={0}. data=", byteSize);
						for (int i = 0; i < data.Length; i++)
						{
							readLogStr.AppendFormat("{0},", data.Get(i).ToString());
						}
						_RdpBufLog(readLogStr.ToString());
					}

					PacketHeader packetHeader = default;
					byteSize = packetHeader.ReadFrom(data);
					if (byteSize > 0)
					{
						PacketKind kind = packetHeader.Kind;
						if (kind == PacketKind.Data)
						{
							break;
						}
						if (kind == PacketKind.Ack)
						{
							if (_send.Ack(packetHeader.Ack, packetHeader.AckBits, _window) > 0)
							{
								_writeBlockEvent.Set();
							}
						}
					}
				}
			}
		
			// 取body
			data = data.Cut(byteSize);

			recvPackLog.Clear();
			recvPackLog.Append("Recv Pack: [");
			while (true)
			{
				PacketLoad packet = new PacketLoad {Buffer = Slice<byte>.Make(BufferPool.Get())};
				byteSize = packet.ReadFrom(data);
				if (byteSize == 0)
				{
					packet.Free();
					break;
				}

				_recv.Set(packet);
				data = data.Cut(byteSize);
			
				if (SeqLog)
				{
					recvPackLog.AppendFormat("{0}, ", packet.Seq.ToString());
				}
			}
			if (SeqLog)
			{
				recvPackLog.AppendFormat("]");
				_RdpSeqLog( recvPackLog.ToString());
			}
			result = true;
			return result;
		}

		private void SendAck()
		{
			uint ack;
			uint ackBits;
			_recv.GetAck(out ack, out ackBits);
			Slice<byte> slice = Slice<byte>.Make(BufferPool.Get());
			try
			{
				PacketHeader packetHeader = default;
				packetHeader.Ack = ack;
				packetHeader.AckBits = ackBits;
				packetHeader.Kind = PacketKind.Ack;
				slice = slice.Cut(0, packetHeader.WriteTo(slice));
				SendPacket(slice);
				_RdpAckLog("Send ack={0}, ackBits={1}", ack, ackBits);
			}
			catch (Exception e)
			{
				if (SendErrLog)
				{
					LogHelper.ErrorF("SendAck err={0}", e);
				}
			}
			finally
			{
				BufferPool.Put(slice.BaseArray);
			}
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			int mss = MaxLoadSize;
			int allCount = count;
			int nowOffset = offset;
			object writeMutex = _writeMutex;
			lock (writeMutex)
			{
				while (allCount > mss)
				{
					WriteOne(buffer, nowOffset, mss, allCount-mss > 0);
					nowOffset += mss;
					allCount -= mss;
				}
				if (allCount > 0)
				{
					WriteOne(buffer, nowOffset, allCount, false);
				}
			}
		}

		public void WriteOne(byte[] buffer, int offset, int count, bool subPacket)
		{
			if (_closed)
			{
				throw new ObjectDisposedException("RdpStream Write Close");
			}
			if (count > MaxLoadSize)
			{
				throw new ArgumentOutOfRangeException(nameof(count), "MTU exceeded");
			}
			Slice<byte> srcBuff = Slice<byte>.Make(buffer, offset, offset + count);
			int writeTimeout = WriteTimeout;
			long nowMs = Connection.NowMillis();
			Slice<byte> loadBuf = Slice<byte>.Make(BufferPool.Get());
			try
			{
				PacketLoad load = new PacketLoad
				{
					Seq = _seq,
					Size = count,
					SubPacket = subPacket,
					Buffer = Slice<byte>.Make(BufferPool.Get())
				};
				long nowTimestamp = Connection.NowMillis();
				load.Buffer = load.Buffer.Cut(0, srcBuff.CopyTo(load.Buffer));
				load.Timestamp = nowTimestamp;
				
				if (BufLog)
				{
					var writeLogStr = new StringBuilder();
					writeLogStr.AppendFormat("writeOne. binLen={0}. data=", load.Buffer.Length);
					for (int i = 0; i < load.Buffer.Length; i++)
					{
						writeLogStr.AppendFormat("{0},", load.Buffer.Get(i).ToString());
					}
					_RdpBufLog(writeLogStr.ToString());
				}
				
				while (!_send.Write(load))
                {
                    if (writeTimeout > 0 && Connection.NowMillis() > nowMs + writeTimeout)
					{
						throw new TimeoutException("RDP Write Timeout");
					}
					// if (Monitor.TryEnter(_readMutex))
					// {
					// 	try
					// 	{
					//      TransferToQueue();
					// 	}
					// 	catch (Exception e)
					// 	{
					// 		LogHelper.ErrorF("Write 1 err={0}", e);
					// 	}
					// 	finally
					// 	{
					// 		Monitor.Exit(_readMutex);
					// 	}
					// }
                    FlushInternal();
					_writeBlockEvent.WaitOne(10);
					if (_closed)
					{
						throw new ObjectDisposedException("RdpStream closed");
					}
				}
				int sendSize = new PacketHeader
				{
					Kind = PacketKind.Data
				}.WriteTo(loadBuf);
				sendSize += load.WriteTo(loadBuf.Cut(sendSize));
				PacketLoad tmpPacket = default;
                // long minRTT = _window.Min();

                if (SeqLog)
                {
	                sendPackLog.Clear();
	                sendPackLog.AppendFormat("Send Pack: [{0}", load.Seq.ToString());
                }

				for (uint i = 1; i <= RedundantNum; i++)
				{
					if (_seq < i || !_send.GetSeqPacket(_seq - i, ref tmpPacket))
					{
						continue;
					}
                    // if (nowTimestamp - tmpPacket.Timestamp < minRTT)
                    // {
                    //     continue;
                    // }
					if (!Datagram.After(load.Seq, tmpPacket.Seq))
					{
						break;
					}
					if (sendSize + PacketLoad.Overhead + tmpPacket.Size + Datagram.CheckSumSize > Datagram.Mtu)
					{
						break;
					}
                    sendSize += tmpPacket.WriteTo(loadBuf.Cut(sendSize));
					// 冗余packetLoad.Seq
					if (SeqLog)
					{
						sendPackLog.AppendFormat(", {0}", tmpPacket.Seq.ToString());
					}
				}
				if (SeqLog)
				{
					sendPackLog.Append("]");
					_RdpSeqLog(sendPackLog.ToString());
				}

				loadBuf = loadBuf.Cut(0, sendSize);
				SendPacket(loadBuf);
			}
			catch (Exception e)
			{
				throw new Exception($"RdpStream Write err:{e}");
			}
			finally
			{
				BufferPool.Put(loadBuf.BaseArray);
			}
			_seq++;
		}

		private void SendPacket(Slice<byte> packet)
		{
			if (_closed)
            {
                return;
            }
			if (!Datagram.Seal(packet.BaseArray, packet.Length))
			{
				LogHelper.ErrorF("SendPacket err! BaseArray.Len={0}, packet.Length={1}",
					packet.BaseArray.Length, packet.Length);
				return;
			}
			int size = packet.Length + Datagram.CheckSumSize;
			// 如果连接凉凉了, 就不要再发了
			_socket.Send(packet.BaseArray, packet.From, size, SocketFlags.None);
        }

		public override void Close()
		{
			_closed = true;
			if (_resendTimer != null)
			{
				_resendTimer.Dispose();
				_resendTimer = null;
			}
			
			// if (_ownsSocket)
			// {
			// 	_socket.Close();
			// }
			while (!Monitor.TryEnter(_writeMutex))
			{
				_writeBlockEvent.Set();
				Thread.Sleep(1);
			}
			Monitor.Exit(_writeMutex);
			base.Close();
		}

		public override void Flush()
		{
		}

        // 所有queue都过一遍
		private void FlushInternal()
		{
            Slice<byte> slice = Slice<byte>.Make(BufferPool.Get());
            try
            {
                var dataPackHeader = new PacketHeader { Kind = PacketKind.Data };
                int sendSize = dataPackHeader.WriteTo(slice);
                bool flag = false;
                long nowMs = Connection.NowMillis();
                long minRtt = _window.HistoryMin();
                PacketLoad packetLoad = default;
                
                if (SeqLog)
                {
	                flushPackLog.Clear();
	                flushPackLog.Append("Flush Send Pack: [");
                }
                for (uint i = 0; i < QueueSize; i++)
                {
                    if (!_send.Get(i, ref packetLoad))
                    {
                        continue;
                    }
                    if (nowMs - packetLoad.Timestamp < minRtt)
                    {
                        continue;
                    }
                    if (!Datagram.After(_seq, packetLoad.Seq))
                    {
                        break;
                    }
                    if (sendSize + PacketLoad.Overhead + packetLoad.Size + Datagram.CheckSumSize > Datagram.Mtu)
                    {
                        // 该发了
                        if (flag)
                        {
                            slice = slice.Cut(0, sendSize);
                            SendPacket(slice);
                            if (SeqLog)
                            {
	                            flushPackLog.Append("] | [");
                            }
                            // 重置slice
                            slice.Reset();
                            // 重置sendSize
                            sendSize = dataPackHeader.WriteTo(slice);
                            // 重置flag
                            flag = false;
                        }
                    }
                    sendSize += packetLoad.WriteTo(slice.Cut(sendSize));
                    flag = true;
                    
                    if (SeqLog)
                    {
	                    flushPackLog.AppendFormat("{0}, ", packetLoad.Seq.ToString());
                    }
                }
                if (flag)
                {
                    slice = slice.Cut(0, sendSize);
                    SendPacket(slice);
					
                    if (SeqLog)
                    {
	                    flushPackLog.Append("]");
	                    _RdpSeqLog(flushPackLog.ToString());
                    }
                }
            }
            catch (Exception e)
            {
	            if (SendErrLog)
	            {
		            LogHelper.ErrorF("Flush err={0}", e);
	            }
            }
            finally
            {
                BufferPool.Put(slice.BaseArray);
                _sent = true;
            }
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}

		public static void _RdpConnectLog(string format, params object[] arg)
		{
			if (ConnectLog)
			{
				LogHelper.DebugF("[Conn] "+format,arg);
			}
		}
		
		public static void _RdpBufLog(string format, params object[] arg)
		{
			if (BufLog)
			{
				LogHelper.DebugF("[Buf] "+format,arg);
			}
		}
		
		public static void _RdpPackLog(string format, params object[] arg)
		{
			if (PackLog)
			{
				LogHelper.DebugF("[Pack] "+format,arg);
			}
		}
		
		public static void _RdpPackTypeLog(string format, params object[] arg)
		{
			if (PackTypeLog)
			{
				LogHelper.DebugF("[PackType] "+format,arg);
			}
		}

		public static void _RdpSeqLog(string format, params object[] arg)
		{
			if (SeqLog)
			{
				LogHelper.DebugF("[Seq] "+ format,arg);
			}
		}
		
		public static void _RdpAckLog(string format, params object[] arg)
		{
			if (AckLog)
			{
				LogHelper.DebugF("[ACK] "+format,arg);
			}
		}
		
		public static void _RdpQueLog(string format, params object[] arg)
		{
			if (QueueLog)
			{
				LogHelper.DebugF("[Que] "+ format,arg);
			}
		}
	}
}
