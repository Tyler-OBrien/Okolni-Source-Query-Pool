using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Okolni.Source.Query.Pool.Common.SocketHelpers
{
    public struct ArrayPoolMemory
    {
        public byte[] RawRequest;

        public Memory<byte> UsableChunk;

        public ArrayPoolMemory(byte[] borrowedArray, int intendedLength)
        {
            RawRequest = borrowedArray;
            UsableChunk = borrowedArray.AsMemory().Slice(0, intendedLength);
        }
    }
}
