using System;

namespace Okolni.Source.Common
{
    public struct MultiPacketResponse
    {
        public long Id { get; set; }
        public int Total { get; set; }
        public int Number { get; set; }
        public int Size { get; set; }
        public Memory<byte> Payload { get; set; }
        public byte[] ReceivedByteArrayRef { get; set; }
    }
}