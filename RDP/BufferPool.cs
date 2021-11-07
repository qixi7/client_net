using System.Collections.Generic;

namespace Public.Net.RDP
{
	internal static class BufferPool
	{
		private static FastMutex _mutex = default;

		private static readonly Stack<byte[]> Pool = new Stack<byte[]>();

		public static byte[] Get()
		{
			bool flag = false;
			try
			{
				_mutex.Enter(ref flag);
				if (Pool.Count > 0)
				{
					return Pool.Pop();
				}
			}
			finally
			{
				if (flag)
				{
					_mutex.Exit();
				}
			}
			return new byte[Datagram.Mtu];
		}

		public static void Put(byte[] buf)
		{
			bool flag = false;
			try
			{
				_mutex.Enter(ref flag);
				Pool.Push(buf);
			}
			finally
			{
				if (flag)
				{
					_mutex.Exit();
				}
			}
		}
	}
}
