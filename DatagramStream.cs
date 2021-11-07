using System;
using System.IO;
using Public.Log;

namespace Public.Net
{
	internal class DatagramStream : Stream
	{
		private readonly Stream _baseStream;

		private readonly int _maxSendSize;

		private readonly byte[] _readBuf;

		private Slice<byte> _readSlice;

		public override bool CanRead => _baseStream.CanRead;

		public override bool CanSeek => _baseStream.CanSeek;

		public override bool CanWrite => _baseStream.CanWrite;

		public override long Length => _baseStream.Length;

		public override long Position
		{
			get => _baseStream.Position;
			set => _baseStream.Position = value;
		}

		public DatagramStream(Stream baseStream, int maxSendSize, int maxReceiveSize)
		{
			_baseStream = baseStream;
			_maxSendSize = maxSendSize;
			_readBuf = new byte[maxReceiveSize];
			_readSlice = Slice<byte>.Make(_readBuf, 0, 0);
		}

		public override int Read(byte[] buffer, int offset, int count)
		{
			if (count <= 0)
			{
				return 0;
			}
			Slice<byte> b = Slice<byte>.Make(buffer, offset, offset + count);
			if (_readSlice.Length == 0)
			{
				int to = _baseStream.Read(_readBuf, 0, _readBuf.Length);
				_readSlice = Slice<byte>.Make(_readBuf, 0, to);
			}
			int from = _readSlice.CopyTo(b);
			b = b.Cut(from);
			_readSlice = _readSlice.Cut(from);
			return count - b.Length;
		}

		public override void Write(byte[] buffer, int offset, int count)
		{
			while (count > _maxSendSize)
			{
				_baseStream.Write(buffer, offset, _maxSendSize);
				offset += _maxSendSize;
				count -= _maxSendSize;
			}
			if (count > 0)
			{
				_baseStream.Write(buffer, offset, count);
			}
		}

		public override void Flush()
		{
			_baseStream.Flush();
		}

		public override void Close()
		{
			_baseStream.Close();
		}

		public override long Seek(long offset, SeekOrigin origin)
		{
			return _baseStream.Seek(offset, origin);
		}

		public override void SetLength(long value)
		{
			_baseStream.SetLength(value);
		}

		protected override void Dispose(bool disposing)
		{
			if (disposing)
			{
				_baseStream.Dispose();
			}
			base.Dispose(disposing);
		}
	}
}
