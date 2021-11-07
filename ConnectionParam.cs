using System;
using System.Net;

namespace Public.Net
{
	public class ConnectionParam
	{
		public ConnectionType connType;

		public int connectTimeout = 15000;	// default 15s

		public int MaxSplitPackSize = 300;	// 单个数据包最大Size

		public bool secure = false;	// default encryption. // todo.加密还未弄，暂且默认关闭

		public int MaxPackSize = 2 * 1024 * 1024;	// 2M

		public ushort heartMsgID = 1;	// heartMsgID or flag

		public int HeartTickTime = 1000;	// default 1s

		public int HeartTickTimeout = 15000;	// default 15s

		public bool LazyHeartbeat;	// for optimize heart. if you need, open it.

		public string IP;

		public int Port;

		public bool AutoFlush = true;

		private string _cacheString;

		public IPEndPoint RemoteEndPoint
		{
			get;
		}

		public ConnectionParam(string ip, int port, ConnectionType type)
		{
			IP = ip;
			Port = port;
			IPAddress address;
			if (!IPAddress.TryParse(ip, out address))
			{
				throw new Exception("Provided remoteIPAddress string was not succesfully parsed.");
			}
			RemoteEndPoint = new IPEndPoint(address, port);
			connType = type;
		}

		public override string ToString()
		{
			if (_cacheString == null)
			{
				_cacheString = string.Concat(new object[]
				{
					"[",
					connType.ToString(),
					"] ",
					RemoteEndPoint.Address,
					":",
					RemoteEndPoint.Port.ToString()
				});
			}
			return _cacheString;
		}
	}
}
