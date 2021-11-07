using System;

namespace Public.Net.KCP
{
	public class KCP
	{
		internal class Segment
		{
			internal uint conv;

			internal uint cmd;

			internal uint frg;

			internal uint wnd;

			internal uint ts;

			internal uint sn;

			internal uint una;

			internal uint resendts;

			internal uint rto;

			internal uint fastack;

			internal uint xmit;

			internal byte[] data;

			internal Segment(int size)
			{
				data = new byte[size];
			}

			internal int encode(byte[] ptr, int offset)
			{
				int num = offset;
				offset += ikcp_encode32u(ptr, offset, conv);
				offset += ikcp_encode8u(ptr, offset, (byte)cmd);
				offset += ikcp_encode8u(ptr, offset, (byte)frg);
				offset += ikcp_encode16u(ptr, offset, (ushort)wnd);
				offset += ikcp_encode32u(ptr, offset, ts);
				offset += ikcp_encode32u(ptr, offset, sn);
				offset += ikcp_encode32u(ptr, offset, una);
				offset += ikcp_encode32u(ptr, offset, (uint)data.Length);
				return offset - num;
			}
		}

		public const int IKCP_RTO_NDL = 30;

		public const int IKCP_RTO_MIN = 100;

		public const int IKCP_RTO_DEF = 200;

		public const int IKCP_RTO_MAX = 60000;

		public const int IKCP_CMD_PUSH = 81;

		public const int IKCP_CMD_ACK = 82;

		public const int IKCP_CMD_WASK = 83;

		public const int IKCP_CMD_WINS = 84;

		public const int IKCP_ASK_SEND = 1;

		public const int IKCP_ASK_TELL = 2;

		public const int IKCP_WND_SND = 32;

		public const int IKCP_WND_RCV = 32;

		public const int IKCP_MTU_DEF = 1400;

		public const int IKCP_ACK_FAST = 3;

		public const int IKCP_INTERVAL = 100;

		public const int IKCP_OVERHEAD = 24;

		public const int IKCP_DEADLINK = 10;

		public const int IKCP_THRESH_INIT = 2;

		public const int IKCP_THRESH_MIN = 2;

		public const int IKCP_PROBE_INIT = 7000;

		public const int IKCP_PROBE_LIMIT = 120000;

		private uint conv;

		private uint mtu;

		private uint mss;

		private uint snd_una;

		private uint snd_nxt;

		private uint rcv_nxt;

		private uint ts_recent;

		private uint ts_lastack;

		private uint ssthresh;

		private uint rx_rttval;

		private uint rx_srtt;

		private uint rx_rto;

		private uint rx_minrto;

		private uint snd_wnd;

		private uint rcv_wnd;

		private uint rmt_wnd;

		private uint cwnd;

		private uint probe;

		private uint current;

		private uint interval;

		private uint ts_flush;

		private uint xmit;

		private uint nodelay;

		private uint updated;

		private uint ts_probe;

		private uint probe_wait;

		private uint incr;

		private Segment[] snd_queue = new Segment[0];

		private Segment[] rcv_queue = new Segment[0];

		private Segment[] snd_buf = new Segment[0];

		private Segment[] rcv_buf = new Segment[0];

		private uint[] acklist = new uint[0];

		private byte[] buffer;

		private int fastresend;

		private int nocwnd;

		private int logmask;

		private Action<byte[], int> output;

		public KCP(uint conv_, Action<byte[], int> output_)
		{
			conv = conv_;
			snd_wnd = 32u;
			rcv_wnd = 32u;
			rmt_wnd = 32u;
			mtu = 1400u;
			mss = mtu - 24u;
			rx_rto = 200u;
			rx_minrto = 100u;
			interval = 100u;
			ts_flush = 100u;
			ssthresh = 2u;
			buffer = new byte[(mtu + 24u) * 3u];
			output = output_;
		}

		public static int ikcp_encode8u(byte[] p, int offset, byte c)
		{
			p[offset] = c;
			return 1;
		}

		public static int ikcp_decode8u(byte[] p, int offset, ref byte c)
		{
			c = p[offset];
			return 1;
		}

		public static int ikcp_encode16u(byte[] p, int offset, ushort w)
		{
			p[offset] = (byte)(w >> 0);
			p[1 + offset] = (byte)(w >> 8);
			return 2;
		}

