using System;

namespace Public.Net.RDP
{
	internal struct PacketHeader
	{
		public PacketKind Kind;

		public uint Ack;

		public uint AckBits;

		public int ReadFrom(Slice<byte> data)
		{
			if (data.Length < 2)
			{
				return 0;
			}
			if (data.Get(0) != Datagram.ProtocolID)
			{
				return 0;
			}
			Kind = (PacketKind)data.Get(1);
			int num = 2;
			if (Kind == PacketKind.Ack)
			{
				if (data.Length < 10)
				{
					return 0;
				}
				Ack = BigEndian.ToUInt32(data.Cut(2, 6));
				AckBits = BigEndian.ToUInt32(data.Cut(6, 10));
				num += 8;
			}
			return num;
		}

		public int WriteTo(Slice<byte> data)
		{
			if (data.Length < 2)
			{
				return 0;
			}
			data.Set(0, Datagram.ProtocolID);
			data.Set(1, (byte)Kind);
			int num = 2;
			PacketKind kind = Kind;
			if (kind == PacketKind.Ack)
			{
				if (data.Length < 10)
				{
					return 0;
				}
				BigEndian.PutBytes(data.Cut(2, 6), Ack);
				BigEndian.PutBytes(data.Cut(6, 10), AckBits);
				num += 8;
			}
			return num;
		}
	}
}
