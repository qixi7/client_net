namespace NetModule
{
	public static class BigEndian
	{
		public static void PutBytes(Slice<byte> bits, ulong data)
		{
			bits.Set(7, (byte)data);
			bits.Set(6, (byte)(data >> 8));
			bits.Set(5, (byte)(data >> 16));
			bits.Set(4, (byte)(data >> 24));
			bits.Set(3, (byte)(data >> 32));
			bits.Set(2, (byte)(data >> 40));
			bits.Set(1, (byte)(data >> 48));
			bits.Set(0, (byte)(data >> 56));
		}

		public static ulong ToUInt64(Slice<byte> bits)
		{
			return (ulong)bits.Get(7) |
			       (ulong)bits.Get(6) << 8 |
			       (ulong)bits.Get(5) << 16 |
			       (ulong)bits.Get(4) << 24 |
			       (ulong)bits.Get(3) << 32 |
			       (ulong)bits.Get(2) << 40 |
			       (ulong)bits.Get(1) << 48 |
			       (ulong)bits.Get(0) << 56;
		}

		public static void PutBytes(Slice<byte> bits, uint data)
		{
			bits.Set(3, (byte)data);
			bits.Set(2, (byte)(data >> 8));
			bits.Set(1, (byte)(data >> 16));
			bits.Set(0, (byte)(data >> 24));
		}

		public static void PutBytes(byte[] bits, uint data, int begin)
		{
			bits[begin+3] =  (byte)data;
			bits[begin+2] =  (byte)(data >> 8);
			bits[begin+1] =  (byte)(data >> 16);
			bits[begin] =  (byte)(data >> 24);
		}
		
		public static uint ToUInt32(Slice<byte> bits)
		{
			return (uint)(bits.Get(3) |
			              bits.Get(2) << 8 |
			              bits.Get(1) << 16 |
			              bits.Get(0) << 24);
		}
		
		public static uint ToUInt32(byte[] bits, int begin)
		{
			return (uint)(bits[begin+3] |
			              bits[begin+2] << 8 |
			              bits[begin+1] << 16 |
			              bits[begin+0] << 24);
		}

		public static void PutBytes(byte[] bits, ushort data, int begin)
		{
			bits[begin+1] =  (byte)data;
			bits[begin+0] =  (byte)(data >> 8);
		}

		public static void PutBytes(Slice<byte> bits, ushort data)
		{
			bits.Set(1, (byte)data);
			bits.Set(0, (byte)(data >> 8));
		}

		public static ushort ToUInt16(Slice<byte> bits)
		{
			return (ushort)(bits.Get(1) |
			                bits.Get(0) << 8);
		}
		
		public static ushort ToUInt16(byte[] bits, int begin)
		{
			return (ushort)(bits[begin+1] |
			                bits[begin+0] << 8);
		}
	}
}
