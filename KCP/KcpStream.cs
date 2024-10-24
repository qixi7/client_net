using System;
using System.IO;
using System.Net.Sockets;
using System.Threading;
using KcpProject;

/*
 *	该kcp库地址: https://github.com/limpo1989/kcp-csharp
 *  当前版本:
 *		SHA-1: 6ed5b68645c8afe97dbcbd71fb6f469a98f49420
 *		date:  2023年7月24日 11:10:20
 */

namespace NetModule.Kcp
{
	internal class KcpStream :Stream
	{
		private readonly bool _ownsSocket;
		
		private UDPSession _sess;
		private readonly object _lock = new object();
		
		private bool _closed;

		private Timer _updateTimer;

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
			_ownsSocket = ownsSocket;
			_sess = new UDPSession();
			_sess.AckNoDelay = true;
			_sess.WriteDelay = false;
			_sess.Connect(socket);

			_updateTimer = new Timer(delegate
			{
				if (_closed)
				{
					return;
				}
				lock (_lock)
				{
					_sess.Update();
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

			var readCount = 0;
			while (true)
			{
				var onceCount = 0;
				lock (_lock)
				{
					onceCount = _sess.Recv(buffer, offset, count - readCount);
				}
				if (onceCount < 0)
				{
					throw new IOException();
				}

				readCount += onceCount;
				
				if (readCount > 0)
				{
					break;
				}
			}
			return readCount;
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
			
			lock (_lock)
			{
				_sess.Send(buffer, offset, count);
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
				if (_sess != null)
				{
					lock (_lock)
					{
						_sess.Close();
						_sess = null;
					}
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
