using System;

namespace Public.Net.RDP
{
	internal interface IRecvQueue
	{
		bool Set(PacketLoad load);

		bool Read(ref PacketLoad load);

		void GetAck(out uint ack, out uint ackBits);
	}
}
