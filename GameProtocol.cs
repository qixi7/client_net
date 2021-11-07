using System;

namespace Public.Net
{
	public static class GameProtocol
	{
		public static int ReadPack(ProtocolReader reader, ref GameNetPack pack)
		{
			int num = (int)reader.ReadUInt32();
			pack.Deserialize(reader, num);
			return 4 + num;			// 4 for byteSize
		}

		public static int WritePack(ref GameNetPack pack, ProtocolWriter writer)
		{
			int byteSize = pack.GetByteSize();
			writer.Write((uint)byteSize);
			pack.Serialize(writer);
			return 4 + byteSize;	// 4 for byteSize
		}
	}
}
