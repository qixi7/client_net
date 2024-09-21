using System.Threading;

namespace NetModule
{
	public class NetStatistics
	{
		private long _send;
		private long _receive;
		private long _sendPackets;
		private long _receivePackets;

		private volatile bool _enabled = true;
		public bool Enabled
		{
			get => _enabled;
			set => _enabled = value;
		}

		// get
		public long BytesSent => Interlocked.Read(ref _send);
		public long PacketsSent => Interlocked.Read(ref _sendPackets);
		public long BytesReceived => Interlocked.Read(ref _receive);
		public long PacketsReceived => Interlocked.Read(ref _receivePackets);

		public void Clear()
		{
			Interlocked.Exchange(ref _send, 0L);
			Interlocked.Exchange(ref _receive, 0L);
			Interlocked.Exchange(ref _sendPackets, 0L);
			Interlocked.Exchange(ref _receivePackets, 0L);
		}

		public void OnReceive(long size)
		{
			if (!_enabled) { return; }
			Interlocked.Add(ref _receive, size);
			Interlocked.Increment(ref _receivePackets);
		}

		public void OnSend(long size)
		{
			if (!_enabled) { return; }
			Interlocked.Add(ref _send, size);
			Interlocked.Increment(ref _sendPackets);
		}
	}
}
