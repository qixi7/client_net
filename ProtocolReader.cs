namespace NetModule
{
	public class ProtocolReader : System.IDisposable
	{
		private readonly System.IO.Stream _stream;
		private readonly bool _leaveOpen;
		private bool DataMode;	// 是否UDP数据包模式

		public ProtocolReader(System.IO.Stream baseStream, bool dataMode, bool leaveOpen = false)
		{
			_stream = baseStream;
			_leaveOpen = leaveOpen;
			DataMode = dataMode;
		}
		
		public int Read(byte[] buffer, int offset, int count)
		{
			int bodySize;
			if (IsDataMode())
			{
				bodySize = _stream.Read(buffer, offset, count);
			}
			else
			{
				for (int i = 0; i < count; i += _stream.Read(buffer, offset + i, count - i)) { }
				bodySize = count;
			}
		
			return bodySize;
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
