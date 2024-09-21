using System;
using System.Collections.Generic;
using System.Threading;

namespace NetModule
{
	public sealed class NetQueue : IDisposable
	{
		private Queue<GameNetPack> _queue = new Queue<GameNetPack>();

		private volatile int _size;

		private readonly int _maxSize;

		private readonly AutoResetEvent _dataAvailable = new AutoResetEvent(false);

		private readonly AutoResetEvent _spaceAvailable = new AutoResetEvent(true);

		private volatile bool _interrupted;

		private bool _disposedValue;

		public int Count
		{
			get
			{
				if (_disposedValue)
				{
					throw new ObjectDisposedException("NetQueue");
				}
				object queue = _queue;
				int count;
				lock (queue)
				{
					count = _queue.Count;
				}
				return count;
			}
		}

		public int ByteSize
		{
			get
			{
				if (_disposedValue)
				{
					throw new ObjectDisposedException("NetQueue");
				}
				object queue = _queue;
				int size;
				lock (queue)
				{
					size = _size;
				}
				return size;
			}
		}

		public NetQueue(int maxSize)
		{
			_maxSize = maxSize;
		}

		public void Interrupt()
		{
			_interrupted = true;
			_dataAvailable.Set();
			_spaceAvailable.Set();
		}

		public GameNetPack Pop()
		{
			if (_disposedValue)
			{
				throw new ObjectDisposedException("NetQueue");
			}
			GameNetPack result = default;
			while (!PopInternal(ref result))
			{
				_dataAvailable.WaitOne();
				if (_interrupted)
				{
					throw new ThreadInterruptedException();
				}
			}
			return result;
		}

		public bool TryPop(ref GameNetPack pack)
		{
			if (_disposedValue)
			{
				throw new ObjectDisposedException("NetQueue");
			}
			return PopInternal(ref pack);
		}

		private bool PopInternal(ref GameNetPack pack)
		{
			object queue = _queue;
			lock (queue)
			{
				if (_queue.Count <= 0)
				{
					return false;
				}
				pack = _queue.Dequeue();
				_size = _size - pack.GetByteSize();
			}
			_spaceAvailable.Set();
			return true;
		}

		public void Push(GameNetPack pack)
		{
			if (_disposedValue)
			{
				throw new ObjectDisposedException("NetQueue");
			}
			while (!PushInternal(pack))
			{
				_spaceAvailable.WaitOne();
				if (_interrupted)
				{
					throw new ThreadInterruptedException();
				}
			}
		}

		public bool TryPush(GameNetPack pack)
		{
			if (_disposedValue)
			{
				throw new ObjectDisposedException("NetQueue");
			}
			return PushInternal(pack);
		}

		private bool PushInternal(GameNetPack node)
		{
			object queue = _queue;
			lock (queue)
			{
				int finalSize = _size + node.GetByteSize();
				if (finalSize > _maxSize)
				{
					return false;
				}
				_queue.Enqueue(node);
				_size = finalSize;
			}
			_dataAvailable.Set();
			return true;
		}

		public void Clear()
		{
			if (_disposedValue)
			{
				throw new ObjectDisposedException("NetQueue");
			}
			object queue = _queue;
			lock (queue)
			{
				_queue.Clear();
			}
		}

		private void Dispose(bool disposing)
		{
			if (!_disposedValue)
			{
				if (disposing)
				{
					_dataAvailable.Close();
					_spaceAvailable.Close();
				}
				_queue = null;
				_disposedValue = true;
			}
		}

		public void Dispose()
		{
			Dispose(true);
		}
	}
}
