#if CONTENT_PROBE
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace SpatialWallpaper.ContentValidation
{
    // Experimental observation only: this does not inspect native object
    // configurations or streaming paths and MUST NOT authorize Unity loading.
    public static class NativeTypeTableProbe
    {
        public static bool TryInspect(byte[] bytes, out string reason)
        {
            try
            {
                Inspect(bytes);
                reason = "native type table only; not a content admission decision";
                return true;
            }
            catch (InvalidDataException error)
            {
                reason = error.Message;
                return false;
            }
        }

        // Deliberately accepts only the precise uncompressed UnityFS/serialized
        // format emitted by this probe. It is not a general Unity asset reader.
        const int Limit = 32 * 1024 * 1024;
        static void Inspect(byte[] bytes)
        {
            Require(bytes != null && bytes.Length <= Limit, "probe-size-limit");
            var header = new Reader(bytes, true);
            Require(header.String() == "UnityFS" && header.UInt() == 8, "unsupported-bundle-header");
            header.String();
            Require(header.String() == "2022.3.62f3", "bundle-engine-mismatch");
            Require(header.Long() == bytes.Length, "bundle-length-mismatch");
            int packedSize = header.Count(Limit), metadataSize = header.Count(Limit);
            uint flags = header.UInt();
            Require(flags == 0x243, "unsupported-bundle-flags");
            header.Align(16);
            var metadata = new Reader(Lz4(header.Bytes(packedSize), metadataSize), true);
            header.Align(16);
            metadata.Skip(16);
            int blockCount = metadata.Count(256), total = 0;
            var sizes = new List<int>();
            for (int i = 0; i < blockCount; i++)
            {
                int unpacked = metadata.Count(Limit), packed = metadata.Count(Limit);
                Require(unpacked == packed && (metadata.UShort() & 63) == 0, "compressed-resource-block-not-supported");
                Require(unpacked <= Limit - total, "resource-expansion-limit");
                total += unpacked;
                sizes.Add(unpacked);
            }
            Require(total == header.Remaining, "resource-block-length");
            var content = header.Bytes(total);
            int entryCount = metadata.Count(32), serializedCount = 0;
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            long previousEnd = 0;
            for (int i = 0; i < entryCount; i++)
            {
                long offset = metadata.Long(), length = metadata.Long();
                uint entryFlags = metadata.UInt();
                string name = metadata.String();
                Require(offset >= previousEnd && length > 0 && offset <= content.Length && length <= content.Length - offset, "invalid-resource-entry-range");
                Require(name.StartsWith("CAB-", StringComparison.Ordinal) && name.IndexOfAny(new[] { '/', '\\', ':' }) < 0 && names.Add(name), "resource-entry-name");
                previousEnd = offset + length;
                if (entryFlags == 4)
                {
                    var serialized = new byte[(int)length];
                    Array.Copy(content, (int)offset, serialized, 0, (int)length);
                    InspectSerialized(serialized);
                    serializedCount++;
                }
                else Require(entryFlags == 0 && (name.EndsWith(".resS", StringComparison.Ordinal) || name.EndsWith(".resource", StringComparison.Ordinal)), "unknown-resource-entry");
            }
            Require(serializedCount == 1 && metadata.Remaining == 0, "resource-directory-count");
        }

        static void InspectSerialized(byte[] bytes)
        {
            var file = new Reader(bytes, true);
            file.UInt(); file.UInt();
            Require(file.UInt() == 22, "serialized-version");
            file.UInt();
            Require(file.Byte() == 0, "serialized-endian");
            file.Skip(3);
            int metadataSize = file.Count(Limit);
            Require(file.Long() == bytes.Length, "serialized-file-length");
            long dataOffset = file.Long();
            file.Long();
            Require(dataOffset > file.Position && dataOffset < bytes.Length && metadataSize <= dataOffset, "serialized-data-offset");
            file.BigEndian = false;
            Require(file.String() == "2022.3.62f3" && file.UInt() == 19, "serialized-build-target");
            Require(file.Byte() == 1, "type-tree-required");
            int types = file.Count(32);
            for (int i = 0; i < types; i++)
            {
                uint classId = file.UInt();
                Require(classId == 21 || classId == 28 || classId == 43 || classId == 48 || classId == 142, "forbidden-serialized-class:" + classId);
                Require(file.Byte() == 0 && file.UShort() == 65535, "stripped-or-script-type");
                file.Skip(16);
                int nodes = file.Count(16384), strings = file.Count(Limit);
                file.Skip(nodes * 32);
                file.Skip(strings);
                int dependencies = file.Count(32);
                file.Skip(dependencies * 4);
            }
            int objects = file.Count(4096);
            var paths = new HashSet<long>();
            for (int i = 0; i < objects; i++)
            {
                file.Align(4);
                Require(paths.Add(file.Long()), "duplicate-object-id");
                long offset = file.Long();
                uint length = file.UInt(), type = file.UInt();
                Require(offset >= 0 && offset <= bytes.Length - dataOffset && length <= bytes.Length - dataOffset - offset && type < types, "serialized-object-range-or-type");
            }
            Require(file.Count(0) == 0, "script-identifiers-not-allowed");
            Require(file.Count(0) == 0, "external-resource-references-not-allowed");
            Require(file.Count(0) == 0, "managed-reference-types-not-allowed");
            Require(file.String() == "" && file.Position <= dataOffset, "serialized-metadata-tail");
        }

        static byte[] Lz4(byte[] source, int length)
        {
            var input = new Reader(source, false);
            var output = new byte[length];
            int position = 0;
            while (input.Remaining > 0)
            {
                int token = input.Byte(), literals = Extended(input, token >> 4);
                Require(literals <= length - position, "lz4-literal-range");
                var bytes = input.Bytes(literals);
                Array.Copy(bytes, 0, output, position, literals);
                position += literals;
                if (input.Remaining == 0) break;
                int distance = input.UShort(), match = Extended(input, token & 15) + 4;
                Require(distance > 0 && distance <= position && match <= length - position, "lz4-match-range");
                for (int i = 0; i < match; i++) { output[position] = output[position - distance]; position++; }
            }
            Require(position == length, "lz4-decoded-length");
            return output;
        }

        static int Extended(Reader input, int value)
        {
            if (value != 15) return value;
            int next;
            do { next = input.Byte(); Require(value <= Limit - next, "lz4-length-limit"); value += next; } while (next == 255);
            return value;
        }

        static void Require(bool valid, string reason) { if (!valid) throw new InvalidDataException(reason); }

        sealed class Reader
        {
            readonly byte[] bytes;
            public int Position;
            public bool BigEndian;
            public int Remaining { get { return bytes.Length - Position; } }
            public Reader(byte[] bytes, bool bigEndian) { this.bytes = bytes; BigEndian = bigEndian; }
            public byte Byte() { Require(Remaining > 0, "truncated-data"); return bytes[Position++]; }
            public ushort UShort() { uint a = Byte(), b = Byte(); return (ushort)(BigEndian ? (a << 8) | b : a | (b << 8)); }
            public uint UInt() { uint a = UShort(), b = UShort(); return BigEndian ? (a << 16) | b : a | (b << 16); }
            public long Long() { ulong a = UInt(), b = UInt(); return (long)(BigEndian ? (a << 32) | b : a | (b << 32)); }
            public int Count(int limit) { uint count = UInt(); Require(count <= limit, "count-limit"); return (int)count; }
            public void Skip(int length) { Require(length >= 0 && length <= Remaining, "truncated-data"); Position += length; }
            public void Align(int alignment) { Skip((alignment - Position % alignment) % alignment); }
            public byte[] Bytes(int length) { Require(length >= 0 && length <= Remaining, "truncated-data"); var result = new byte[length]; Array.Copy(bytes, Position, result, 0, length); Position += length; return result; }
            public string String()
            {
                int start = Position;
                while (Byte() != 0) Require(Position - start < 256, "string-limit");
                return Encoding.UTF8.GetString(bytes, start, Position - start - 1);
            }
        }
    }
}

#endif
