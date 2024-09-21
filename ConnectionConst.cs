namespace NetModule
{
    // 连接类型
    public enum ConnectionType {
        Undefined,
        Tcp,
        Kcp,
        Rdp,
    }

    // 连接状态
    public enum ConnectionState {
        Undefined,
        PrepareConnect,
        Connected,
        Closed
    }

    // 连接错误值
    public enum ConnectionError {
        None,
        ConnectFailed,
        SendBroken,
        RecvBroken,
        DataOverLimit,
        HeartTimeout,
        WriteBufferError,
        WorkThreadException,
        ProtocolViolation,
        SendThreadAbort,
        SendThreadInterrupted,		
        RecvThreadAbort,
        RecvThreadInterrupted
    }
}