using System;

namespace Public.Net.RDP
{
	internal class Window
	{
		private const int RttWindow = 4;

		private readonly long[] _v = new long[4];

		private int _i;

		private int _n;

		private long _min;

		public void Append(long value)
		{
			if (value < 0L)
			{
				throw new ArgumentOutOfRangeException(nameof(value), "negative RTT");
			}
			_v[_i] = value;
			_i = (_i + 1) % RttWindow;
			if (_i > _n)
			{
				_n = _i;
			}
			if (value < _min)
			{
				_min = value;
			}
			else
			{
				_min = -1L;
			}
		}

		public long Min()
		{
			if (_min > 0L)
			{
				return _min;
			}
			_min = _v[0];
			for (int i = 1; i < _n; i++)
			{
				if (_v[i] < _min)
				{
					_min = _v[i];
				}
			}
			return _min;
		}
	}
}