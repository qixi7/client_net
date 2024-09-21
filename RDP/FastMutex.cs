using System;
using System.Threading;
using NetModule.Log;

namespace NetModule.RDP
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
			int waiterNum = Interlocked.Increment(ref _waiters);
			try
			{
				int processorCount = Environment.ProcessorCount;
				int num;
				if (waiterNum < processorCount)
				{
					num = 1;
					for (int i = 1; i <= waiterNum * SpinningFactor; i++)
					{
						Thread.SpinWait((waiterNum + i) * SpinningFactor * num);
						lockTaken = TryEnter();
						if (lockTaken)
						{
							return;
						}
						if (num < processorCount)
						{
							num++;
						}
					}
				}
				num = 0;
				do
				{
					Thread.Sleep((num % SleepOneFrequency != 0) ? 0 : 1);
					num++;
					lockTaken = TryEnter();
				}
				while (!lockTaken);
			}
			catch (Exception e)
			{
				LogHelper.ErrorF("FastMutex Enter err={0}", e);
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