using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Public.Net
{
	public class LinkMonitor
	{
		private readonly Timer _timer;

		private readonly Socket _conn;

		private readonly List<int> _ping = new List<int>();

		private readonly List<double> _rtt = new List<double>();

		private volatile int _seq = -1;

		private volatile bool _closed;

		private readonly byte[] _sendBuf = new byte[16];

		public LinkMonitor(string ip, int port, Connection conn)
		{
			IPAddress address;
			if (!IPAddress.TryParse(ip, out address)){
				throw new System.Exception("Provided remoteIPAddress string was not succesfully parsed.");
			}
			IPEndPoint ep = new IPEndPoint(address, port);
			_conn = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
			_conn.Connect(ep);
			_timer = new Timer(SendTask, conn, conn.InitParam.HeartTickTime, conn.InitParam.HeartTickTime);
			new Thread(ReceiveThread).Start();
		}

		private void SendTask(object so)
		{
			Connection connection = (Connection)so;
			object ping = _ping;
			lock (ping)
			{
				_ping.Add(-1);
			}
			object rtt = _rtt;
			lock (rtt)
			{
				_rtt.Add(connection.RTT);
			}
			Slice<byte> slice = Slice<byte>.Make(_sendBuf);
			BigEndian.PutBytes(slice.Cut(0, 4), (uint)Interlocked.Increment(ref _seq));
			BigEndian.PutBytes(slice.Cut(4, 12), (ulong)Connection.Now());
			BigEndian.PutBytes(slice.Cut(12, 16), Crc32.Hash(slice.Cut(0, 12), 0u));
			_conn.Send(_sendBuf);
		}

		private void ReceiveThread()
		{
			byte[] array = new byte[16];
			Slice<byte> slice = Slice<byte>.Make(array);
			try
			{
				while (!_closed)
				{
					int num = _conn.Receive(array);
					if (num == array.Length)
					{
						if (BigEndian.ToUInt32(slice.Cut(12, 16)) == Crc32.Hash(slice.Cut(0, 12), 0u))
						{
							int seq = (int)BigEndian.ToUInt32(slice.Cut(0, 4));
							long timeDiff = Connection.Now() - (long)BigEndian.ToUInt64(slice.Cut(4, 12));
							object ping = _ping;
							lock (ping)
							{
								if (seq >= 0 && _ping.Count > seq)
								{
									if (_ping[seq] < 0.0)
									{
										_ping[seq] = (int)timeDiff / ((int)Stopwatch.Frequency / 1000);
									}
								}
							}
						}
					}
				}
			}
			catch (System.Exception)
			{
			}
		}

		public void Close()
		{
			_closed = true;
			_timer.Dispose();
			_conn.Close();
		}

		public IEnumerable<int> GetPing()
		{
			object ping = _ping;
			IEnumerable<int> result;
			lock (ping)
			{
				result = new ReadOnlyCollection<int>(_ping);
			}
			return result;
		}

		public IEnumerable<double> GetRtt()
		{
			object rtt = _rtt;
			IEnumerable<double> result;
			lock (rtt)
			{
				result = new ReadOnlyCollection<double>(_rtt);
			}
			return result;
		}
	}
}
