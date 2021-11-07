using System;

namespace Public.Net.RDP
{
	internal interface ISendQueue
	{
		void Clear(uint seq);

		bool Get(uint offset, ref PacketLoad load);

		bool Write(PacketLoad load);

		int Ack(uint ack, uint ackBits, Window w);
	}
}
