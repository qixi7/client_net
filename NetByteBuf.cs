using System;

namespace Public.Net
{
	public class NetByteBuf
	{
		private int len;

		private byte[] data;

		private int readerIndex;

		private int writerIndex;

		private int markReader;

		private int markWriter;

		public int ReaderIndex
		{
			get
			{
				return readerIndex;
			}
			set
			{
				if (readerIndex <= writerIndex)
				{
					readerIndex = value;
				}
			}
		}

		public int WriterIndex
		{
			get => writerIndex;
			set
			{
				if (writerIndex >= readerIndex && writerIndex <= len)
				{
					writerIndex = value;
				}
			}
		}

		public byte[] Raw
		{
			get
			{
				return data;
			}
		}

		public NetByteBuf(int capacity)
		{
			len = capacity;
			data = new byte[len];
			Clear();
		}

		public int Capacity()
		{
			return len;
		}

		public NetByteBuf Capacity(int nc)
		{
			if (nc > len)
			{
				byte[] sourceArray = data;
				data = new byte[nc];
				Array.Copy(sourceArray, data, len);
				len = nc;
			}
			return this;
		}

		public NetByteBuf Clear()
		{
			readerIndex = 0;
			writerIndex = 0;
			markReader = 0;
			markWriter = 0;
			return this;
		}

		public NetByteBuf Copy()
		{
			NetByteBuf netByteBuf = new NetByteBuf(len);
			Array.Copy(data, netByteBuf.data, len);
			netByteBuf.readerIndex = readerIndex;
			netByteBuf.writerIndex = writerIndex;
			netByteBuf.markReader = markReader;
			netByteBuf.markWriter = markWriter;
			return netByteBuf;
		}

		public NetByteBuf MarkReaderIndex()
		{
			markReader = readerIndex;
			return this;
		}

		public NetByteBuf MarkWriterIndex()
		{
			markWriter = writerIndex;
			return this;
		}

		public int MaxWritableBytes()
		{
			return len - writerIndex;
		}

		public byte ReadByte()
		{
			if (readerIndex + 1 <= writerIndex)
			{
				return data[readerIndex++];
			}
			return 0;
		}

		/*
		public int ReadInt()
		{
			if (readerIndex + 4 <= writerIndex)
			{
				int num = (int)((long)((long)data[readerIndex++] << 24) & (long)((ulong)-16777216));
				num |= ((int)data[readerIndex++] << 16 & 16711680);
				num |= ((int)data[readerIndex++] << 8 & 65280);
				return num | (int)(data[readerIndex++] & 255);
			}
			return 0;
		}

		public uint ReadUInt()
		{
			if (readerIndex + 4 <= writerIndex)
			{
				int num = (int)((long)((long)data[readerIndex++] << 24) & (long)((ulong)-16777216));
				num |= ((int)data[readerIndex++] << 16 & 16711680);
				num |= ((int)data[readerIndex++] << 8 & 65280);
				return (uint)(num | (int)(data[readerIndex++] & 255));
			}
			return 0u;
		}
		*/

		public short ReadShort()
		{
			if (readerIndex + 2 <= writerIndex)
			{
				int num = data[readerIndex++];
				int num2 = (data[readerIndex++] & 255);
				int num3 = (num << 8 & 65280) | num2;
				return (short)num3;
			}
			return 0;
		}

		public ushort ReadUShort()
		{
			if (readerIndex + 2 <= writerIndex)
			{
				int num = data[readerIndex++];
				int num2 = (data[readerIndex++] & 255);
				int num3 = (num << 8 & 65280) | num2;
				return (ushort)num3;
			}
			return 0;
		}

		public void ReadBytes(byte[] buffer, int offset, int count)
		{
			if (readerIndex + count <= writerIndex)
			{
				Array.Copy(data, readerIndex, buffer, offset, count);
				readerIndex += count;
			}
		}

		public int ReadableBytes()
		{
			return writerIndex - readerIndex;
		}

		public NetByteBuf ResetReaderIndex()
		{
			if (markReader <= writerIndex)
			{
				readerIndex = markReader;
			}
			return this;
		}

		public NetByteBuf ResetWriterIndex()
		{
			if (markWriter >= readerIndex)
			{
				writerIndex = markWriter;
			}
			return this;
		}

		public int WritableBytes()
		{
			return len - writerIndex;
		}

		public NetByteBuf WriteByte(byte value)
		{
			Capacity(writerIndex + 1);
			data[writerIndex++] = value;
			return this;
		}

		public NetByteBuf WriteInt(int value)
		{
			Capacity(writerIndex + 4);
			data[writerIndex++] = (byte)(value >> 24 & 255);
			data[writerIndex++] = (byte)(value >> 16 & 255);
			data[writerIndex++] = (byte)(value >> 8 & 255);
			data[writerIndex++] = (byte)(value & 255);
			return this;
		}

		public NetByteBuf WriteUInt(uint value)
		{
			Capacity(writerIndex + 4);
			data[writerIndex++] = (byte)(value >> 24 & 255u);
			data[writerIndex++] = (byte)(value >> 16 & 255u);
			data[writerIndex++] = (byte)(value >> 8 & 255u);
			data[writerIndex++] = (byte)(value & 255u);
			return this;
		}

		public NetByteBuf WriteShort(short value)
		{
			Capacity(writerIndex + 2);
			data[writerIndex++] = (byte)(value >> 8 & 255);
			data[writerIndex++] = (byte)(value & 255);
			return this;
		}

		public NetByteBuf WriteUShort(ushort value)
		{
			Capacity(writerIndex + 2);
			data[writerIndex++] = (byte)(value >> 8 & 255);
			data[writerIndex++] = (byte)(value & 255);
			return this;
		}

		public NetByteBuf WriteBytes(NetByteBuf src)
		{
			int num = src.writerIndex - src.readerIndex;
			Capacity(writerIndex + num);
			if (num > 0)
			{
				Array.Copy(src.data, src.readerIndex, data, writerIndex, num);
				writerIndex += num;
				src.readerIndex += num;
			}
			return this;
		}

		public NetByteBuf WriteBytes(NetByteBuf src, int len)
		{
			if (len > 0)
			{
				Capacity(writerIndex + len);
				Array.Copy(src.data, src.readerIndex, data, writerIndex, len);
				writerIndex += len;
				src.readerIndex += len;
			}
			return this;
		}

		public NetByteBuf WriteBytes(byte[] src)
		{
			int num = src.Length;
			Capacity(writerIndex + num);
			if (num > 0)
			{
				Array.Copy(src, 0, data, writerIndex, num);
				writerIndex += num;
			}
			return this;
		}

		public NetByteBuf WriteBytes(byte[] src, int off, int len)
		{
			if (len > 0)
			{
				Capacity(writerIndex + len);
				Array.Copy(src, off, data, writerIndex, len);
				writerIndex += len;
			}
			return this;
		}

		public void ResetBuffer()
		{
			markReader = 0;
			markWriter = 0;
			int num = writerIndex - readerIndex;
			if (num > 0)
			{
				Buffer.BlockCopy(data, readerIndex, data, 0, num);
 			}
 			else
 			{
 				num = 0;
 			}
 			readerIndex = 0;
 			writerIndex = num;
 		}
 	}
 }
