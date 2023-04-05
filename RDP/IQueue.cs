namespace Public.Net.RDP
{
	internal interface IRecvQueue
	{
		bool Set(PacketLoad load);

		bool Read(ref PacketLoad load);

		void GetAck(out uint ack, out uint ackBits);
	}
	
	internal interface ISendQueue
	{
		void Clear(uint seq);

		bool Get(uint offset, ref PacketLoad load);

		bool Write(PacketLoad load);

		int Ack(uint ack, uint ackBits, Window w);
	}
}