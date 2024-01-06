using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Okolni.Source.Query.Pool.Common
{
    public static class ArrayPoolInterface
    {
        private static int _counter;
        public static byte[] Rent(int size)
        {
            Interlocked.Increment(ref _counter);
            return ArrayPool<byte>.Shared.Rent(size);
        }

        public static void Return(byte[] rentedArray)
        {
            Interlocked.Decrement(ref _counter);
            ArrayPool<byte>.Shared.Return(rentedArray);
        }
        public static int NotReturnedArrays => _counter;

    }
}
