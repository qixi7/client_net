using System;
using System.IO;

namespace NetModule
{
	public class ProtocolWriter : IDisposable
	{
		private readonly Stream _stream;
		private readonly bool _leaveOpen;
		private readonly byte[] _buf;
		private readonly Slice<byte> _bufSlice;
		private bool DataMode;	// 是否UDP数据包模式

		public ProtocolWriter(Stream baseStream, bool dataMode, bool leaveOpen = false)
		{
			_stream = new BufferedStream(baseStream, 65536);
			_leaveOpen = leaveOpen;
			_buf = new byte[8];
			_bufSlice = Slice<byte>.Make(_buf);
			DataMode = dataMode;
		}

		public void Write(byte[] buffer, int offset, int count)
		{
			_stream.Write(buffer, offset, count);
		}

		// public void Write(byte value)
		// {
		// 	_buf[0] = value;
		// 	_stream.Write(_buf, 0, 1);
		// }
		//
		// public void Write(ushort value)
		// {
		// 	BigEndian.PutBytes(_bufSlice, value);
		// 	_stream.Write(_buf, 0, 2);
		// }
		//
		public void Write(uint value)
		{
			BigEndian.PutBytes(_bufSlice, value);
			_stream.Write(_buf, 0, 4);
		}

		public void Flush()
		{
			_stream.Flush();
		}

		public void Dispose()
		{
			if (_stream != null && !_leaveOpen)
			{
				_stream.Dispose();
			}
		}
		
		public bool IsDataMode()
		{
			return DataMode;
		}
	}
}
