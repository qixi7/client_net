namespace Public.Net.RDP
{
	internal struct PacketHeader
	{
		public PacketKind Kind;

		public uint Ack;

		public uint AckBits;

		public int ReadFrom(Slice<byte> data)
		{
			// 第1位=协议ID, 第2位=包类型
			if (data.Length < 2)
			{
				return 0;
			}
			if (data.Get(0) != Datagram.ProtocolID)
			{
				return 0;
			}
			Kind = (PacketKind)data.Get(1);
			int size = 2;
			if (Kind == PacketKind.Ack)
			{
				// ack包: 3-6位=Ack, 7-10位=selectAck
				if (data.Length < 10)
				{
					return 0;
				}
				Ack = BigEndian.ToUInt32(data.Cut(2, 6));
				AckBits = BigEndian.ToUInt32(data.Cut(6, 10));
				size += 8;
			}
			return size;
		}

		public int WriteTo(Slice<byte> data)
		{
			if (data.Length < 2)
			{
				return 0;
			}
			data.Set(0, Datagram.ProtocolID);
			data.Set(1, (byte)Kind);
			int size = 2;
			PacketKind kind = Kind;
			if (kind == PacketKind.Ack)
			{
				if (data.Length < 10)
				{
					return 0;
				}
				BigEndian.PutBytes(data.Cut(2, 6), Ack);
				BigEndian.PutBytes(data.Cut(6, 10), AckBits);
				size += 8;
			}
			return size;
		}
	}
}