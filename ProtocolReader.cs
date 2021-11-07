

namespace Public.Net
{
	public class ProtocolReader : System.IDisposable
	{
		private readonly System.IO.Stream _stream;

		private readonly bool _leaveOpen;

		private readonly byte[] _buf;

		private readonly Slice<byte> _bufSlice;

		public ProtocolReader(System.IO.Stream baseStream, bool leaveOpen = false)
		{
			_stream = baseStream;
			_leaveOpen = leaveOpen;
			_buf = new byte[8];
			_bufSlice = Slice<byte>.Make(_buf);
		}

		public void Read(byte[] buffer, int offset, int count)
		{
			for (int i = 0; i < count; i += _stream.Read(buffer, offset + i, count - i))
			{
			}
		}

		public byte ReadByte()
		{
			Read(_buf, 0, 1);
			return _buf[0];
		}

		public ushort ReadUInt16()
		{
			Read(_buf, 0, 2);
			return BigEndian.ToUInt16(_bufSlice);
		}

		public uint ReadUInt32()
		{
			Read(_buf, 0, 4);
			return BigEndian.ToUInt32(_bufSlice);
		}

		public void Dispose()
		{
			if (_stream != null && !_leaveOpen)
			{
				_stream.Dispose();
			}
		}
	}
}
