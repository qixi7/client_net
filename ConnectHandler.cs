using System;

namespace Public.Net
{
	// 连接成功委托
	public delegate void ConnectHandler(Connection conn);
	// 连接失败委托
	public delegate void ConnectErrorHandler(Connection conn, ConnectionError error);
	// 收到消息委托
	public delegate void RecvMsgHandler(Connection conn, GameNetPack pack);
}
