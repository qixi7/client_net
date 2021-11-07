using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Public.Log;

namespace Public.Net
{
	internal static class Pools
	{
		private const int MinIndexSize = 5;

		private const int MinBufferSize = 1 << MinIndexSize;	// 1<<MinIndexSize

		private const int MaxBufferSize = 4 * 1024;		// 4KB. 1<<12

		private const int MaxSizePerPool = 64 * 1024;	// 64KB

		private static readonly IList<Pool> PoolsList;

		static Pools()
		{
			List<Pool> list = new List<Pool>();
			int poolIndex = GetPoolIndex(MaxBufferSize);
			for (int i = 0; i <= poolIndex; i++)
			{
				int poolSize = GetPoolSize(i);
				list.Add(new Pool(poolSize, MaxSizePerPool / poolSize));
				LogHelper.DebugF("poolIndex={0}, size={1}", i, poolSize);
			}
			PoolsList = new ReadOnlyCollection<Pool>(list);
		}

		public static int GetPoolSize(int index)
		{
			return 1 << index + MinIndexSize;
		}

		public static int GetPoolIndex(int size)
		{
			if (size <= MinBufferSize)
			{
				return 0;
			}
			// 4841369599423283200uL == 1075 << 52
			// 4503599627370496 == 1 << 52
			double num = BitConverter.Int64BitsToDouble((long)(4841369599423283200uL | (ulong)(size - 1)));
			LogHelper.DebugF("nu1={0}", num);
			long num2 = BitConverter.DoubleToInt64Bits(num - 4503599627370496.0);
			LogHelper.DebugF("nu2={0}", num2);
//			return (int)((num2 >> 52) - 1027L);
			return (int)((num2 >> 52) - 1027L);
		}

		public static byte[] Get(int size)
		{
			if (size < 0)
			{
				throw new ArgumentOutOfRangeException(nameof(size));
			}
			// todo.考虑超过MaxBufferSize太频繁打印warning。
			return size <= MaxBufferSize ? PoolsList[GetPoolIndex(size)].Get() : new byte[size];
		}

		public static void Put(byte[] buf)
		{
			if (buf.Length > MaxBufferSize || buf.Length < MinBufferSize)
			{
				return;
			}
			int poolIndex = GetPoolIndex(buf.Length);
			if (PoolsList.Count <= poolIndex)
			{
				return;
			}
			int poolSize = GetPoolSize(poolIndex);
			if (poolSize != buf.Length)
			{
				return;
			}
			PoolsList[poolIndex].Put(buf);
		}

		public static void Trim()
		{
			foreach (var current in PoolsList)
			{
				current.Trim(0);
			}
		}
	}
}
