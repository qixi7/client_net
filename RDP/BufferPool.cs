using System;
using System.Collections.Generic;
using NetModule.Log;

namespace NetModule.RDP
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
			catch (Exception e)
			{
				LogHelper.ErrorF("BufferPool Get err={0}", e);
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
			catch (Exception e)
			{
				LogHelper.ErrorF("BufferPool Put err={0}", e);
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