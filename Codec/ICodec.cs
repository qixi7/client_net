namespace Public.Net.Codec
{
	public interface ICodec
	{
		int Encode(byte[] src, byte[] dst);

		int Decode(byte[] src, byte[] dst);
	}
}
