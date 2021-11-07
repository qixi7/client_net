using System;
using Public.Log;

namespace Public.Net.RDP
{
	internal class Queue : ISendQueue, IRecvQueue
	{
		private readonly object _mutex = new object();

		private readonly Packet[] _packets;

		private uint _seq;

		private readonly uint _size;

		public Queue(uint size)
		{
			_size = size;
			_packets = new Packet[size];
		}

		public bool Get(uint offset, ref PacketLoad load)
		{
			object mutex = _mutex;
			bool result;
			lock (mutex)
			{
				uint num = _seq - 1u + offset;
				Packet packet = _packets[num % _size];
				if (!packet.Known || packet.Load.Seq != num)
				{
					result = false;
				}
				else
				{
					load = packet.Load;
					result = true;
				}
			}
			return result;
		}

		public void Clear(uint seq)
		{
			object mutex = _mutex;
			lock (mutex)
			{
				ClearInternal(seq, null);
			}
		}

		private bool ClearInternal(uint seq, Window w = null)
		{
			uint num = seq % _size;
			Packet packet = _packets[num];
			if (!packet.Known || packet.Load.Seq != seq)
			{
				return false;
			}
			packet.Known = false;
			if (w != null)
			{
				w.Append(Connection.Now() - packet.Load.Timestamp);
			}
			packet.Load.Free();
			_packets[num] = packet;
			return true;
		}

		public bool Write(PacketLoad load)
		{
			object mutex = _mutex;
			lock (mutex)
			{
				uint num = load.Seq % _size;
				Packet packet = _packets[num];
				if (packet.Known)
				{
					return packet.Load.Seq == load.Seq;
				}
				packet.Load = load;
				packet.Known = true;
				_packets[num] = packet;
			}
			return true;
		}

		public int Ack(uint ack, uint ackBits, Window w)
		{
			object mutex = _mutex;
			int result = 0;
			lock (mutex)
			{
				int num = 0;
				// drop the data out of cache.
				if (ack - _seq <= _size)
				{
					// move to new ack
					while (_seq < ack)
					{
						if (ClearInternal(_seq, w))
						{
							num++;
						}
						_seq++;
					}

					uint nextAckIndex = 1;
					while (ackBits != 0)
					{
						if ((ackBits & 1) > 0 && ClearInternal(ack + nextAckIndex, w))
						{
							num++;
						}
						ackBits >>= 1;
						nextAckIndex++;
					}
					result = num;
				}
			}
			return result;
		}

		public bool Set(PacketLoad load)
		{
			object mutex = _mutex;
			bool result;
			lock (mutex)
			{
				if (load.Seq - _seq > _size)
				{
					result = false;
					RdpStream._RdpDebugLog("Set func. load.Seq={0}, _seq={1}, _size={2}",
						load.Seq, _seq, _size);
				}
				else
				{
					uint num = load.Seq % _size;
					Packet packet = _packets[(int)num];
					if (packet.Known)
					{
						result = false;
						RdpStream._RdpDebugLog("Set func. load.Seq={0}, packet.Known.", load.Seq);
					}
					else
					{
						packet.Load = load;
						packet.Known = true;
						_packets[(int)num] = packet;
						result = true;
					}
				}
			}
			return result;
		}

		public bool Read(ref PacketLoad load)
		{
			object mutex = _mutex;
			bool result;
			lock (mutex)
			{
				uint num = _seq % _size;
				Packet packet = _packets[(int)num];
				if (!packet.Known || packet.Load.Seq != _seq)
				{
					result = false;
				}
				else
				{
					packet.Known = false;
					load = packet.Load;
					packet.Load = default;
					_packets[(int)num] = packet;
					_seq += 1u;
					result = true;
				}
			}
			return result;
		}

		public void GetAck(out uint ack, out uint ackBits)
		{
			object mutex = _mutex;
			lock (mutex)
			{
				uint numOfSelectBit = 32u;
				if (_size < numOfSelectBit)
				{
					numOfSelectBit = _size;
				}
				ack = _seq;
				ackBits = 0u;
				for (uint bitIndex = 0u; bitIndex < numOfSelectBit; bitIndex++)
				{
					uint selectAckBit = _seq + 1u + bitIndex;
					uint index = selectAckBit % _size;
					Packet packet = _packets[index];
					if (packet.Known && packet.Load.Seq == selectAckBit)
					{
						ackBits |= 1u << (int)bitIndex;
					}
				}
			}
		}
	}
}
