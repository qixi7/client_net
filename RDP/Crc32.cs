using System;
using System.Collections.Generic;

namespace Public.Net.RDP
{
	public static class Crc32
	{
		// Predefined polynomials.
		// IEEE is by far and away the most common CRC-32 polynomial.
		// Used by ethernet (IEEE 802.3), v.42, fddi, gzip, zip, png, ...
		private const uint Polyrev = 3988292384u;

		private static readonly uint[] Table = new uint[256];

		private static bool _initialized;

		private static readonly object Mutex = new object();

		public static uint Hash(IEnumerable<byte> data, uint prevCrc = 0u)
		{
			if (!_initialized)
			{
				object mutex = Mutex;
				lock (mutex)
				{
					Initialize();
				}
			}
			uint num = ~prevCrc;
			foreach (byte current in data)
			{
				num = num >> 8 ^ Table[(byte)num ^ current];
			}
			return ~num;
		}

		private static void Initialize()
		{
			if (_initialized)
			{
				return;
			}
			for (uint num = 0u; num < 256u; num += 1u)
			{
				uint num2 = num;
				for (int i = 0; i < 8; i++)
				{
					if ((num2 & 1u) != 0u)
					{
						num2 = num2 >> 1 ^ Polyrev;
					}
					else
					{
						num2 >>= 1;
					}
				}
				Table[(int)(UIntPtr)num] = num2;
			}
			_initialized = true;
		}
	}
}
