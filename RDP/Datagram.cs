using System;

namespace Public.Net.RDP
{
	internal static class Datagram
	{
		public const byte ProtocolID = 67;

		public const int Mtu = 1200;

		public const int Overhead = 4;	// 4字节校验和

		private const uint Magic = 1511108092u;

		public static int Open(byte[] packet, int size)
		{
			if (size < 6)
			{
				return 0;
			}
			if (packet[0] != ProtocolID)
			{
				return 0;
			}
			Slice<byte> slice = Slice<byte>.Make(packet, 0, size);
			uint num = BigEndian.ToUInt32(slice.Cut(size - Overhead));
			uint num2 = Crc32.Hash(slice.Cut(0, -4), 0u) ^ Magic;
			if (num != num2)
			{
				return 0;
			}
			return size - Overhead;
		}

		public static bool Seal(byte[] packet, int size)
		{
			if (packet.Length < 1)
			{
				return false;
			}
			BigEndian.PutBytes(Slice<byte>.Make(packet, size, size + Overhead),
				Crc32.Hash(Slice<byte>.Make(packet, 0, size), 0u) ^ Magic);
			return true;
		}

		public static bool SeqSign(uint seq)
		{
			return seq >> 31 != 0u;
		}
	}
}
