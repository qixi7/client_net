using System;
using System.Collections.Generic;

namespace Public.Net
{
	internal struct Pool
	{
		private readonly int _maxCount;

		private readonly int _size;

		private FastMutex _mutex;

		private readonly Stack<byte[]> _pool;

		public Pool(int size, int maxCount)
		{
			_size = size;
			_maxCount = maxCount;
			_mutex = default;
			_pool = new Stack<byte[]>();
		}

		public byte[] Get()
		{
			bool flag = false;
			try
			{
				_mutex.Enter(ref flag);
				if (_pool.Count > 0)
				{
					return _pool.Pop();
				}
			}
			finally
			{
				_mutex.Exit();
			}
			return new byte[_size];
		}

		public void Put(byte[] buf)
		{
			bool flag = false;
			try
			{
				_mutex.Enter(ref flag);
				if (_pool.Count < _maxCount)
				{
					_pool.Push(buf);
				}
			}
			finally
			{
				_mutex.Exit();
			}
		}

		public void Trim(int maxSizeAfterTrim = 0)
		{
			bool flag = false;
			try
			{
				_mutex.Enter(ref flag);
				while (_pool.Count > maxSizeAfterTrim)
				{
					_pool.Pop();
				}
			}
			finally
			{
				_mutex.Exit();
			}
		}
	}


	internal struct Pool<T> where T : new()
	{
		private readonly int _maxSize;

		private FastMutex _mutex;

		private readonly Stack<T> _pool;

		public Pool(int maxSize)
		{
			_maxSize = maxSize;
			_mutex = default;
			_pool = new Stack<T>();
		}

		public T Get()
		{
			bool flag = false;
			try
			{
				_mutex.Enter(ref flag);
				if (_pool.Count > 0)
				{
					return _pool.Pop();
				}
			}
			finally
			{
				_mutex.Exit();
			}
			return Activator.CreateInstance<T>();
		}

		public void Put(T buf)
		{
			bool flag = false;
			try
			{
				_mutex.Enter(ref flag);
				if (_pool.Count < _maxSize)
				{
					_pool.Push(buf);
				}
			}
			finally
			{
				_mutex.Exit();
			}
		}

		public void Trim(int maxSizeAfterTrim = 0)
		{
			bool flag = false;
			try
			{
				_mutex.Enter(ref flag);
				while (_pool.Count > maxSizeAfterTrim)
				{
					_pool.Pop();
				}
			}
			finally
			{
				_mutex.Exit();
			}
		}
	}
}
