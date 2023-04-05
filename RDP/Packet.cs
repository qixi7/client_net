namespace Public.Net.RDP
{
    internal enum PacketKind : byte
    {
        Data,
        Ack,
        Dial,
        DialAck
    }
	
    internal struct Packet
    {
        public bool Known;

        public PacketLoad Load;
    }
	
    internal struct PacketLoad
    {
        public const int Overhead = 7;	// 4 for seq, 2 for bodySize, 1 for subPacketTag

        public uint Seq;		// seq
        public int Size;		// body size
        public bool SubPacket;	// 是否子包. 子包=1, 终包=0
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
            data.Set(6, 0);
            if (SubPacket)
            {
                data.Set(6, 1);
            }
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
            SubPacket = data.Get(6) > 0;
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