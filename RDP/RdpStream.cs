using System;
using System.IO;
using Public.Log;
using System.Net.Sockets;
using System.Threading;

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
	[协议头(1字节) + 包类型(1字节) + ack(4位) + ackBit(4位)]
*/

namespace NetModule.RDP
{
	public class RdpStream : Stream
	{
		private const int QueueSize = 256;

		private const int MaxLoadSize = 300;
		private const int RedundantNum = 3;	// 冗余包数
		private readonly bool _ownsSocket;

		private readonly Socket _socket;

		private volatile bool _closed;

		private readonly object _readMutex = new object();

		private readonly object _writeMutex = new object();

		private readonly AutoResetEvent _writeBlockEvent = new AutoResetEvent(true);

		private readonly Window _window = new Window();

		private readonly ISendQueue _send = new Queue(QueueSize);

		private readonly IRecvQueue _recv = new Queue(QueueSize);

		private uint _seq;

		private readonly Timer _resendTimer;

		private volatile bool _sent;
		private const bool _DebugLog = false;
		private const bool _SendRecvLog = false;
		private const bool _AckLog = false;
		private const bool _QueueLog = false;

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
						       SendPacket(sendBuf);
						       _RdpDebugLog("send RDP dial buffer.");
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
							_RdpDebugLog("ReadFrom PacketKind={0}.",packetHeader.Kind.ToString());
							if (byteSize > 0 && packetHeader.Kind == PacketKind.DialAck)
							{
								break;
							}
						}
					}
				_RdpDebugLog("begin RunSend.");
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
			if (_sent)
			{
				_sent = false;
				return;
			}
			if (!Monitor.TryEnter(_writeMutex))
			{
				return;
			}
			try
			{
				FlushInternal();
			}
			catch (Exception e)
			{
				LogHelper.ErrorF("RunSend err={0}", e);
			}
			finally
			{
				Monitor.Exit(_writeMutex);
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
			byte[] array = BufferPool.Get();
			bool result = false;
			try
			{
				int byteSize;
				Slice<byte> data;
				while (true)
				{
					if (_closed)
					{
						return false;
					}

					int size = _socket.Receive(array);
					byteSize = Datagram.Open(array, size);
					if (byteSize > 0)
					{
						data = Slice<byte>.Make(array, 0, byteSize);
						_RdpDebugLog("TransferToQueue data Read. binLen={0}.", byteSize);
						if (_DebugLog)
						{
							var binStr = ""; 
							for (int i = 0; i < data.Length; i++)
							{
								binStr += data.Get(i).ToString();
								binStr += ", ";
							}
							_RdpDebugLog("data={0}", binStr);
						}
						_RdpDebugLog("TransferToQueue data Read end.");
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
				var recvPack = "Recv Pack: [";
				while (true)
				{
					PacketLoad packet = new PacketLoad {Buffer = Slice<byte>.Make(BufferPool.Get())};
					byteSize = packet.ReadFrom(data);
					if (byteSize == 0)
					{
						packet.Free();
						break;
					}
					if (_recv.Set(packet))
					{
						_RdpDebugLog("_recv Set ok. seq={0}, n={1}, data.len={2}, packet.Size={3}, buffer.len={4}",
							packet.Seq, byteSize, data.Length, packet.Size, packet.Buffer.Length);
					}
					data = data.Cut(byteSize);
				
					recvPack += packet.Seq.ToString();
					recvPack += ", ";
				}
				recvPack += "]";
				_RdpSendRecvLog(recvPack);
				result = true;
			}
			catch (Exception e)
			{
				if (!_closed)
				{
					LogHelper.ErrorF("Transfer to queue err={0}", e);					
				}
			}
			finally
			{
				BufferPool.Put(array);
			}
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
				_RdpAckLog("Send Recv Ack {0}", ack);
			}
			catch (Exception e)
			{
				LogHelper.ErrorF("SendAck err={0}", e);
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
				while (!_send.Write(load))
				{
					if (Connection.NowMillis() > nowMs + writeTimeout)
					{
						throw new TimeoutException("RDP Write Timeout");
					}
					if (Monitor.TryEnter(_readMutex))
					{
						try
						{
							TransferToQueue();
						}
						catch (Exception e)
						{
							LogHelper.ErrorF("Write 1 err={0}", e);
						}
						finally
						{
							Monitor.Exit(_readMutex);
						}
					}
					FlushInternal();
					_writeBlockEvent.WaitOne(100);
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
			
				var sendPackStr = "Normal Send Pack [" + load.Seq;
				for (uint i = 0; i < RedundantNum; i++)
				{
					if (!_send.Get(i, ref tmpPacket))
					{
						continue;
					}
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
					sendPackStr += ", ";
					sendPackStr += tmpPacket.Seq.ToString();
				}
				sendPackStr += "]";
				_RdpSendRecvLog(sendPackStr);
			
				loadBuf = loadBuf.Cut(0, sendSize);
				try
				{
					SendPacket(loadBuf);
				}
				catch (Exception e)
				{
					LogHelper.ErrorF("Write 2 err={0}", e);
					_send.Clear(_seq);
					throw;
				}
			}
			catch (Exception e)
			{
				LogHelper.ErrorF("Stream Write err={0}", e);
			}
			finally
			{
				BufferPool.Put(loadBuf.BaseArray);
			}
			_seq++;
		}

		private void SendPacket(Slice<byte> packet)
		{
			if (!Datagram.Seal(packet.BaseArray, packet.Length))
			{
				LogHelper.ErrorF("SendPacket err! BaseArray.Len={0}, packet.Length={1}",
					packet.BaseArray.Length, packet.Length);
				return;
			}
			int size = packet.Length + Datagram.CheckSumSize;
			try
			{
				// 如果连接凉凉了, 就不要再发了
				_socket.Send(packet.BaseArray, packet.From, size, SocketFlags.None);
			}
			catch (Exception e)
			{
				LogHelper.ErrorF("Send Rdp data err={0}", e);
				Close();
				return;
			}
			_sent = true;
		}

		public override void Close()
		{
			_closed = true;
			_resendTimer.Dispose();
			if (_ownsSocket)
			{
				_socket.Close();
			}
			while (!Monitor.TryEnter(_writeMutex))
			{
				_writeBlockEvent.Set();
				Thread.Sleep(1);
			}
			Monitor.Exit(_writeMutex);
		}

		public override void Flush()
		{
		}

		private void FlushInternal()
		{
			Slice<byte> slice = Slice<byte>.Make(BufferPool.Get());
			try
			{
				int sendSize = new PacketHeader
				{
					Kind = PacketKind.Data
				}.WriteTo(slice);
				bool flag = false;
				PacketLoad packetLoad = default;
				var sendPackStr = "Flush Send Pack [";
				for (uint i = 0; i < QueueSize; i++)
				{
					if (!_send.Get(i, ref packetLoad))
					{
						continue;
					}
					if (!Datagram.After(_seq, packetLoad.Seq))
					{
						break;
					}
					if (sendSize + PacketLoad.Overhead + packetLoad.Size + Datagram.CheckSumSize > Datagram.Mtu)
					{
						break;
					}
					sendSize += packetLoad.WriteTo(slice.Cut(sendSize));
					flag = true;
					sendPackStr += packetLoad.Seq.ToString();
					sendPackStr += ", ";
				}
				if (flag)
				{
					slice = slice.Cut(0, sendSize);
					SendPacket(slice);
				
					sendPackStr += "]";
					_RdpSendRecvLog(sendPackStr);
				}
			}
			catch (Exception e)
			{
				LogHelper.ErrorF("Flush err={0}", e);
			}
			finally
			{
				BufferPool.Put(slice.BaseArray);
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

		public static void _RdpDebugLog(string format, params object[] arg)
		{
			if (_DebugLog)
			{
				LogHelper.DebugF(format,arg);
			}
		}
	
		public static void _RdpSendRecvLog(string format, params object[] arg)
		{
			if (_SendRecvLog)
			{
				LogHelper.DebugF(format,arg);
			}
		}
		
		public static void _RdpAckLog(string format, params object[] arg)
		{
			if (_AckLog)
			{
				LogHelper.DebugF(format,arg);
			}
		}
		
		public static void _RdpQueLog(string format, params object[] arg)
		{
			if (_QueueLog)
			{
				LogHelper.DebugF(format,arg);
			}
		}
	}
}