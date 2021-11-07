using System;
using System.Collections.Generic;
using System.Diagnostics;
using Public.Log;

namespace Public.Net.RDP
{
	internal struct PacketLoad
	{
		public const int Overhead = 6;

		public uint Seq;

		public int Size;

		public Slice<byte> Buffer;

		public long Timestamp;

		public int WriteTo(Slice<byte> data)
		{
			if (data.Length < Overhead + Size)
			{
				return 0;
			}
			BigEndian.PutBytes(data.Cut(0, 4), Seq);
			BigEndian.PutBytes(data.Cut(4, 6), (ushort)Size);
			return Overhead + Buffer.CopyTo(data.Cut(Overhead));
		}

		public int ReadFrom(Slice<byte> data)
		{
			if (data.Length < Overhead)
			{
				return 0;
			}
			Seq = BigEndian.ToUInt32(data.Cut(0, 4));
			Size = BigEndian.ToUInt16(data.Cut(4, 6));
			if (data.Length - Overhead < Size || Buffer.Length < Size)
			{
				return 0;
			}

			Buffer = Buffer.Cut(0, Size);
			data.Cut(Overhead).CopyTo(Buffer);
			RdpStream._RdpDebugLog("ReadFrom size={0}, buffer.len={1}, Seq={2}",
				Size, Buffer.Length, Size);
			return Overhead + Size;
		}

		public void Free()
		{
			BufferPool.Put(Buffer.BaseArray);
			Buffer = default;
		}
	}
}