		public static int ikcp_decode16u(byte[] p, int offset, ref ushort c)
		{
			ushort num = 0;
			num |= p[offset];
			num |= (ushort)(p[1 + offset] << 8);
			c = num;
			return 2;
		}

		public static int ikcp_encode32u(byte[] p, int offset, uint l)
		{
			p[offset] = (byte)(l >> 0);
			p[1 + offset] = (byte)(l >> 8);
			p[2 + offset] = (byte)(l >> 16);
			p[3 + offset] = (byte)(l >> 24);
			return 4;
		}

		public static int ikcp_decode32u(byte[] p, int offset, ref uint c)
		{
			uint num = 0u;
			num |= p[offset];
			num |= (uint)p[1 + offset] << 8;
			num |= (uint)p[2 + offset] << 16;
			num |= (uint)p[3 + offset] << 24;
			c = num;
			return 4;
		}

		public static byte[] slice(byte[] p, int start, int stop)
		{
			byte[] array = new byte[stop - start];
			Array.Copy(p, start, array, 0, array.Length);
			return array;
		}

		public static T[] slice<T>(T[] p, int start, int stop)
		{
			T[] array = new T[stop - start];
			int num = 0;
			for (int i = start; i < stop; i++)
			{
				array[num] = p[i];
				num++;
			}
			return array;
		}

		public static byte[] append(byte[] p, byte c)
		{
			byte[] array = new byte[p.Length + 1];
			Array.Copy(p, array, p.Length);
			array[p.Length] = c;
			return array;
		}

		public static T[] append<T>(T[] p, T c)
		{
			T[] array = new T[p.Length + 1];
			for (int i = 0; i < p.Length; i++)
			{
				array[i] = p[i];
			}
			array[p.Length] = c;
			return array;
		}

		public static T[] append<T>(T[] p, T[] cs)
		{
			T[] array = new T[p.Length + cs.Length];
			for (int i = 0; i < p.Length; i++)
			{
				array[i] = p[i];
			}
			for (int j = 0; j < cs.Length; j++)
			{
				array[p.Length + j] = cs[j];
			}
			return array;
		}

		private static uint _imin_(uint a, uint b)
		{
			return (a > b) ? b : a;
		}

		private static uint _imax_(uint a, uint b)
		{
			return (a < b) ? b : a;
		}

		private static uint _ibound_(uint lower, uint middle, uint upper)
		{
			return _imin_(_imax_(lower, middle), upper);
		}

		private static int _itimediff(uint later, uint earlier)
		{
			return (int)(later - earlier);
		}

		public int PeekSize()
		{
			if (rcv_queue.Length == 0)
			{
				return -1;
			}
			Segment segment = rcv_queue[0];
			if (segment.frg == 0u)
			{
				return segment.data.Length;
			}
			if ((long)rcv_queue.Length < (long)((ulong)(segment.frg + 1u)))
			{
				return -1;
			}
			int num = 0;
			Segment[] array = rcv_queue;
			for (int i = 0; i < array.Length; i++)
			{
				Segment segment2 = array[i];
				num += segment2.data.Length;
				if (segment2.frg == 0u)
				{
					break;
				}
			}
			return num;
		}

		public int Recv(byte[] buffer, int offset, int len)
		{
			if (rcv_queue.Length == 0)
			{
				return -1;
			}
			int num = PeekSize();
			if (0 > num)
			{
				return -2;
			}
			if (num > len)
			{
				return -3;
			}
			bool flag = rcv_queue.Length >= rcv_wnd;
			int num2 = 0;
			int num3 = offset;
			Segment[] array = rcv_queue;
			for (int i = 0; i < array.Length; i++)
			{
				Segment segment = array[i];
				Array.Copy(segment.data, 0, buffer, num3, segment.data.Length);
				num3 += segment.data.Length;
				num2++;
				if (segment.frg == 0u)
				{
					break;
				}
			}
			if (0 < num2)
			{
				rcv_queue = slice<Segment>(rcv_queue, num2, rcv_queue.Length);
			}
			num2 = 0;
			Segment[] array2 = rcv_buf;
			for (int j = 0; j < array2.Length; j++)
			{
				Segment segment2 = array2[j];
				if (segment2.sn != rcv_nxt || (long)rcv_queue.Length >= (long)((ulong)rcv_wnd))
				{
					break;
				}
				rcv_queue = append<Segment>(rcv_queue, segment2);
				rcv_nxt += 1u;
				num2++;
			}
			if (0 < num2)
			{
				rcv_buf = slice<Segment>(rcv_buf, num2, rcv_buf.Length);
			}
			if ((long)rcv_queue.Length < (long)((ulong)rcv_wnd) && flag)
			{
				probe |= 2u;
			}
			return num3 - offset;
		}

