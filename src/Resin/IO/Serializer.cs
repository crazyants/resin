using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Resin.IO
{
    public static class Serializer
    {
        private static readonly Encoding Enc = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        private static readonly Dictionary<bool, byte> EncodedBoolean = new Dictionary<bool, byte> { { true, 1 }, { false, 0 } };

        public static void Serialize(this IxInfo ix, string fileName)
        {
            using (var fs = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = ix.Serialize();
                fs.Write(bytes, 0, bytes.Length);
            }
        }

        public static void Serialize(this LcrsTrie trie, string fileName)
        {
            using (var stream = new FileStream(fileName, FileMode.Create, FileAccess.Write, FileShare.None, 4800))
            {
                if (trie.LeftChild != null)
                {
                    trie.LeftChild.SerializeDepthFirst(stream, 0);
                }
            }
        }

        private static void SerializeDepthFirst(this LcrsTrie trie, Stream stream, int depth)
        {
            var bytes = new LcrsNode(trie, depth, trie.GetWeight(), trie.PostingsAddress).Serialize();

            stream.Write(bytes, 0, bytes.Length);

            if (trie.LeftChild != null)
            {
                trie.LeftChild.SerializeDepthFirst(stream, depth + 1);
            }

            if (trie.RightSibling != null)
            {
                trie.RightSibling.SerializeDepthFirst(stream, depth);
            }
        }

        public static int SizeOfNode()
        {
            return sizeof(char) + 3*sizeof (bool) + 2*sizeof (int) + SizeOfBlock();
        }

        public static int SizeOfBlock()
        {
            return sizeof(long) + sizeof(int);
        }

        public static byte[] Serialize(this LcrsNode node)
        {
            using (var stream = new MemoryStream())
            {
                var bytes0 = BitConverter.GetBytes(node.Value);
                var byte0 = EncodedBoolean[node.HaveSibling];
                var byte1 = EncodedBoolean[node.HaveChild];
                var byte2 = EncodedBoolean[node.EndOfWord];
                var bytes1 = BitConverter.GetBytes(node.Depth);
                var bytes2 = BitConverter.GetBytes(node.Weight);
                var bytes3 = Serialize(node.PostingsAddress);

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes0);
                    Array.Reverse(bytes1);
                    Array.Reverse(bytes2);
                    Array.Reverse(bytes3);
                }

                stream.Write(bytes0, 0, bytes0.Length);
                stream.WriteByte(byte0);
                stream.WriteByte(byte1);
                stream.WriteByte(byte2);
                stream.Write(bytes1, 0, bytes1.Length);
                stream.Write(bytes2, 0, bytes2.Length);
                stream.Write(bytes3, 0, bytes3.Length);

                return stream.ToArray();
            }
        }

        public static byte[] Serialize(this BlockInfo? block)
        {
            using (var stream = new MemoryStream())
            {
                byte[] longBytes;
                byte[] intBytes;

                if (block == null)
                {
                    longBytes = BitConverter.GetBytes(long.MinValue);
                    intBytes = BitConverter.GetBytes(int.MinValue);
                }
                else
                {
                    longBytes = BitConverter.GetBytes(block.Value.Position);
                    intBytes = BitConverter.GetBytes(block.Value.Length);
                }

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(longBytes);
                    Array.Reverse(intBytes);
                }

                stream.Write(longBytes, 0, longBytes.Length);
                stream.Write(intBytes, 0, intBytes.Length);

                return stream.ToArray();
            }
        }

        public static byte[] Serialize(this Document document)
        {
            using (var stream = new MemoryStream())
            {
                var intBytes = BitConverter.GetBytes(document.Id);
                var dicBytes = document.Fields.Serialize();

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(intBytes);
                    Array.Reverse(dicBytes);
                }

                stream.Write(intBytes, 0, intBytes.Length);
                stream.Write(dicBytes, 0, dicBytes.Length);

                return stream.ToArray();
            }
        }

        public static byte[] Serialize(this BlockInfo block)
        {
            using (var stream = new MemoryStream())
            {
                byte[] longBytes = BitConverter.GetBytes(block.Position);
                byte[] intBytes = BitConverter.GetBytes(block.Length);

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(longBytes);
                    Array.Reverse(intBytes);
                }

                stream.Write(longBytes, 0, longBytes.Length);
                stream.Write(intBytes, 0, intBytes.Length);

                return stream.ToArray();
            }
        }

        public static byte[] Serialize(this IxInfo ix)
        {
            using (var stream = new MemoryStream())
            {
                byte[] stringBytes = Enc.GetBytes(ix.Name);
                byte[] lengthBytes = BitConverter.GetBytes((short)stringBytes.Length);
                byte[] dicBytes = ix.DocumentCount.Serialize();
                
                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(stringBytes);
                    Array.Reverse(lengthBytes);
                    Array.Reverse(dicBytes);
                }

                stream.Write(lengthBytes, 0, sizeof(short));
                stream.Write(stringBytes, 0, stringBytes.Length);
                stream.Write(dicBytes, 0, dicBytes.Length);

                return stream.ToArray();
            }
        }

        public static IxInfo DeserializeIxInfo(Stream stream)
        {
            var lengthBytes = new byte[sizeof(short)];

            stream.Read(lengthBytes, 0, sizeof(short));

            var stringLength = BitConverter.ToInt16(lengthBytes, 0);

            var stringBytes = new byte[stringLength];

            stream.Read(stringBytes, 0, stringLength);

            var dic = DeserializeStringIntDic(stream).ToList();

            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(stringBytes);
                Array.Reverse(lengthBytes);
            }

            return new IxInfo{Name=Enc.GetString(stringBytes), DocumentCount = dic.ToDictionary(x=>x.Key, x=>x.Value)};
        }

        public static byte[] Serialize(this IEnumerable<KeyValuePair<string, int>> entries)
        {
            byte[] keyBytes;
            byte[] lengthBytes;
            byte[] intBytes;

            using (var stream = new MemoryStream())
            {
                foreach (var entry in entries)
                {
                    keyBytes = Enc.GetBytes(entry.Key);
                    lengthBytes = BitConverter.GetBytes((short)keyBytes.Length);
                    intBytes = BitConverter.GetBytes(entry.Value);

                    if (!BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(lengthBytes);
                        Array.Reverse(keyBytes);
                        Array.Reverse(intBytes);
                    }

                    stream.Write(lengthBytes, 0, sizeof(short));
                    stream.Write(keyBytes, 0, keyBytes.Length);
                    stream.Write(intBytes, 0, sizeof(int));
                }
                return stream.ToArray();
            }
        }

        public static byte[] Serialize(this IEnumerable<KeyValuePair<string, string>> entries)
        {
            byte[] keyBytes;
            byte[] keyLengthBytes;
            byte[] valBytes;
            byte[] valLengthBytes;

            using (var stream = new MemoryStream())
            {
                foreach (var entry in entries)
                {
                    keyBytes = Enc.GetBytes(entry.Key);
                    keyLengthBytes = BitConverter.GetBytes((short)keyBytes.Length);
                    valBytes = Enc.GetBytes(entry.Value??string.Empty);
                    valLengthBytes = BitConverter.GetBytes(valBytes.Length);

                    if (!BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(keyLengthBytes);
                        Array.Reverse(keyBytes);
                        Array.Reverse(valBytes);
                        Array.Reverse(valLengthBytes);
                    }

                    stream.Write(keyLengthBytes, 0, sizeof(short));
                    stream.Write(keyBytes, 0, keyBytes.Length);
                    stream.Write(valLengthBytes, 0, sizeof(int));
                    stream.Write(valBytes, 0, valBytes.Length);
                }
                return stream.ToArray();
            }
        }

        public static byte[] Serialize(this IEnumerable<DocumentPosting> entries)
        {
            byte[] idBytes;
            byte[] countBytes;

            using (var stream = new MemoryStream())
            {
                foreach (var entry in entries)
                {
                    idBytes = BitConverter.GetBytes(entry.DocumentId);
                    countBytes = BitConverter.GetBytes(entry.Count);

                    if (!BitConverter.IsLittleEndian)
                    {
                        Array.Reverse(idBytes);
                        Array.Reverse(countBytes);
                    }

                    stream.Write(idBytes, 0, sizeof(int));
                    stream.Write(countBytes, 0, sizeof(int));
                }
                return stream.ToArray();
            }
        }

        public static LcrsNode DeserializeNode(Stream stream)
        {
            if (!stream.CanRead) return LcrsNode.MinValue;

            var bytes0 = new byte[sizeof(char)];
            var bytes1 = new byte[sizeof(int)];
            var bytes2 = new byte[sizeof(int)];

            stream.Read(bytes0, 0, sizeof (char));
            int byte0 = stream.ReadByte();
            int byte1 = stream.ReadByte();
            int byte2 = stream.ReadByte();
            stream.Read(bytes1, 0, sizeof(int));
            stream.Read(bytes2, 0, sizeof(int));
            BlockInfo block = DeserializeBlock(stream);
            
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(bytes0);
                Array.Reverse(bytes1);
                Array.Reverse(bytes2);
            }

            return new LcrsNode(
                BitConverter.ToChar(bytes0, 0),
                byte0 == 1,
                byte1 == 1,
                byte2 == 1,
                BitConverter.ToInt32(bytes1, 0),
                BitConverter.ToInt32(bytes2, 0),
                block);
        }

        public static BlockInfo DeserializeBlock(byte[] bytes)
        {
            using (var stream = new MemoryStream(bytes))
            {
                return DeserializeBlock(stream);
            }
        }

        public static Document DeserializeDocument(byte[] data)
        {
            var idBytes = new byte[sizeof(int)];
            Array.Copy(data, 0, idBytes, 0, sizeof(int));

            var dicBytes = new byte[data.Length - sizeof(int)];
            Array.Copy(data, sizeof(int), dicBytes, 0, dicBytes.Length);
            
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(idBytes);
                Array.Reverse(dicBytes);
            }

            var id = BitConverter.ToInt32(idBytes, 0);
            var dic = DeserializeStringStringDic(dicBytes).ToDictionary(x=>x.Key, y=>y.Value);

            return new Document(dic) {Id = id};
        }

        public static BlockInfo DeserializeBlock(Stream stream)
        {
            var longBytes = new byte[sizeof(long)];
            var intBytes = new byte[sizeof(int)];

            stream.Read(longBytes, 0, sizeof (long));
            stream.Read(intBytes, 0, sizeof (int));

            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(longBytes);
                Array.Reverse(intBytes);
            }

            return new BlockInfo(BitConverter.ToInt64(longBytes, 0), BitConverter.ToInt32(intBytes, 0));
        }

        public static IEnumerable<DocumentPosting> DeserializePostings(byte[] data)
        {
            byte[] chunk = new byte[sizeof(int)*2];
            long pos = 0;

            while (pos<data.Length)
            {
                Array.Copy(data, pos, chunk, 0, chunk.Length);

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(chunk);
                }

                yield return new DocumentPosting(
                    BitConverter.ToInt32(chunk, 0),
                    BitConverter.ToInt32(chunk, sizeof(int)));

                pos = pos + chunk.Length;
            }
        }

        public static IEnumerable<KeyValuePair<string, string>> DeserializeStringStringDic(byte [] data)
        {
            using (var stream = new MemoryStream(data))
            {
                return DeserializeStringStringDic(stream).ToList();
            }
        }

        public static IEnumerable<KeyValuePair<string, string>> DeserializeStringStringDic(Stream stream)
        {
            byte[] keyLengthBytes = new byte[sizeof(short)];
            byte[] valLengthBytes = new byte[sizeof(int)];

            while (true)
            {
                var read = stream.Read(keyLengthBytes, 0, sizeof(short));

                if (read == 0) break;

                short keyLength = BitConverter.ToInt16(keyLengthBytes, 0);

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(keyLengthBytes);
                }

                byte[] keyBytes = new byte[keyLength];

                stream.Read(keyBytes, 0, keyLength);

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(keyBytes);
                }

                string key = Enc.GetString(keyBytes);

                stream.Read(valLengthBytes, 0, sizeof(int));

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(valLengthBytes);
                }

                int valLength = BitConverter.ToInt32(valLengthBytes, 0);

                byte[] valBytes = new byte[valLength];

                stream.Read(valBytes, 0, valLength);

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(valBytes);
                }

                string value = Enc.GetString(valBytes);

                yield return new KeyValuePair<string, string>(key, value);
            }
        }

        public static IEnumerable<KeyValuePair<string, int>> DeserializeStringIntDic(Stream stream)
        {
            byte[] keyBytes;
            byte[] lengthBytes = new byte[sizeof(short)];
            byte[] intBytes = new byte[sizeof(int)];
            short keyLength;
            string key;
            int value;

            while (true)
            {
                var read = stream.Read(lengthBytes, 0, sizeof (short));

                if (read == 0) break;

                keyLength = BitConverter.ToInt16(lengthBytes, 0);

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(lengthBytes);
                }

                keyBytes = new byte[keyLength];

                stream.Read(keyBytes, 0, keyLength);

                key = Enc.GetString(keyBytes);

                stream.Read(intBytes, 0, sizeof (int));

                if (!BitConverter.IsLittleEndian)
                {
                    Array.Reverse(intBytes);
                }

                value = BitConverter.ToInt32(intBytes, 0);
                
                yield return new KeyValuePair<string, int>(key, value);
            }
        }
    }
}