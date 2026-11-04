using System;

namespace FreeVelo.Devices.Ble
{
    /// <summary>
    /// Sequential little-endian reader for Bluetooth GATT payloads.
    /// Throws <see cref="FormatException"/> when reading past the end.
    /// </summary>
    internal ref struct ByteReader
    {
        private readonly ReadOnlySpan<byte> _data;

        public ByteReader(ReadOnlySpan<byte> data)
        {
            _data = data;
            Position = 0;
        }

        public int Position { get; private set; }

        public int Remaining => _data.Length - Position;

        public byte ReadUInt8()
        {
            Require(1);
            return _data[Position++];
        }

        public ushort ReadUInt16()
        {
            Require(2);
            int value = _data[Position] | (_data[Position + 1] << 8);
            Position += 2;
            return (ushort)value;
        }

        public short ReadInt16() => unchecked((short)ReadUInt16());

        public int ReadUInt24()
        {
            Require(3);
            int value = _data[Position] | (_data[Position + 1] << 8) | (_data[Position + 2] << 16);
            Position += 3;
            return value;
        }

        public uint ReadUInt32()
        {
            Require(4);
            uint value = (uint)(_data[Position]
                                | (_data[Position + 1] << 8)
                                | (_data[Position + 2] << 16)
                                | (_data[Position + 3] << 24));
            Position += 4;
            return value;
        }

        public void Skip(int count)
        {
            Require(count);
            Position += count;
        }

        public byte[] ReadRemaining()
        {
            byte[] rest = _data.Slice(Position).ToArray();
            Position = _data.Length;
            return rest;
        }

        private void Require(int count)
        {
            if (Position + count > _data.Length)
            {
                throw new FormatException(
                    $"Payload too short: need {count} byte(s) at offset {Position}, length is {_data.Length}.");
            }
        }
    }
}
