using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using Public.Log;
//using UnityEngine;

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

namespace Public.Net.RDP
{
	public class RdpStream : Stream
	{
		private const int QueueSize = 64;

		private const int MaxLoadSize = 300;

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
					if (!Monitor.TryEnter(sendBuf))
					{
						return;
					}
					try
					{
						SendPacket(sendBuf);
						_RdpDebugLog("send RDP dial buffer.");
					}
					catch (Exception exception)
					{
//						Debug.LogException(exception);
					}
					finally
					{
						Monitor.Exit(sendBuf);
					}
				}, null, 0, 50))
				{
					// wait for handshake.
					while (true)
					{
						int num = socket.Receive(recvBuf.BaseArray, recvBuf.From, recvBuf.Length, SocketFlags.None);
						num = Datagram.Open(recvBuf.BaseArray, num);
						if (num >= 1)
						{
							PacketHeader packetHeader = default;
							num = packetHeader.ReadFrom(recvBuf.Cut(0, num));
							_RdpDebugLog("ReadFrom PacketKind={0}.",packetHeader.Kind.ToString());
							if (num > 0 && packetHeader.Kind == PacketKind.DialAck)
							{
								break;
							}
						}
					}
					_RdpDebugLog("begin Resend.");
					_resendTimer = new Timer(Resend, null, 10, 10);
				}
			}
			finally
			{
				BufferPool.Put(sendBuf.BaseArray);
				BufferPool.Put(recvBuf.BaseArray);
			}
		}

		private void Resend(object _)
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
			finally
			{
				Monitor.Exit(_writeMutex);
			}
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_closed)
			{
				throw new ObjectDisposedException("RdpStream");
			}
			Slice<byte> b = Slice<byte>.Make(buffer, offset, offset + count);
			object readMutex = _readMutex;
			int result;
			lock (readMutex)
			{
				bool flag = false;
				while (true)
				{
					PacketLoad packetLoad = default;
					if (_recv.Read(ref packetLoad))
					{
						try
						{
							SendAck();
							result = packetLoad.Buffer.CopyTo(b);
							break;
						}
						finally
						{
							BufferPool.Put(packetLoad.Buffer.BaseArray);
						}
					}
					if (flag)
					{
						SendAck();
					}
					flag = TransferToQueue();
				}
			}
			return result;
		}

		private bool TransferToQueue()
		{
			byte[] array = BufferPool.Get();
			bool result;
			try
			{
				int num;
				Slice<byte> data;
				while (true)
				{
					int size = _socket.Receive(array);

					num = Datagram.Open(array, size);
					if (num > 0)
					{
						data = Slice<byte>.Make(array, 0, num);
						_RdpDebugLog("TransferToQueue data Read. to={0}.", num);
						if (_DebugLog)
						{
							for (int i = 0; i < data.Length; i++)
							{
								_RdpDebugLog("{0}", data.Get(i));
							}
						}
						_RdpDebugLog("TransferToQueue data Read end.");
						PacketHeader packetHeader = default;
						num = packetHeader.ReadFrom(data);
						if (num > 0)
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

				data = data.Cut(num);
				while (true)
				{
					PacketLoad packet = new PacketLoad {Buffer = Slice<byte>.Make(BufferPool.Get())};
					int n = packet.ReadFrom(data);
					if (n==0)
					{
						packet.Free();
						break;
					}
					if (_recv.Set(packet))
					{
						_RdpDebugLog("_recv Set ok. seq={0}, n={1}, data.len={2}, packet.Size={3}, buffer.len={4}",
							packet.Seq, n,data.Length, packet.Size, packet.Buffer.Length);
					}
					data = data.Cut(n);
				}
				result = true;
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
				_RdpDebugLog("SendAck {0}", ack);
			}
			finally
			{
				BufferPool.Put(slice.BaseArray);
			}
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			if (_closed)
			{
				throw new ObjectDisposedException("RdpStream");
			}
			if (count > MaxLoadSize)
			{
				throw new ArgumentOutOfRangeException(nameof(count), "MTU exceeded");
			}
			Slice<byte> slice = Slice<byte>.Make(buffer, offset, offset + count);
			object writeMutex = _writeMutex;
			lock (writeMutex)
			{
				int writeTimeout = WriteTimeout;
				long nowMs = Connection.NowMillis();
				Slice<byte> slice2 = Slice<byte>.Make(BufferPool.Get());
				try
				{
					PacketLoad load = new PacketLoad
					{
						Seq = _seq,
						Size = count,
						Buffer = Slice<byte>.Make(BufferPool.Get())
					};
					long nowTimestamp = Connection.Now();
					load.Buffer = load.Buffer.Cut(0, slice.CopyTo(load.Buffer));
					load.Timestamp = nowTimestamp;
					while (!_send.Write(load))
					{
						if (Connection.NowMillis() > nowMs + writeTimeout)
						{
							throw new TimeoutException("RDP Write");
						}
						if (Monitor.TryEnter(_readMutex))
						{
							try
							{
								TransferToQueue();
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
							throw new ObjectDisposedException("RdpStream");
						}
					}
					int headerLen = new PacketHeader
					{
						Kind = PacketKind.Data
					}.WriteTo(slice2);
					headerLen += load.WriteTo(slice2.Cut(headerLen));
					long minRTT = _window.Min();
					PacketLoad packetLoad = default;
					for (uint i = 1; i <= 3; i++)
					{
						if (_send.Get(i, ref packetLoad))
						{
							if (!Datagram.SeqSign(packetLoad.Seq - load.Seq) || headerLen + 6 + packetLoad.Size + 4 > Datagram.Mtu)
							{
								break;
							}
							if (nowTimestamp - packetLoad.Timestamp >= minRTT)
							{
								headerLen += packetLoad.WriteTo(slice2.Cut(headerLen));
							}
						}
					}
					slice2 = slice2.Cut(0, headerLen);
					try
					{
						SendPacket(slice2);
					}
					catch (Exception)
					{
						_send.Clear(_seq);
						throw;
					}
				}
				finally
				{
					BufferPool.Put(slice2.BaseArray);
				}
				_seq++;
			}
		}

		private void SendPacket(Slice<byte> packet)
		{
			if (!Datagram.Seal(packet.BaseArray, packet.Length))
			{
				return;
			}
			int size = packet.Length + 4;
			try
			{
				_socket.Send(packet.BaseArray, packet.From, size, SocketFlags.None);
			}
			catch (Exception)
			{
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
				int num = new PacketHeader
				{
					Kind = PacketKind.Data
				}.WriteTo(slice);
				bool flag = false;
				long num2 = Connection.Now();
				long num3 = _window.Min();
				PacketLoad packetLoad = default;
				for (uint num4 = 0; num4 < QueueSize; num4++)
				{
					if (_send.Get(num4, ref packetLoad))
					{
						if (!Datagram.SeqSign(packetLoad.Seq - _seq) ||
						    num + PacketLoad.Overhead + packetLoad.Size + Datagram.Overhead > Datagram.Mtu)
						{
							break;
						}
						if (num2 - packetLoad.Timestamp >= num3)
						{
							num += packetLoad.WriteTo(slice.Cut(num));
							flag = true;
						}
					}
				}
				if (flag)
				{
					slice = slice.Cut(0, num);
					SendPacket(slice);
				}
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
	}
}
