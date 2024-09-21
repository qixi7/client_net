using System.Net;
using System.Net.Sockets;
using System.Text;
using NetModule.Log;

namespace NetModule
{
    class Example
    {
        private static volatile Thread _fakeServerThread;

        // step_1: 定义 连接服务器成功委托
        private static void ConnectSuccessEvent(Connection conn)
        {
            LogHelper.DebugF("<Client> ConnectSuccessEvent with type={0}, ip={1}, port={2}",
                conn.InitParam.connType.ToString(), conn.InitParam.Addr, conn.InitParam.Port);
            GameNetPack pack = new GameNetPack();
            pack.msgID = 1001;
            pack.body = Encoding.UTF8.GetBytes("HelloWord!");
            conn.SendData(pack);
        }

        // step_2: 定义 连接服务器失败委托
        private static void ConnectFailedEvent(Connection conn, ConnectionError error)
        {
            LogHelper.ErrorF("<Client> ConnectFailedEvent with type={0}, ip={1}, port={2}, err={3}",
                conn.InitParam.connType.ToString(), conn.InitParam.Addr, conn.InitParam.Port, error.ToString());
        }

        // step_3: 定义 接受消息委托
        private static void RecvMsgEvent(Connection conn, GameNetPack pack)
        {
            LogHelper.DebugF("<Client> RecvMsgEvent Called. msgID={0}, msg={1}",
                pack.msgID, Encoding.UTF8.GetString(pack.body));
        }

        // run example
        public static void RunExample()
        {
            // --------- 测试准备工作. 忽略 Begin-------
            _fakeServerThread = new Thread(FakeTcpServer);
            _fakeServerThread.Start();
            Thread.Sleep(3000);

            string ip = "127.0.0.1";
            int port = 17000;
            ConnectionType connType = ConnectionType.Tcp;
            // --------- 测试准备工作. 忽略 End---------

            // step_4: 创建连接
            ConnectionParam param = new ConnectionParam(ip, port, connType);
            Connection conn = new Connection(param);
            conn.ConnectedEvent = ConnectSuccessEvent;
            conn.ConnectErrorEvent = ConnectFailedEvent;
            conn.RecvMsgEvent = RecvMsgEvent;
            conn.Connect();
            LogHelper.DebugF("<Client> Begin Connect, ip={0}, port={1}, Type={2}",
                ip, port, connType.ToString());

            // 模拟unity Update
            while (true)
            {
                conn.Update();
            }
        }

        // 假装我是个监听TCP的服务器
        private static void FakeTcpServer()
        {
            LogHelper.DebugF("<Server> FakeTcpServer is running ... ");
            IPAddress ip = new IPAddress(new byte[] { 127, 0, 0, 1 });
            TcpListener listener = new TcpListener(ip, 17000);
            listener.Start(); // 开始监听
            LogHelper.InfoF("<Server> Start Listening ...");
            Socket s = listener.AcceptSocket();
            byte[] binData = new byte[80];
            while (true)
            {
                int n = s.Receive(binData); //接受连接请求的字节流
                LogHelper.DebugF("<Server> Recv Msg, length={0}", n);
                // 原封不动发送回去
                s.Send(binData, n, SocketFlags.None);
            }
        }
    }
}