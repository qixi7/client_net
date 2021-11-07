using System;
using System.Threading;

namespace Public.Net
{
	public struct FastMutex
	{
		private const int SpinningFactor = 100;

		private const int SleepOneFrequency = 4;

		private volatile int _lock;

		private volatile int _waiters;

		public void Enter(ref bool lockTaken)
		{
			lockTaken = TryEnter();
			if (lockTaken)
			{
				return;
			}
			int num = Interlocked.Increment(ref _waiters);
			try
			{
				int processorCount = Environment.ProcessorCount;
				if (num < processorCount)
				{
					int num2 = 1;
					for (int i = 1; i <= num * SpinningFactor; i++)
					{
						Thread.SpinWait((num + i) * SpinningFactor * num2);
						lockTaken = TryEnter();
						if (lockTaken)
						{
							return;
						}
						if (num2 < processorCount)
						{
							num2++;
						}
					}
				}
				int num3 = 0;
				do
				{
					Thread.Sleep((num3 % SleepOneFrequency != 0) ? 0 : 1);
					num3++;
					lockTaken = TryEnter();
				}
				while (!lockTaken);
			}
			finally
			{
				Interlocked.Decrement(ref _waiters);
			}
		}

		public bool TryEnter()
		{
			return Interlocked.CompareExchange(ref _lock, 1, 0) == 0;
		}

		public void Exit()
		{
			Interlocked.CompareExchange(ref _lock, 0, 1);
		}
	}
}
