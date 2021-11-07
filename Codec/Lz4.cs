using System;
using System.IO;
using System.Runtime.InteropServices;

namespace Public.Net.Codec
{
	public class Lz4 : ICodec
	{
		private static class Api
		{
//			private const string Dll = "lz4";
//
//			[System.Runtime.InteropServices.DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
//			public static extern int LZ4_compress_fast_extState_fastReset(System.IntPtr state, System.IntPtr src, System.IntPtr dst, int srcSize, int dstCapacity, int acceleration);
//
//			[System.Runtime.InteropServices.DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
//			public static extern int LZ4_decompress_safe(System.IntPtr src, System.IntPtr dst, int compressedSize, int dstCapacity);
//
//			[System.Runtime.InteropServices.DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
//			public static extern int LZ4_sizeofState();
		}

		private readonly byte[] _state;

		private GCHandle _stateHandle;

		private Lz4()
		{
//			int num = Lz4.Api.LZ4_sizeofState();
//			this._state = new byte[num];
//			this._stateHandle = System.Runtime.InteropServices.GCHandle.Alloc(this._state, System.Runtime.InteropServices.GCHandleType.Pinned);
		}

		~Lz4()
		{
			_stateHandle.Free();
		}

		public static Lz4 Create()
		{
			return new Lz4();
		}

		public int Encode(byte[] src, byte[] dst)
		{
			if (src == null)
			{
				throw new ArgumentNullException(nameof(src));
			}
			if (dst == null)
			{
				throw new ArgumentNullException(nameof(dst));
			}
			if (src.Length < 1)
			{
				return 0;
			}
			GCHandle gCHandle = GCHandle.Alloc(src, GCHandleType.Pinned);
			try
			{
				GCHandle gCHandle2 = GCHandle.Alloc(dst, GCHandleType.Pinned);
				try
				{
//					int num = Lz4.Api.LZ4_compress_fast_extState_fastReset(this._stateHandle.AddrOfPinnedObject(), gCHandle.AddrOfPinnedObject(), gCHandle2.AddrOfPinnedObject(), src.Length, dst.Length, 0);
//					if (num > 0 && num < src.Length)
//					{
//						return num;
//					}
				}
				finally
				{
					gCHandle2.Free();
				}
			}
			finally
			{
				gCHandle.Free();
			}
			return 0;
		}

		public int Decode(byte[] src, byte[] dst)
		{
			if (src == null)
			{
				throw new ArgumentNullException(nameof(src));
			}
			if (dst == null)
			{
				throw new ArgumentNullException(nameof(dst));
			}
			if (src.Length < 1)
			{
				return 0;
			}
			GCHandle gCHandle = GCHandle.Alloc(src, GCHandleType.Pinned);
			try
			{
				GCHandle gCHandle2 = GCHandle.Alloc(dst, GCHandleType.Pinned);
				try
				{
//					int num = Lz4.Api.LZ4_decompress_safe(gCHandle.AddrOfPinnedObject(), gCHandle2.AddrOfPinnedObject(), src.Length, dst.Length);
//					if (num > 0)
//					{
//						return num;
//					}
				}
				finally
				{
					gCHandle2.Free();
				}
			}
			finally
			{
				gCHandle.Free();
			}
			throw new IOException("LZ4 decode failed");
		}
	}
}