		public int Send(byte[] buffer, int offset, int len)
		{
			if (len == 0)
			{
				return -1;
			}
			int num;
			if ((long)len < (long)((ulong)mss))
			{
				num = 1;
			}
			else
			{
				num = (int)((long)len + (long)((ulong)mss) - 1L) / (int)mss;
			}
			if (255 < num)
			{
				return -2;
			}
			if (num == 0)
			{
				num = 1;
			}
			for (int i = 0; i < num; i++)
			{
				int num2;
				if ((long)(len - offset) > (long)((ulong)mss))
				{
					num2 = (int)mss;
				}
				else
				{
					num2 = len - offset;
				}
				Segment segment = new Segment(num2);
				Array.Copy(buffer, offset, segment.data, 0, num2);
				offset += num2;
				segment.frg = (uint)(num - i - 1);
				snd_queue = append<Segment>(snd_queue, segment);
			}
			if (nodelay != 0u)
			{
				flush();
			}
			return 0;
		}

		private void update_ack(int rtt)
		{
			if (rx_srtt == 0u)
			{
				rx_srtt = (uint)rtt;
				rx_rttval = (uint)(rtt / 2);
			}
			else
			{
				int num = rtt - (int)rx_srtt;
				if (0 > num)
				{
					num = -num;
				}
				rx_rttval = (3u * rx_rttval + (uint)num) / 4u;
				rx_srtt = (uint)(((ulong)(7u * rx_srtt) + (ulong)((long)rtt)) / 8uL);
				if (rx_srtt < 1u)
				{
					rx_srtt = 1u;
				}
			}
			int middle = (int)(rx_srtt + _imax_(1u, 4u * rx_rttval));
			rx_rto = _ibound_(rx_minrto, (uint)middle, 60000u);
		}

		private void shrink_buf()
		{
			if (snd_buf.Length > 0)
			{
				snd_una = snd_buf[0].sn;
			}
			else
			{
				snd_una = snd_nxt;
			}
		}

		private void parse_ack(uint sn)
		{
			if (_itimediff(sn, snd_una) < 0 || _itimediff(sn, snd_nxt) >= 0)
			{
				return;
			}
			int num = 0;
			Segment[] array = snd_buf;
			for (int i = 0; i < array.Length; i++)
			{
				Segment segment = array[i];
				if (sn == segment.sn)
				{
					snd_buf = append<Segment>(slice<Segment>(snd_buf, 0, num), slice<Segment>(snd_buf, num + 1, snd_buf.Length));
					break;
				}
				segment.fastack += 1u;
				num++;
			}
		}

		private void parse_una(uint una)
		{
			int num = 0;
			Segment[] array = snd_buf;
			for (int i = 0; i < array.Length; i++)
			{
				Segment segment = array[i];
				if (_itimediff(una, segment.sn) <= 0)
				{
					break;
				}
				num++;
			}
			if (0 < num)
			{
				snd_buf = slice<Segment>(snd_buf, num, snd_buf.Length);
			}
		}

		private void ack_push(uint sn, uint ts)
		{
			acklist = append<uint>(acklist, new uint[]
			{
				sn,
				ts
			});
		}

		private void ack_get(int p, ref uint sn, ref uint ts)
		{
			sn = acklist[p * 2];
			ts = acklist[p * 2 + 1];
		}

