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
				uint seq = _seq + offset;
				Packet packet = _packets[seq % _size];
				if (!packet.Known || packet.Load.Seq != seq)
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
			uint index = seq % _size;
			Packet packet = _packets[index];
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
			_packets[index] = packet;
			// RdpStream._RdpSendRecvLog("Ack Send Seq={0}", seq);
			return true;
		}

		public bool Write(PacketLoad load)
		{
			object mutex = _mutex;
			lock (mutex)
			{
				uint index = load.Seq % _size;
				Packet packet = _packets[index];
				if (packet.Known)
				{
					return packet.Load.Seq == load.Seq;
				}
				packet.Load = load;
				packet.Known = true;
				_packets[index] = packet;
			}
			return true;
		}

		public int Ack(uint ack, uint ackBits, Window w)
		{
			object mutex = _mutex;
			int ackNum = 0;
			lock (mutex)
			{
				// drop the data out of cache.
				if (ack - _seq <= _size)
				{
					// move to new ack
					while (_seq < ack)
					{
						if (ClearInternal(_seq, w))
						{
							ackNum++;
						}
						_seq++;
					}

					uint nextAckIndex = 1;
					while (ackBits != 0)
					{
						if ((ackBits & 1) > 0 && ClearInternal(ack + nextAckIndex, w))
						{
							ackNum++;
						}
						ackBits >>= 1;
						nextAckIndex++;
					}
				}
			}
			return ackNum;
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
					RdpStream._RdpQueLog("Set load err _seq={0}. load.Seq={1}, _size={2}",
						_seq, load.Seq, _size);
				}
				else
				{
					uint index = load.Seq % _size;
					Packet packet = _packets[(int)index];
					if (packet.Known)
					{
						result = false;
						RdpStream._RdpQueLog("Set load err _seq={0}. load.Seq={1}, packet.Known. packet.Seq={2}",
							_seq, load.Seq, packet.Load.Seq);
						if (load.Seq == _seq)
						{
							// 这个包更重要! 替换掉
							packet.Load = load;
							_packets[(int)index] = packet;
							result = true;
							RdpStream._RdpQueLog("Set Replace load ok _seq={0}. load.Seq={1}",
								_seq, load.Seq);
						}
					}
					else
					{
						packet.Load = load;
						packet.Known = true;
						_packets[(int)index] = packet;
						result = true;
						RdpStream._RdpQueLog("Set load ok _seq={0}. load.Seq={1}",
							_seq, load.Seq);
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
				uint index = _seq % _size;
				Packet packet = _packets[(int)index];
				if (!packet.Known || packet.Load.Seq != _seq)
				{
					result = false;
					if (packet.Load.Seq != 0)
					{
						RdpStream._RdpQueLog("Read queue err! _seq={0}, Known={1}, packet.seq={2}",
							_seq, packet.Known, packet.Load.Seq);						
					}
				}
				else
				{
					packet.Known = false;
					load = packet.Load;
					packet.Load = default;
					_packets[(int)index] = packet;
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