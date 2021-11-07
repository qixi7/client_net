using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;

namespace Public.Net.KCP
{
	internal class KcpStream :Stream
	{
		private const int MaxPacketSize = 65536;

		private static readonly Random Random = new Random();

		private readonly bool _ownsSocket;

		private Socket _socket;

		private KCP _kcp;

		private byte[] _buffer = new byte[MaxPacketSize];

		private bool _closed;

		private Timer _updateTimer;

		private NetByteBuf _receiveBuf;

		public override bool CanRead => !_closed;

		public override bool CanSeek => false;

		public override bool CanWrite => !_closed;

		public override long Length => throw new NotSupportedException();

		public override long Position
		{
			get => throw new NotSupportedException();
			set => throw new NotSupportedException();
		}

		public KcpStream(Socket socket, bool ownsSocket = false)
		{
			if (socket == null)
			{
				throw new ArgumentNullException(nameof(socket));
			}
			_socket = socket;
			_ownsSocket = ownsSocket;
			_kcp = new KCP((uint)Random.Next(1, 2147483647), PumpOut);
			_kcp.NoDelay(1, 10, 2, 1);
			_kcp.WndSize(256, 256);
			_kcp.SetMtu(1200);
			_receiveBuf = new NetByteBuf(128*1024);
			_updateTimer = new Timer(delegate
			{
				if (_closed)
				{
					return;
				}
				object kcp = _kcp;
				lock (kcp)
				{
					_kcp.Update((uint)Connection.NowMillis());
				}
			}, null, 10, 10);
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (_closed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}
			if (buffer == null)
			{
				throw new ArgumentNullException(nameof(buffer));
			}
			if (offset < 0 || offset > buffer.Length)
			{
				throw new ArgumentOutOfRangeException(nameof(offset));
			}
			if (count < 0 || count > buffer.Length - offset)
			{
				throw new ArgumentOutOfRangeException(nameof(count));
			}
			if (_socket.Available > 0)
			{
				PumpIn();
			}
			while (true)
			{
				object kcp = _kcp;
				lock (kcp)
				{
					int num = _kcp.PeekSize();
					while (num > 0 && _receiveBuf.WritableBytes() >= num)
					{
						if (_kcp.Recv(_buffer, 0, num) > 0)
						{
							_receiveBuf.WriteBytes(_buffer, 0, num);
						}
						num = _kcp.PeekSize();
					}
				}
				int num2 = _receiveBuf.ReadableBytes();
				if (num2 > 0)
				{
					if (num2 > count)
					{
						num2 = count;
					}
					_receiveBuf.ReadBytes(buffer, offset, num2);
					_receiveBuf.ResetBuffer();
				}
				else if (PumpIn() <= 0)
				{
					break;
				}
				if (num2 > 0)
				{
					return num2;
				}
			}
			return 0;
		}

		private int PumpIn()
		{
			int num;
			try
			{
				num = _socket.Receive(_buffer);
			}
			catch (Exception)
			{
				int result = 0;
				return result;
			}
			if (num <= 0)
			{
				return num;
			}
			object kcp = _kcp;
			lock (kcp)
			{
				_kcp.Input(_buffer, num);
			}
			return num;
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			if (_closed)
			{
				throw new ObjectDisposedException(GetType().FullName);
			}
			if (buffer == null)
			{
				throw new ArgumentNullException(nameof(buffer));
			}
			if (offset < 0 || offset > buffer.Length)
			{
				throw new ArgumentOutOfRangeException(nameof(offset));
			}
			if (count < 0 || count > buffer.Length - offset)
			{
				throw new ArgumentOutOfRangeException(nameof(count));
			}
			object kcp = _kcp;
			lock (kcp)
			{
				_kcp.Send(buffer, offset, count);
			}
		}

		private void PumpOut(byte[] buff, int size)
		{
			if (_closed)
			{
				return;
			}
			try
			{
				_socket.Send(buff, 0, size, SocketFlags.None);
			}
			catch (Exception)
			{
			}
		}

		public override void Close()
		{
			_closed = true;
			base.Close();
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing && _ownsSocket)
			{
				if (_socket != null)
				{
					_socket.Close();
					_socket = null;
				}
				if (_updateTimer != null)
				{
					using (AutoResetEvent autoResetEvent = new AutoResetEvent(false))
					{
						_updateTimer.Dispose(autoResetEvent);
						autoResetEvent.WaitOne();
					}
					_updateTimer = null;
				}
				_receiveBuf = null;
				_kcp = null;
				_buffer = null;
			}
			base.Dispose(disposing);
		}

		public override void Flush()
		{
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			throw new NotSupportedException();
		}

		public override void SetLength(long value)
		{
			throw new NotSupportedException();
		}
	}
}