		private void parse_data(Segment newseg)
		{
			uint sn = newseg.sn;
			if (_itimediff(sn, rcv_nxt + rcv_wnd) >= 0 || _itimediff(sn, rcv_nxt) < 0)
			{
				return;
			}
			int num = rcv_buf.Length - 1;
			int num2 = -1;
			bool flag = false;
			for (int i = num; i >= 0; i--)
			{
				Segment segment = rcv_buf[i];
				if (segment.sn == sn)
				{
					flag = true;
					break;
				}
				if (_itimediff(sn, segment.sn) > 0)
				{
					num2 = i;
					break;
				}
			}
			if (!flag)
			{
				if (num2 == -1)
				{
					rcv_buf = append<Segment>(new Segment[]
					{
						newseg
					}, rcv_buf);
				}
				else
				{
					rcv_buf = append<Segment>(slice<Segment>(rcv_buf, 0, num2 + 1), append<Segment>(new Segment[]
					{
						newseg
					}, slice<Segment>(rcv_buf, num2 + 1, rcv_buf.Length)));
				}
			}
			int num3 = 0;
			Segment[] array = rcv_buf;
			for (int j = 0; j < array.Length; j++)
			{
				Segment segment2 = array[j];
				if (segment2.sn != rcv_nxt || (long)rcv_queue.Length >= (long)((ulong)rcv_wnd))
				{
					break;
				}
				rcv_queue = append<Segment>(rcv_queue, segment2);
				rcv_nxt += 1u;
				num3++;
			}
			if (0 < num3)
			{
				rcv_buf = slice<Segment>(rcv_buf, num3, rcv_buf.Length);
			}
		}

		public int Input(byte[] data, int len)
		{
			uint earlier = snd_una;
			if (len < 24)
			{
				return 0;
			}
			int num = 0;
			while (true)
			{
				uint num2 = 0u;
				uint num3 = 0u;
				uint num4 = 0u;
				uint una = 0u;
				uint num5 = 0u;
				ushort wnd = 0;
				byte b = 0;
				byte frg = 0;
				if (len - num < 24)
				{
					break;
				}
				num += ikcp_decode32u(data, num, ref num5);
				if (conv != num5)
				{
					return -1;
				}
				num += ikcp_decode8u(data, num, ref b);
				num += ikcp_decode8u(data, num, ref frg);
				num += ikcp_decode16u(data, num, ref wnd);
				num += ikcp_decode32u(data, num, ref num2);
				num += ikcp_decode32u(data, num, ref num3);
				num += ikcp_decode32u(data, num, ref una);
				num += ikcp_decode32u(data, num, ref num4);
				if ((long)(len - num) < (long)((ulong)num4))
				{
					return -2;
				}
				switch (b)
				{
				case 81:
				case 82:
				case 83:
				case 84:
					rmt_wnd = (uint)wnd;
					parse_una(una);
					shrink_buf();
					if (b == 82)
					{
						if (_itimediff(current, num2) >= 0)
						{
							update_ack(_itimediff(current, num2));
						}
						parse_ack(num3);
						shrink_buf();
					}
					else if (b == 81)
					{
						if (_itimediff(num3, rcv_nxt + rcv_wnd) < 0)
						{
							ack_push(num3, num2);
							if (_itimediff(num3, rcv_nxt) >= 0)
							{
								Segment segment = new Segment((int)num4);
								segment.conv = num5;
								segment.cmd = (uint)b;
								segment.frg = (uint)frg;
								segment.wnd = (uint)wnd;
								segment.ts = num2;
								segment.sn = num3;
								segment.una = una;
								if (num4 > 0u)
								{
									Array.Copy(data, (long)num, segment.data, 0L, (long)((ulong)num4));
								}
								parse_data(segment);
							}
						}
					}
					else if (b == 83)
					{
						probe |= 2u;
					}
					else if (b != 84)
					{
						return -3;
					}
					num += (int)num4;
					continue;
				}
				return -3;
			}
			if (_itimediff(snd_una, earlier) > 0 && cwnd < rmt_wnd)
			{
				uint num6 = mss;
				if (cwnd < ssthresh)
				{
					cwnd += 1u;
					incr += num6;
				}
				else
				{
					if (incr < num6)
					{
						incr = num6;
					}
					incr += num6 * num6 / incr + num6 / 16u;
					if ((cwnd + 1u) * num6 <= incr)
					{
						cwnd += 1u;
					}
				}
				if (cwnd > rmt_wnd)
				{
					cwnd = rmt_wnd;
					incr = rmt_wnd * num6;
				}
			}
			return 0;
		}

