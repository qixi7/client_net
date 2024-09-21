using System;
using System.Collections.Generic;

namespace NetModule.RDP
{
	public static class Crc32
	{
		// Predefined polynomials.
		// IEEE is by far and away the most common CRC-32 polynomial.
		// Used by ethernet (IEEE 802.3), v.42, fddi, gzip, zip, png, ...
		private const uint Polyrev = 3988292384u;	// 0xedb88320 IEEE标准

		private const int TableLen = 256;
		private static readonly uint[] Table = new uint[TableLen];

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
			uint crcHash = ~prevCrc;
			foreach (byte current in data)
			{
				crcHash = crcHash >> 8 ^ Table[(byte)crcHash ^ current];
			}
			return ~crcHash;
		}

		private static void Initialize()
		{
			if (_initialized)
			{
				return;
			}
			for (uint i = 0u; i < TableLen; i += 1u)
			{
				uint tmpHash = i;
				for (int j = 0; j < 8; j++)
				{
					if ((tmpHash & 1u) != 0u)
					{
						tmpHash = tmpHash >> 1 ^ Polyrev;
					}
					else
					{
						tmpHash >>= 1;
					}
				}
				Table[(int)(UIntPtr)i] = tmpHash;
			}
			_initialized = true;
		}
	}
}