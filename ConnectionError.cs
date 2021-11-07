using System;

namespace Public.Net
{
	public enum ConnectionError
	{
		None,
		ConnectFailed,
		ServerClose,
		DataOverLimit,
		HeartTimeout,
		WriteBufferError,
		WorkThreadException,
		ProtocolViolation
	}
}
