using System;
using System.Threading;

namespace Public.Net
{
	public static class NetStatistics
	{
		private static long _send;

		private static long _receive;

		private static long _sendPackets;

		private static long _receivePackets;

		private static volatile bool _enabled = true;

		public static bool Enabled
		{
			get => _enabled;
			set => _enabled = value;
		}

		public static long BytesSent => Interlocked.Read(ref _send);

		public static long PacketsSent => Interlocked.Read(ref _sendPackets);

		public static long BytesReceived => Interlocked.Read(ref _receive);

		public static long PacketsReceived => Interlocked.Read(ref _receivePackets);

		public static void Clear()
		{
			Interlocked.Exchange(ref _send, 0L);
			Interlocked.Exchange(ref _receive, 0L);
			Interlocked.Exchange(ref _sendPackets, 0L);
			Interlocked.Exchange(ref _receivePackets, 0L);
		}

		public static void OnReceive(long size)
		{
			if (!_enabled)
			{
				return;
			}
			Interlocked.Add(ref _receive, size);
			Interlocked.Increment(ref _receivePackets);
		}

		public static void OnSend(long size)
		{
			if (!_enabled)
			{
				return;
			}
			Interlocked.Add(ref _send, size);
			Interlocked.Increment(ref _sendPackets);
		}
	}
}
