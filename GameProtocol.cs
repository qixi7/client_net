using System;
using NetModule.Log;

namespace NetModule
{
    // 使用SimpleNetPack
    public struct GameNetPack
    {
        /*
         * 	网络协议包. 协议序：
         *  |<msgID>|<body>|
         *  |   2   |  ..  |
         */
        public ushort msgID;
        public byte[] body;
        public const int Overhead = 2;    // 2 msgID
        public const int PackSizeLen = 4; // 流式传输才使用: 单个包二进制长度
        
        public int GetByteSize()
        {
            if (body == null)
            {
                return Overhead;
            }
            return body.Length + Overhead;
        }
    }

    public class GameProtocol
    {
        // 最大包Size
        private const int MaxPacketSize = 4 * 1024 * 1024; // 4M
        private byte[] _recvBuf;

        public GameProtocol()
        {
            _recvBuf = new byte[MaxPacketSize];
        }

        public int ReadPack(ProtocolReader reader, ref GameNetPack pack)
        {
            int bytesSize = RDP.Datagram.Mtu;
            int recvCount = 0;
            if (!reader.IsDataMode())
            {
                // 流式传输先在这里读header
                reader.Read(_recvBuf, 0, GameNetPack.PackSizeLen);
                // 流式总包长(流式传输多4字节总包长PackSizeLen)
                bytesSize = (int)BigEndian.ToUInt32(_recvBuf, 0);
                recvCount += 4;
            }
            bytesSize = reader.Read(_recvBuf, 0, bytesSize);
            if (bytesSize < GameNetPack.Overhead)
            {
                // 抛出异常
                throw new Exception($"bytesSize len={bytesSize} err"); 
            }
            // unmarshal
            // 1: msgID
            pack.msgID = BigEndian.ToUInt16(_recvBuf, 0);
            // 2: body
            int bodyLen = bytesSize-2;
            pack.body = new byte[bodyLen];
            Array.Copy(
                _recvBuf,
                2,
                pack.body,
                0,
                bodyLen);
            recvCount += bytesSize;
            return recvCount;
        }

        public int WritePack(ref GameNetPack pack, ProtocolWriter writer)
        {
            int byteSize = pack.GetByteSize();
            byte[] writeBody = new byte[byteSize];
            // write msgID
            BigEndian.PutBytes(writeBody, pack.msgID, 0);
            // write seq
            Array.Copy(
                pack.body, 
                0,
                writeBody, 
                GameNetPack.Overhead, 
                pack.body.Length);
            if (!writer.IsDataMode())
            {
                writer.Write((uint)byteSize);
                byteSize += 4;
            }
            writer.Write(writeBody, 0, writeBody.Length);
            // 如果是UDP模式, 直接flush
            if (writer.IsDataMode())
            {
                writer.Flush();
            }
            return byteSize;
        }
    }
}