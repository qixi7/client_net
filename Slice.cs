using System;
using System.Collections;
using System.Collections.Generic;

namespace NetModule
{
	public struct Slice<T> : IEnumerable<T>
	{
		// 迭代器
		private struct SliceEnumerator : IEnumerator<T>
		{
			public Slice<T> Slice;

			public int CurrentIndex;

			object IEnumerator.Current => Current;

			public T Current => Slice.BaseArray[CurrentIndex - 1];

			public void Dispose()
			{
			}

			public bool MoveNext()
			{
				if (CurrentIndex >= Slice.To)
				{
					return false;
				}
				CurrentIndex++;
				return true;
			}

			public void Reset()
			{
				CurrentIndex = Slice.From;
			}
		}

		// Slice 定义
		public T[] BaseArray;

		public int Length => To - From;

		public int To
		{
			get;
			private set;
		}

		public int From
		{
			get;
			private set;
		}

		public T Get(int index)
		{
			int length = Length;
			if (index < 0)
			{
				index += length;
			}
			if (index < 0 || index >= length)
			{
				throw new IndexOutOfRangeException();
			}
			return BaseArray[From + index];
		}

		public void Set(int index, T value)
		{
			int length = Length;
			if (index < 0)
			{
				index += length;
			}
			if (index < 0 || index >= length)
			{
				throw new IndexOutOfRangeException();
			}
			BaseArray[From + index] = value;
		}

		public static Slice<T> Make(T[] baseArray)
		{
			return Make(baseArray, 0, baseArray.Length);
		}

		public static Slice<T> Make(T[] baseArray, int from)
		{
			return Make(baseArray, from, baseArray.Length);
		}

		public static Slice<T> Make(T[] baseArray, int from, int to)
		{
			if (baseArray == null)
			{
				throw new ArgumentNullException(nameof(baseArray));
			}
			int arrLen = baseArray.Length;
			if (from < 0)
			{
				from += arrLen;
			}
			if (from < 0 || from > arrLen)
			{
				throw new IndexOutOfRangeException();
			}
			if (to < 0)
			{
				to += arrLen;
			}
			if (to < 0 || to > arrLen)
			{
				throw new IndexOutOfRangeException();
			}
			if (from > to)
			{
				throw new IndexOutOfRangeException();
			}
			return new Slice<T>
			{
				BaseArray = baseArray,
				From = from,
				To = to
			};
		}
        
        public void Reset()
        {
            From = 0;
            To = BaseArray.Length;
        }

		public int CopyTo(Slice<T> b)
		{
			int minLen = Math.Min(Length, b.Length);
			Array.Copy(BaseArray, From, b.BaseArray, b.From, minLen);
			return minLen;
		}

		public Slice<T> Cut(int from)
		{
			return Cut(from, Length);
		}

		public Slice<T> Cut(int from, int to)
		{
			int length = Length;
			if (from < 0)
			{
				from += length;
			}
			if (from < 0 || from > length)
			{
				throw new IndexOutOfRangeException();
			}
			if (to < 0)
			{
				to += length;
			}
			if (to < 0 || to > length)
			{
				throw new IndexOutOfRangeException();
			}
			if (from > to)
			{
				throw new IndexOutOfRangeException();
			}
			return new Slice<T>
			{
				BaseArray = BaseArray,
				From = From + from,
				To = From + to
			};
		}

		public IEnumerator<T> GetEnumerator()
		{
			return new SliceEnumerator
			{
				Slice = this,
				CurrentIndex = From
			};
		}

		IEnumerator IEnumerable.GetEnumerator()
		{
			return GetEnumerator();
		}
	}
}
