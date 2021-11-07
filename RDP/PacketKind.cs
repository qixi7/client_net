using System;

namespace Public.Net.RDP
{
	internal enum PacketKind : byte
	{
		Data,
		Ack,
		Dial,
		DialAck
	}
}
