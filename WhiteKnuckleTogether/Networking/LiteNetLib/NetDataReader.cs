// Portions of this file are derived from LiteNetLib
// https://github.com/RevenantX/LiteNetLib
//
// Original license:
//
// MIT License
//
// Copyright (c) 2025 Ruslan Pyrch
//
// Permission is hereby granted, free of charge, to any person obtaining a copy
// of this software and associated documentation files (the "Software"), to deal
// in the Software without restriction, including without limitation the rights
// to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
// copies of the Software, and to permit persons to whom the Software is
// furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in all
// copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
// IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
// OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
// SOFTWARE.

using System;
using System.Net;
using System.Runtime.CompilerServices;

namespace WhiteKnuckleTogether.Networking.LiteNetLib
{
    public class NetDataReader
    {
        protected byte[] _data;
        protected int _position;
        protected int _dataSize;
        protected int _offset;

        public byte[] RawData
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _data;
        }
        public int RawDataSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _dataSize;
        }
        public int UserDataOffset
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _offset;
        }
        public int UserDataSize
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _dataSize - _offset;
        }
        public bool IsNull
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _data == null;
        }
        public int Position
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _position;
        }
        public bool EndOfData
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _position == _dataSize;
        }
        public int AvailableBytes
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => _dataSize - _position;
        }

        public void SkipBytes(int count) => _position += count;

        public void SetPosition(int position) => _position = position;

        public void SetSource(NetDataWriter dataWriter)
        {
            _data = dataWriter.Data;
            _position = 0;
            _offset = 0;
            _dataSize = dataWriter.Length;
        }

        public void SetSource(byte[] source)
        {
            _data = source;
            _position = 0;
            _offset = 0;
            _dataSize = source.Length;
        }

        public void SetSource(byte[] source, int offset, int maxSize)
        {
            _data = source;
            _position = offset;
            _offset = offset;
            _dataSize = maxSize;
        }

        public NetDataReader() { }

        public NetDataReader(NetDataWriter writer) => SetSource(writer);

        public NetDataReader(byte[] source) => SetSource(source);

        public NetDataReader(byte[] source, int offset, int maxSize) => SetSource(source, offset, maxSize);

        #region GetMethods

        // WKT: every getter is bounds-checked against the message size. The reader is reused over a shared buffer, so
        // without this a read past the end of a short message silently returns stale bytes of an earlier message.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Require(int count)
        {
            if (count < 0 || _position + count > _dataSize)
                throw new IndexOutOfRangeException($"Read of {count} bytes at {_position} exceeds data size {_dataSize}.");
        }

        public void Get(out IPEndPoint result) => result = GetIPEndPoint();

        public IPEndPoint GetIPEndPoint()
        {
            IPAddress address;
            //IPv4
            if (GetByte() == 0)
            {
                Require(4);
                address = new IPAddress(new ReadOnlySpan<byte>(_data, _position, 4));
                _position += 4;
            }
            //IPv6
            else
            {
                Require(16);
                address = new IPAddress(new ReadOnlySpan<byte>(_data, _position, 16));
                _position += 16;
            }
            return new IPEndPoint(address, GetUShort());
        }

        public void Get(out byte result) => result = GetByte();
        public void Get(out sbyte result) => result = (sbyte)GetByte();
        public void Get(out bool result) => result = GetBool();
        public void Get(out char result) => result = GetChar();
        public void Get(out ushort result) => result = GetUShort();
        public void Get(out short result) => result = GetShort();
        public void Get(out ulong result) =>  result = GetULong();
        public void Get(out long result) => result = GetLong();
        public void Get(out uint result) => result = GetUInt();
        public void Get(out int result) => result = GetInt();
        public void Get(out double result) => result = GetDouble();
        public void Get(out float result) =>  result = GetFloat();
        public void Get(out string result) => result = GetString();
        public void Get(out string result, int maxLength) => result = GetString(maxLength);
        public void Get(out Guid result) => result = GetGuid();

        public byte GetByte()
        {
            Require(1);
            return _data[_position++];
        }

        public sbyte GetSByte() => (sbyte)GetByte();

        public T[] GetArray<T>(ushort size)
        {
            ushort length = GetUShort();
            Require(length * size);
            T[] result = new T[length];
            length *= size;
            Buffer.BlockCopy(_data, _position, result, 0, length);
            _position += length;
            return result;
        }

        public bool[] GetBoolArray() => GetArray<bool>(1);
        public ushort[] GetUShortArray() => GetArray<ushort>(2);
        public short[] GetShortArray() => GetArray<short>(2);
        public int[] GetIntArray() => GetArray<int>(4);
        public uint[] GetUIntArray() => GetArray<uint>(4);
        public float[] GetFloatArray() => GetArray<float>(4);
        public double[] GetDoubleArray() => GetArray<double>(8);
        public long[] GetLongArray() => GetArray<long>(8);
        public ulong[] GetULongArray() => GetArray<ulong>(8);

        public string[] GetStringArray()
        {
            ushort length = GetUShort();
            string[] arr = new string[length];
            for (int i = 0; i < length; i++)
            {
                arr[i] = GetString();
            }
            return arr;
        }

        /// <summary>
        /// Note that "maxStringLength" only limits the number of characters in a string, not its size in bytes.
        /// Strings that exceed this parameter are returned as empty
        /// </summary>
        public string[] GetStringArray(int maxStringLength)
        {
            ushort length = GetUShort();
            string[] arr = new string[length];
            for (int i = 0; i < length; i++)
            {
                arr[i] = GetString(maxStringLength);
            }
            return arr;
        }

        public bool GetBool() => GetByte() == 1;
        public char GetChar() => (char)GetUShort();

        public ushort GetUShort()
        {
            Require(2);
            ushort result = BitConverter.ToUInt16(_data, _position);
            _position += 2;
            return result;
        }

        public short GetShort()
        {
            Require(2);
            short result = BitConverter.ToInt16(_data, _position);
            _position += 2;
            return result;
        }

        public long GetLong()
        {
            Require(8);
            long result = BitConverter.ToInt64(_data, _position);
            _position += 8;
            return result;
        }

        public ulong GetULong()
        {
            Require(8);
            ulong result = BitConverter.ToUInt64(_data, _position);
            _position += 8;
            return result;
        }

        public int GetInt()
        {
            Require(4);
            int result = BitConverter.ToInt32(_data, _position);
            _position += 4;
            return result;
        }

        public uint GetUInt()
        {
            Require(4);
            uint result = BitConverter.ToUInt32(_data, _position);
            _position += 4;
            return result;
        }

        public float GetFloat()
        {
            Require(4);
            float result = BitConverter.ToSingle(_data, _position);
            _position += 4;
            return result;
        }

        public double GetDouble()
        {
            Require(8);
            double result = BitConverter.ToDouble(_data, _position);
            _position += 8;
            return result;
        }

        /// <summary>
        /// Note that "maxLength" only limits the number of characters in a string, not its size in bytes.
        /// </summary>
        /// <returns>"string.Empty" if value > "maxLength"</returns>
        public string GetString(int maxLength)
        {
            ushort size = GetUShort();
            if (size == 0)
                return string.Empty;

            int actualSize = size - 1;
            Require(actualSize);
            string result = maxLength > 0 && NetDataWriter.uTF8Encoding.GetCharCount(_data, _position, actualSize) > maxLength ?
                string.Empty :
                NetDataWriter.uTF8Encoding.GetString(_data, _position, actualSize);
            _position += actualSize;
            return result;
        }

        public string GetString()
        {
            ushort size = GetUShort();
            if (size == 0)
                return string.Empty;

            int actualSize = size - 1;
            Require(actualSize);
            string result = NetDataWriter.uTF8Encoding.GetString(_data, _position, actualSize);
            _position += actualSize;
            return result;
        }

        public string GetLargeString()
        {
            int size = GetInt();
            if (size <= 0)
                return string.Empty;
            Require(size);
            string result = NetDataWriter.uTF8Encoding.GetString(_data, _position, size);
            _position += size;
            return result;
        }

        public Guid GetGuid()
        {
            Require(16);
            var result = new Guid(_data.AsSpan(_position, 16));
            _position += 16;
            return result;
        }

        public ArraySegment<byte> GetBytesSegment(int count)
        {
            Require(count);
            ArraySegment<byte> segment = new ArraySegment<byte>(_data, _position, count);
            _position += count;
            return segment;
        }

        public ArraySegment<byte> GetRemainingBytesSegment()
        {
            ArraySegment<byte> segment = new ArraySegment<byte>(_data, _position, AvailableBytes);
            _position = _dataSize;
            return segment;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlySpan<byte> GetRemainingBytesSpan() =>
            new ReadOnlySpan<byte>(_data, _position, _dataSize - _position);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public ReadOnlyMemory<byte> GetRemainingBytesMemory() =>
            new ReadOnlyMemory<byte>(_data, _position, _dataSize - _position);

        public byte[] GetRemainingBytes()
        {
            byte[] outgoingData = new byte[AvailableBytes];
            Buffer.BlockCopy(_data, _position, outgoingData, 0, AvailableBytes);
            _position = _dataSize;
            return outgoingData;
        }

        public void GetBytes(byte[] destination, int start, int count)
        {
            Require(count);
            Buffer.BlockCopy(_data, _position, destination, start, count);
            _position += count;
        }

        public void GetBytes(byte[] destination, int count)
        {
            Require(count);
            Buffer.BlockCopy(_data, _position, destination, 0, count);
            _position += count;
        }

        public sbyte[] GetSBytesWithLength() => GetArray<sbyte>(1);
        public byte[] GetBytesWithLength() => GetArray<byte>(1);
        #endregion

        #region PeekMethods

        public byte PeekByte() => _data[_position];
        public sbyte PeekSByte() => (sbyte)_data[_position];
        public bool PeekBool() => _data[_position] == 1;
        public char PeekChar() => (char)PeekUShort();
        public ushort PeekUShort() => BitConverter.ToUInt16(_data, _position);
        public short PeekShort() => BitConverter.ToInt16(_data, _position);
        public long PeekLong() => BitConverter.ToInt64(_data, _position);
        public ulong PeekULong() => BitConverter.ToUInt64(_data, _position);
        public int PeekInt() => BitConverter.ToInt32(_data, _position);
        public uint PeekUInt() => BitConverter.ToUInt32(_data, _position);
        public float PeekFloat() => BitConverter.ToSingle(_data, _position);
        public double PeekDouble() => BitConverter.ToDouble(_data, _position);

        /// <summary>
        /// Note that "maxLength" only limits the number of characters in a string, not its size in bytes.
        /// </summary>
        public string PeekString(int maxLength)
        {
            ushort size = PeekUShort();
            if (size == 0)
                return string.Empty;

            int actualSize = size - 1;
            Require(sizeof(ushort) + actualSize);
            return (maxLength > 0 && NetDataWriter.uTF8Encoding.GetCharCount(_data, _position + 2, actualSize) > maxLength) ?
                string.Empty :
                NetDataWriter.uTF8Encoding.GetString(_data, _position + 2, actualSize);
        }

        public string PeekString()
        {
            ushort size = PeekUShort();
            if (size == 0)
                return string.Empty;

            int actualSize = size - 1;
            Require(sizeof(ushort) + actualSize);
            return NetDataWriter.uTF8Encoding.GetString(_data, _position + 2, actualSize);
        }
        #endregion

        #region TryGetMethods
        public bool TryGetByte(out byte result)
        {
            if (AvailableBytes >= 1)
            {
                result = GetByte();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetSByte(out sbyte result)
        {
            if (AvailableBytes >= 1)
            {
                result = GetSByte();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetBool(out bool result)
        {
            if (AvailableBytes >= 1)
            {
                result = GetBool();
                return true;
            }
            result = false;
            return false;
        }

        public bool TryGetChar(out char result)
        {
            if (!TryGetUShort(out ushort uShortValue))
            {
                result = '\0';
                return false;
            }
            result = (char)uShortValue;
            return true;
        }

        public bool TryGetShort(out short result)
        {
            if (AvailableBytes >= 2)
            {
                result = GetShort();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetUShort(out ushort result)
        {
            if (AvailableBytes >= 2)
            {
                result = GetUShort();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetInt(out int result)
        {
            if (AvailableBytes >= 4)
            {
                result = GetInt();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetUInt(out uint result)
        {
            if (AvailableBytes >= 4)
            {
                result = GetUInt();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetLong(out long result)
        {
            if (AvailableBytes >= 8)
            {
                result = GetLong();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetULong(out ulong result)
        {
            if (AvailableBytes >= 8)
            {
                result = GetULong();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetFloat(out float result)
        {
            if (AvailableBytes >= 4)
            {
                result = GetFloat();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetDouble(out double result)
        {
            if (AvailableBytes >= 8)
            {
                result = GetDouble();
                return true;
            }
            result = 0;
            return false;
        }

        public bool TryGetString(out string result)
        {
            if (AvailableBytes >= 2)
            {
                ushort strSize = PeekUShort();
                if (AvailableBytes >= strSize + 1)
                {
                    result = GetString();
                    return true;
                }
            }
            result = null;
            return false;
        }

        public bool TryGetStringArray(out string[] result)
        {
            if (!TryGetUShort(out ushort strArrayLength)) {
                result = null;
                return false;
            }

            result = new string[strArrayLength];
            for (int i = 0; i < strArrayLength; i++)
            {
                if (!TryGetString(out result[i]))
                {
                    result = null;
                    return false;
                }
            }

            return true;
        }

        public bool TryGetBytesWithLength(out byte[] result)
        {
            if (AvailableBytes >= 2)
            {
                ushort length = PeekUShort();
                if (length >= 0 && AvailableBytes >= 2 + length)
                {
                    result = GetBytesWithLength();
                    return true;
                }
            }
            result = null;
            return false;
        }
        #endregion

        public void Clear()
        {
            _position = 0;
            _dataSize = 0;
            _data = null;
        }
    }
}
