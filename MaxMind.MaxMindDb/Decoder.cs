﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace MaxMind.MaxMindDb
{
    public enum ObjectType
    {
        Extended, Pointer, Utf8String, Double, Bytes, Uint16, Uint32, Map, Int32, Uint64, Uint128, Array, Container, EndMarker, Boolean, Float
    }

    public class Result
    {
        public JToken Node { get; set; }

        public int Offset { get; set; }

        public Result(JToken node, int offset)
        {
            Node = node;
            Offset = offset;
        }
    }
    public class Decoder
    {
        #region Private

        private Stream fs = null;

        private long pointerBase = -1;

        private int[] pointerValueOffset = { 0, 0, 1 << 11, (1 << 19) + ((1) << 11), 0 };

        #endregion

        public Decoder(Stream stream, long pointerBase)
        {
            this.pointerBase = pointerBase;
            this.fs = stream;
        }

        /// <summary>
        /// Decodes the specified offset.
        /// </summary>
        /// <param name="offset">The offset.</param>
        /// <returns></returns>
        public Result Decode(int offset)
        {
            int ctrlByte = 0xFF & ReadOne(offset);
            offset++;

            ObjectType type = FromControlByte(ctrlByte);

            if (type == ObjectType.Pointer)
            {
                Result pointer = this.decodePointer(ctrlByte, offset);
                Result result = this.Decode(Convert.ToInt32(pointer.Node.Value<int>()));
                result.Offset = pointer.Offset;
                return result;
            }

            if (type == ObjectType.Extended)
            {
                int nextByte = ReadOne(offset);
                int typeNum = nextByte + 7;
                type = (ObjectType)typeNum;
                offset++;
            }

            int[] sizeArray = this.SizeFromCtrlByte(ctrlByte, offset);
            int size = sizeArray[0];
            offset = sizeArray[1];

            return DecodeByType(type, offset, size);
        }

        #region Private

        /// <summary>
        /// Reads the one.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <returns></returns>
        private int ReadOne(int position)
        {
            lock (fs)
            {
                fs.Seek(position, SeekOrigin.Begin);
                return fs.ReadByte();
            }
        }

        /// <summary>
        /// Reads the many.
        /// </summary>
        /// <param name="position">The position.</param>
        /// <param name="size">The size.</param>
        /// <returns></returns>
        private byte[] ReadMany(int position, int size)
        {
            lock (fs)
            {
                byte[] buffer = new byte[size];
                fs.Seek(position, SeekOrigin.Begin);
                fs.Read(buffer, 0, buffer.Length);
                return buffer;
            }
        }

        /// <summary>
        /// Decodes the type of the by.
        /// </summary>
        /// <param name="type">The type.</param>
        /// <param name="offset">The offset.</param>
        /// <param name="size">The size.</param>
        /// <returns></returns>
        /// <exception cref="System.Exception">Unable to handle type!</exception>
        private Result DecodeByType(ObjectType type, int offset, int size)
        {
            int new_offset = offset + size;
            byte[] buffer = ReadMany(offset, size);

            switch (type)
            {
                case ObjectType.Map:
                    return decodeMap(size, offset);
                case ObjectType.Array:
                    return decodeArray(size, offset);
                case ObjectType.Boolean:
                    return new Result(decodeBoolean(buffer), offset);
                case ObjectType.Utf8String:
                    return new Result(decodeString(buffer), new_offset);
                case ObjectType.Double:
                    return new Result(decodeDouble(buffer), new_offset);
                case ObjectType.Float:
                    return new Result(decodeFloat(buffer), new_offset);
                case ObjectType.Bytes:
                    return new Result(new JValue(buffer), new_offset);
                case ObjectType.Uint16:
                    return new Result(decodeInteger(buffer), new_offset);
                case ObjectType.Uint32:
                    return new Result(decodeLong(buffer), new_offset);
                case ObjectType.Int32:
                    return new Result(decodeInteger(buffer), new_offset);
                case ObjectType.Uint64:
                    return new Result(decodeUint64(buffer), new_offset);
                case ObjectType.Uint128:
                    return new Result(decodeBigInteger(buffer), new_offset);
                default:
                    throw new Exception("Unable to handle type!");
            }
        }

        /// <summary>
        /// Froms the control byte.
        /// </summary>
        /// <param name="b">The attribute.</param>
        /// <returns></returns>
        private ObjectType FromControlByte(int b)
        {
            int p = ((0xFF & b) >> 5);
            return (ObjectType) p;
        }

        /// <summary>
        /// Sizes from control byte.
        /// </summary>
        /// <param name="ctrlByte">The control byte.</param>
        /// <param name="offset">The offset.</param>
        /// <returns></returns>
        private int[] SizeFromCtrlByte(int ctrlByte, int offset)
        {
            int size = ctrlByte & 0x1f;
            int bytesToRead = size < 29 ? 0 : size - 28;

            if (size == 29) {
                byte[] buffer = ReadMany(offset, bytesToRead);
                int i = this.decodeInteger(buffer).Value<int>();
                size = 29 + i;
            } else if (size == 30) {
                byte[] buffer = ReadMany(offset, bytesToRead);
                int i = this.decodeInteger(buffer).Value<int>();
                size = 285 + i;
            }
            else if (size > 30)
            {
                byte[] buffer = ReadMany(offset, bytesToRead);
                int i = this.decodeInteger(buffer).Value<int>() & (0x0FFFFFFF >> (32 - (8 * bytesToRead)));
                size = 65821 + i;
            }

            return new int[] { size, offset + bytesToRead };
        }

        #region Convert

        /// <summary>
        /// Decodes the boolean.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        private JValue decodeBoolean(byte[] buffer)
        {
            return new JValue(BitConverter.ToBoolean(buffer, 0));
        }

        /// <summary>
        /// Decodes the double.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        private JValue decodeDouble(byte[] buffer)
        {
            return new JValue(BitConverter.ToDouble(buffer, 0));
        }

        /// <summary>
        /// Decodes the float.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        private JValue decodeFloat(byte[] buffer)
        {
            return new JValue(BitConverter.ToSingle(buffer, 0));
        }

        /// <summary>
        /// Decodes the string.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        private JValue decodeString(byte[] buffer)
        {
            return new JValue(Encoding.UTF8.GetString(buffer));
        }

        /// <summary>
        /// Decodes the map.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="offset">The offset.</param>
        /// <returns></returns>
        private Result decodeMap(int size, int offset)
        {
            var obj = new JObject();
            JToken key = null;
            JToken value = null;

            for (int i = 0; i < size; i++)
            {
                Result left = this.Decode(offset);
                key = left.Node;
                offset = left.Offset;
                Result right = this.Decode(offset);
                value = right.Node;
                offset = right.Offset;
                obj.Add((string)key, value);
            }

            return new Result(obj, offset);
        }

        /// <summary>
        /// Decodes the long.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        private JValue decodeLong(byte[] buffer)
        {
            long integer = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                integer = (integer << 8) | (buffer[i] & 0xFF);
            }
            return new JValue(integer);
        }

        /// <summary>
        /// Decodes the integer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        private JValue decodeInteger(byte[] buffer)
        {
            int integer = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                integer = (integer << 8) | (buffer[i] & 0xFF);
            }
            return new JValue(integer);
        }

        /// <summary>
        /// Decodes the integer.
        /// </summary>
        /// <param name="b">The attribute.</param>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        private int decodeInteger(int b, byte[] buffer)
        {
            int integer = b;
            for (int i = 0; i < buffer.Length; i++)
            {
                integer = (integer << 8) | (buffer[i] & 0xFF);
            }
            return integer;
        }

        /// <summary>
        /// Decodes the array.
        /// </summary>
        /// <param name="size">The size.</param>
        /// <param name="offset">The offset.</param>
        /// <returns></returns>
        private Result decodeArray(int size, int offset)
        {
            var array = new JArray();

            for (int i = 0; i < size; i++)
            {
                Result r = this.Decode(offset);
                offset = r.Offset;
                array.Add(r.Node);
            }

            return new Result(array, offset);
        }

        /// <summary>
        /// Decodes the uint64.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        private JValue decodeUint64(byte[] buffer)
        {
            return new JValue(BigInteger.ToInt64(new BigInteger(buffer)));
        }

        /// <summary>
        /// Decodes the big integer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        private JToken decodeBigInteger(byte[] buffer)
        {
            return JObject.FromObject(new BigInteger(buffer));
        }

        /// <summary>
        /// Decodes the pointer.
        /// </summary>
        /// <param name="ctrlByte">The control byte.</param>
        /// <param name="offset">The offset.</param>
        /// <returns></returns>
        private Result decodePointer(int ctrlByte, int offset)
        {
            int pointerSize = ((ctrlByte >> 3) & 0x3) + 1;
            int b = pointerSize == 4 ? (byte)0 : (byte)(ctrlByte & 0x7);
            byte[] buffer = ReadMany(offset, pointerSize);
            int packed = this.decodeInteger(b, buffer);
            long pointer = packed + this.pointerBase + this.pointerValueOffset[pointerSize];
            return new Result(new JValue(pointer), offset + pointerSize);
        }

        #endregion

        #endregion

        #region static

        /// <summary>
        /// Decodes the integer.
        /// </summary>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        public static int DecodeInteger(byte[] buffer)
        {
            int integer = 0;
            for (int i = 0; i < buffer.Length; i++)
            {
                integer = (integer << 8) | (buffer[i] & 0xFF);
            }
            return integer;
        }

        /// <summary>
        /// Decodes the integer.
        /// </summary>
        /// <param name="baseValue">The base value.</param>
        /// <param name="buffer">The buffer.</param>
        /// <returns></returns>
        public static int DecodeInteger(int baseValue, byte[] buffer)
        {
            int integer = baseValue;
            for (int i = 0; i < buffer.Length; i++)
            {
                integer = (integer << 8) | (buffer[i] & 0xFF);
            }
            return integer;
        }

        #endregion
    }
}