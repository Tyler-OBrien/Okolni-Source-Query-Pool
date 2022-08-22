using System;
using System.Text;

namespace Okolni.Source.Common.ByteHelper
{
    /// <inheritdoc/>
    public class ByteReader : IByteReader
    {
        private int _iterator = 0;
        private Memory<byte>? _response;

        /// <inheritdoc/>
        public Memory<byte> Response
        {
            get
            {
                if (_response.HasValue == false)
                    throw new ArgumentNullException("Response has to be set in order to get values from it");

                return _response.Value;
            }
            set
            {
                _response = value;
                Iterator = 0;
            }
        }

        /// <inheritdoc/>
        public int Iterator
        {
            get
            {
                return _iterator;
            }
            set
            {
                if (value < Response.Length && value >= 0)
                    _iterator = value;
                else if (value >= Response.Length && value >= 0)
                    _iterator = -1;
            }
        }

        /// <inheritdoc/>
        public int Remaining
        {
            get
            {
                if (Iterator == -1)
                    return 0;
                return Response.Length - Iterator;
            }
        }

        /// <inheritdoc/>
        public ByteReader(byte[] response)
        {
            Response = response;
        }

        /// <inheritdoc/>
        public byte[] GetBytes(int length)
        {
            if (length <= 0)
                throw new ArgumentOutOfRangeException("The Length must be at least 1");
            if (Remaining < length)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");

            var val = Response.SubArray(Iterator, length);
            Iterator += length;
            return val.ToArray();
        }

        /// <inheritdoc/>
        public byte GetByte()
        {
            if (Remaining < 1)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");
            
            var val = Response.Span[Iterator];
            Iterator++;
            return val;
        }

        /// <inheritdoc/>
        public float GetFloat()
        {
            if (Remaining < 4)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");

            float floatValue = BitConverter.ToSingle(Response.Slice(Iterator, 4).Span);
            Iterator += 4;

            return floatValue;
        }

        /// <inheritdoc/>
        public short GetShort()
        {
            if (Remaining < 2)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");

            short shortValue = BitConverter.ToInt16(Response.Slice(Iterator, 2).Span);
            Iterator += 2;

            return shortValue;
        }


        /// <inheritdoc/>
        public ushort GetUShort()
        {
            if (Remaining < 2)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");

            ushort shortValue = BitConverter.ToUInt16(Response.Slice(Iterator, 2).Span);
            Iterator += 2;

            return shortValue;
        }

        /// <inheritdoc/>
        public int GetInt()
        {
            if (Remaining < 4)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");

            int intValue = BitConverter.ToInt32(Response.Slice(Iterator, 4).Span);
            Iterator += 4;

            return intValue;
        }


        /// <inheritdoc/>
        public uint GetUInt()
        {
            if (Remaining < 4)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");

            uint uintValue = BitConverter.ToUInt32(Response.Slice(Iterator, 4).Span);
            Iterator += 4;

            return uintValue;
        }


        /// <inheritdoc/>
        public long GetLong()
        {
            if (Remaining < 8)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");

            long longValue = BitConverter.ToInt64(Response.Slice(Iterator, 8).Span);
            Iterator += 8;

            return longValue;
        }

        /// <inheritdoc/>
        public ulong GetULong()
        {
            if (Remaining < 8)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");

            ulong longValue = BitConverter.ToUInt64(Response.Slice(Iterator, 8).Span);
            Iterator += 8;

            return longValue;
        }

        /// <inheritdoc/>
        public string GetString()
        {
            if (Remaining < 1)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");

            int posNextNullChar = Response.GetNextNullCharPosition(Iterator);

            if (posNextNullChar == -1)
                throw new ArgumentOutOfRangeException("No valid string could be found in the remaining bytes");

            string stringValue = Encoding.UTF8.GetString(Response.Slice(Iterator, posNextNullChar - Iterator).Span);

            Iterator = posNextNullChar + 1;

            return stringValue;
        }

        /// <inheritdoc/>
        public byte[] GetRemaining(int? iterator = null)
        {
            if (iterator == null)
                iterator = Iterator;

            if (Remaining < 1)
                throw new ArgumentOutOfRangeException("Not Enough bytes left to read");

            return Response.Slice(iterator.Value).ToArray();
        }
    }
}