		private int wnd_unused()
		{
			if ((long)rcv_queue.Length < (long)((ulong)rcv_wnd))
			{
				return (int)(rcv_wnd - (uint)rcv_queue.Length);
			}
			return 0;
		}

		private void flush()
		{
			uint num = current;
			int num2 = 0;
			int num3 = 0;
			if (updated == 0u)
			{
				return;
			}
			Segment segment = new Segment(0);
			segment.conv = conv;
			segment.cmd = 82u;
			segment.wnd = (uint)wnd_unused();
			segment.una = rcv_nxt;
			int num4 = acklist.Length / 2;
			int num5 = 0;
			for (int i = 0; i < num4; i++)
			{
				if ((long)(num5 + 24) > (long)((ulong)mtu))
				{
					output(buffer, num5);
					num5 = 0;
				}
				ack_get(i, ref segment.sn, ref segment.ts);
				num5 += segment.encode(buffer, num5);
			}
			acklist = new uint[0];
			if (rmt_wnd == 0u)
			{
				if (probe_wait == 0u)
				{
					probe_wait = 7000u;
					ts_probe = current + probe_wait;
				}
				else if (_itimediff(current, ts_probe) >= 0)
				{
					if (probe_wait < 7000u)
					{
						probe_wait = 7000u;
					}
					probe_wait += probe_wait / 2u;
					if (probe_wait > 120000u)
					{
						probe_wait = 120000u;
					}
					ts_probe = current + probe_wait;
					probe |= 1u;
				}
			}
			else
			{
				ts_probe = 0u;
				probe_wait = 0u;
			}
			if ((probe & 1u) != 0u)
			{
				segment.cmd = 83u;
				if (num5 + 24 > (int)mtu)
				{
					output(buffer, num5);
					num5 = 0;
				}
				num5 += segment.encode(buffer, num5);
			}
			probe = 0u;
			uint num6 = _imin_(snd_wnd, rmt_wnd);
			if (nocwnd == 0)
			{
				num6 = _imin_(cwnd, num6);
			}
			num4 = 0;
			for (int j = 0; j < snd_queue.Length; j++)
			{
				if (_itimediff(snd_nxt, snd_una + num6) >= 0)
				{
					break;
				}
				Segment segment2 = snd_queue[j];
				segment2.conv = conv;
				segment2.cmd = 81u;
				segment2.wnd = segment.wnd;
				segment2.ts = num;
				segment2.sn = snd_nxt;
				segment2.una = rcv_nxt;
				segment2.resendts = num;
				segment2.rto = rx_rto;
				segment2.fastack = 0u;
				segment2.xmit = 0u;
				snd_buf = append<Segment>(snd_buf, segment2);
				snd_nxt += 1u;
				num4++;
			}
			if (0 < num4)
			{
				snd_queue = slice<Segment>(snd_queue, num4, snd_queue.Length);
			}
			uint num7 = (uint)fastresend;
			if (fastresend <= 0)
			{
				num7 = 4294967295u;
			}
			uint num8 = rx_rto >> 3;
			if (nodelay != 0u)
			{
				num8 = 0u;
			}
			Segment[] array = snd_buf;
			for (int k = 0; k < array.Length; k++)
			{
				Segment segment3 = array[k];
				bool flag = false;
				if (segment3.xmit == 0u)
				{
					flag = true;
					segment3.xmit += 1u;
					segment3.rto = rx_rto;
					segment3.resendts = num + segment3.rto + num8;
				}
				else if (_itimediff(num, segment3.resendts) >= 0)
				{
					flag = true;
					segment3.xmit += 1u;
					xmit += 1u;
					if (nodelay == 0u)
					{
						segment3.rto += rx_rto;
					}
					else
					{
						segment3.rto += rx_rto / 2u;
					}
					segment3.resendts = num + segment3.rto;
					num3 = 1;
				}
				else if (segment3.fastack >= num7)
				{
					flag = true;
					segment3.xmit += 1u;
					segment3.fastack = 0u;
					segment3.resendts = num + segment3.rto;
					num2++;
				}
				if (flag)
				{
					segment3.ts = num;
					segment3.wnd = segment.wnd;
					segment3.una = rcv_nxt;
					int num9 = 24 + segment3.data.Length;
					if ((long)(num5 + num9) > (long)((ulong)mtu))
					{
						output(buffer, num5);
						num5 = 0;
					}
					num5 += segment3.encode(buffer, num5);
					if (segment3.data.Length > 0)
					{
						Array.Copy(segment3.data, 0, buffer, num5, segment3.data.Length);
						num5 += segment3.data.Length;
					}
				}
			}
			if (num5 > 0)
			{
				output(buffer, num5);
			}
			if (num2 != 0)
			{
				uint num10 = snd_nxt - snd_una;
				ssthresh = num10 / 2u;
				if (ssthresh < 2u)
				{
					ssthresh = 2u;
				}
				cwnd = ssthresh + num7;
				incr = cwnd * mss;
			}
			if (num3 != 0)
			{
				ssthresh = cwnd / 2u;
				if (ssthresh < 2u)
				{
					ssthresh = 2u;
				}
				cwnd = 1u;
				incr = mss;
			}
			if (cwnd < 1u)
			{
				cwnd = 1u;
				incr = mss;
			}
		}

