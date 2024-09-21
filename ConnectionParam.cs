using System;
using System.Net;

namespace NetModule
{
	public class ConnectionParam
	{
		// 连接类型
		public ConnectionType connType;
		// 连接超时时间. 单位: ms
		public int ConnectTimeout = 15000;
		// 单个数据包最大Size
		public const int MaxPackSize = 2 * 1024 * 1024;	// 2M
		// 心跳消息标记
		public uint HeartMsgID = 1;
		// 发心跳间隔
		public int HeartTickTime = 1000;	// default 1s
		// 心跳超时时间
		public int HeartTickTimeout = 15000;	// default 15s
		// 心跳优化: 若心跳周期内与服务器有成功的消息交互, 则本次不发心跳节省流量
		public bool LazyHeartbeat;
		// 服务器地址 & 端口
		public string Addr;
		public int Port;
		public IPEndPoint RemoteEndPoint { get; private set; }
		// 是否自动即时发送
		public bool AutoFlush = true;

		public ConnectionParam(string addr, int port, ConnectionType type)
		{
			Addr = addr;
			Port = port;
			connType = type;
			IPAddress address;
			if (IPAddress.TryParse(Addr, out address))
			{
				RemoteEndPoint = new IPEndPoint(address, Port);
				return;
			}

			var hostInfo = Dns.GetHostEntry(Addr);
			if (hostInfo == null || hostInfo.AddressList.Length == 0)
			{
				throw new Exception("Provided remoteIPAddress string was not succesfully parsed.");				
			}
			RemoteEndPoint = new IPEndPoint(hostInfo.AddressList[0], Port);
		}
	}
}
