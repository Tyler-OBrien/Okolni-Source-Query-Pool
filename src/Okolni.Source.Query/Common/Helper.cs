﻿using System;
using Okolni.Source.Common.ByteHelper;
using static Okolni.Source.Common.Enums;

namespace Okolni.Source.Common
{
    /// <summary>
    /// Helper class with static methods to help with arrays and other stuff
    /// </summary>
    public static class Helper
    {
        /// <summary>
        /// Gets a subarray specified by the index and the length
        /// </summary>
        /// <param name="data">The array to get the subarray from</param>
        /// <param name="index">The index from where to get the subarray</param>
        /// <param name="length">The length of the subarray to extract</param>
        /// <typeparam name="T">The generic type of the array</typeparam>
        /// <returns>The sub array</returns>
        public static Memory<T> SubArray<T>(this Memory<T> data, int index, int length)
        {
            return data.Slice(index, length);
        }

        public static int GetNextNullCharPosition(this Memory<byte> data, int startindex)
        {
            var dataspan = data.Span;
            for (; startindex < data.Length; startindex++)
            {
                if (dataspan[startindex].Equals(0x00))
                    return startindex;
            }
            return -1;
        }

        public static IByteReader GetByteReader(this byte[] data)
        {
            return new ByteReader(data);
        }
        public static IByteReader GetByteReader(this Memory<byte> data, byte[] underlyingArray)
        {
            return new ByteReader(underlyingArray, data);
        }
        public static IByteReader GetByteReader(this Tuple<Memory<byte>, byte[]> response)
        {
            return new ByteReader(response.Item2, response.Item1);
        }

        public static ServerType ToServerType(this byte data)
        {
            if (!Constants.ByteServerTypeMapping.TryGetValue(data, out var returnval))
                throw new ArgumentException("Given byte cannot be parsed");
            else
                return returnval;
        }

        public static Enums.Environment ToEnvironment(this byte data)
        {
            if (!Constants.ByteEnvironmentMapping.TryGetValue(data, out var returnval))
                throw new ArgumentException("Given byte cannot be parsed");
            else
                return returnval;
        }

        public static Visibility ToVisibility(this byte data)
        {
            if (!Constants.ByteVisibilityMapping.TryGetValue(data, out var returnval))
                throw new ArgumentException("Given byte cannot be parsed");
            else
                return returnval;
        }
    }
}