		public void Update(uint current_)
		{
			current = current_;
			if (updated == 0u)
			{
				updated = 1u;
				ts_flush = current;
			}
			int num = _itimediff(current, ts_flush);
			if (num >= 10000 || num < -10000)
			{
				ts_flush = current;
				num = 0;
			}
			if (num >= 0)
			{
				ts_flush += interval;
				if (_itimediff(current, ts_flush) >= 0)
				{
					ts_flush = current + interval;
				}
				flush();
			}
		}

		public uint Check(uint current_)
		{
			if (updated == 0u)
			{
				return current_;
			}
			uint num = ts_flush;
			int num2 = 2147483647;
			if (_itimediff(current_, num) >= 10000 || _itimediff(current_, num) < -10000)
			{
				num = current_;
			}
			if (_itimediff(current_, num) >= 0)
			{
				return current_;
			}
			int num3 = _itimediff(num, current_);
			Segment[] array = snd_buf;
			for (int i = 0; i < array.Length; i++)
			{
				Segment segment = array[i];
				int num4 = _itimediff(segment.resendts, current_);
				if (num4 <= 0)
				{
					return current_;
				}
				if (num4 < num2)
				{
					num2 = num4;
				}
			}
			int num5 = num2;
			if (num2 >= num3)
			{
				num5 = num3;
			}
			if ((long)num5 >= (long)((ulong)interval))
			{
				num5 = (int)interval;
			}
			return current_ + (uint)num5;
		}

		public int SetMtu(int mtu_)
		{
			if (mtu_ < 50 || mtu_ < 24)
			{
				return -1;
			}
			byte[] array = new byte[(mtu_ + 24) * 3];
			if (array == null)
			{
				return -2;
			}
			mtu = (uint)mtu_;
			mss = mtu - 24u;
			buffer = array;
			return 0;
		}

		public int Interval(int interval_)
		{
			if (interval_ > 5000)
			{
				interval_ = 5000;
			}
			else if (interval_ < 10)
			{
				interval_ = 10;
			}
			interval = (uint)interval_;
			return 0;
		}

		public int NoDelay(int nodelay_, int interval_, int resend_, int nc_)
		{
			if (nodelay_ > 0)
			{
				nodelay = (uint)nodelay_;
				if (nodelay_ != 0)
				{
					rx_minrto = 30u;
				}
				else
				{
					rx_minrto = 100u;
				}
			}
			if (interval_ >= 0)
			{
				if (interval_ > 5000)
				{
					interval_ = 5000;
				}
				else if (interval_ < 10)
				{
					interval_ = 10;
				}
				interval = (uint)interval_;
			}
			if (resend_ >= 0)
			{
				fastresend = resend_;
			}
			if (nc_ >= 0)
			{
				nocwnd = nc_;
			}
			return 0;
		}

		public int WndSize(int sndwnd, int rcvwnd)
		{
			if (sndwnd > 0)
			{
				snd_wnd = (uint)sndwnd;
			}
			if (rcvwnd > 0)
			{
				rcv_wnd = (uint)rcvwnd;
			}
			return 0;
		}

		public int WaitSnd()
		{
			return snd_buf.Length + snd_queue.Length;
		}
	}
}
