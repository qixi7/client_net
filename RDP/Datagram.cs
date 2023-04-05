namespace Public.Net.RDP
{
    internal static class Datagram
    {
        public const byte ProtocolID = 67;

        public const int Mtu = 1200;
		
        public const int CheckSumSize = 4;	// 4字节校验和

        private const uint Magic = 1511108092u;

        public static int Open(byte[] packet, int size)
        {
            if (size < CheckSumSize)
            {
                return 0;
            }
            if (packet[0] != ProtocolID)
            {
                return 0;
            }
            Slice<byte> slice = Slice<byte>.Make(packet, 0, size);
            uint checkSum = BigEndian.ToUInt32(slice.Cut(size - CheckSumSize));
            uint crc32 = Crc32.Hash(slice.Cut(0, -4), 0u) ^ Magic;
            if (checkSum != crc32)
            {
                return 0;
            }
            return size - CheckSumSize;
        }

        public static bool Seal(byte[] packet, int size)
        {
            if (packet.Length < 1)
            {
                return false;
            }
            BigEndian.PutBytes(Slice<byte>.Make(packet, size, size + CheckSumSize),
                Crc32.Hash(Slice<byte>.Make(packet, 0, size), 0u) ^ Magic);
            return true;
        }

        // seq1 after seq2
        public static bool After(uint seq1, uint seq2)
        {
            return (seq2 - seq1) >> 31 != 0u;
        }
    }
}