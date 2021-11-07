using System;

namespace Public.Net
{
	public struct GameNetPack
	{
		/*
		 * 	网络协议包. 协议序：
		 *  <GamePackSize>  | 					<GamePack>					|
		 *  	4 			|	 4(connID) + 4(seq) + 2(msgID) + 1(flag)	|
		 */
		private const int Overhead = 11;

		public uint connID;
		public uint seq;
		public ushort msgID;
		public byte flag;

		public byte[] body;

		public int GetByteSize()
		{
			if (body == null)
			{
				return Overhead;
			}
			return body.Length + Overhead;
		}

		public void Serialize(ProtocolWriter writer)
		{
			writer.Write(connID);
			writer.Write(seq);
			writer.Write(msgID);
			writer.Write(flag);
			writer.Write(body, 0, body.Length);
		}

		public void Deserialize(ProtocolReader reader, int packLen)
		{
			connID = reader.ReadUInt32();
			seq = reader.ReadUInt32();
			msgID = reader.ReadUInt16();
			flag = reader.ReadByte();
			int num = packLen - Overhead;
			body = new byte[num];
			reader.Read(body, 0, num);
		}
	}
}
