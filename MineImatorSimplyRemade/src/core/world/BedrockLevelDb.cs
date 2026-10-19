using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;

namespace MineImatorSimplyRemade.core.world;

/// <summary>
/// Managed, read-only LevelDB snapshot reader. Only CURRENT's live tables and recoverable
/// journals are read; no LOCK, native library, recovery, or database writes are involved.
/// The world must be closed (or supplied as a consistent filesystem copy).
/// Format references: github.com/google/leveldb/doc/{table_format,log_format}.md.
/// </summary>
internal sealed class BedrockLevelDb : IDisposable
{
    private const int MaxBlock = 64 * 1024 * 1024;
    private const int MaxJournal = 512 * 1024 * 1024;
    private const ulong MaxSequence = (1UL << 56) - 1;
    private readonly string directory;
    private readonly CancellationToken cancellation;
    private readonly Dictionary<ulong, Table> tables = new();
    private readonly Dictionary<string, Entry> journal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (long Length, DateTime Modified)> watched = new();
    private readonly string current;
    private ulong recoveryLog;
    private ulong previousRecoveryLog;
    private bool disposed;

    private sealed record Table(ulong Number, ulong Size, byte[] Smallest, byte[] Largest);
    private sealed record Entry(byte[] Key, byte[]? Value, ulong Sequence);
    private readonly record struct Handle(ulong Offset, ulong Size);

    public BedrockLevelDb(string directory, CancellationToken cancellation)
    {
        this.directory = Path.GetFullPath(directory);
        this.cancellation = cancellation;
        cancellation.ThrowIfCancellationRequested();
        current = Encoding.UTF8.GetString(ReadFile(Path.Combine(this.directory, "CURRENT"), 4096));
        string manifestName = current.TrimEnd('\r', '\n');
        if (!manifestName.StartsWith("MANIFEST-", StringComparison.Ordinal) ||
            !ulong.TryParse(manifestName.AsSpan(9), NumberStyles.None, CultureInfo.InvariantCulture, out _))
            throw Corrupt("CURRENT does not name a MANIFEST.");

        ulong logNumber = 0, previousLog = 0;
        bool comparatorSeen = false, sequenceSeen = false, logSeen = false;
        foreach (byte[] record in LogRecords(ReadFile(Path.Combine(this.directory, manifestName), MaxJournal)))
        {
            var r = new Cursor(record);
            var deleted = new List<ulong>();
            var added = new List<Table>();
            while (r.Remaining > 0)
            {
                switch (r.Varint())
                {
                    case 1:
                        if (Encoding.UTF8.GetString(r.Slice()) != "leveldb.BytewiseComparator")
                            throw new NotSupportedException("Unsupported Bedrock LevelDB comparator.");
                        comparatorSeen = true;
                        break;
                    case 2: logNumber = r.Varint(); logSeen = true; break;
                    case 3: r.Varint(); break; // Next file number.
                    case 4:
                        if (r.Varint() > MaxSequence) throw Corrupt("Invalid manifest sequence.");
                        sequenceSeen = true;
                        break;
                    case 5: ReadLevel(r); ValidateInternalKey(r.Slice()); break;
                    case 6: ReadLevel(r); deleted.Add(r.Varint()); break;
                    case 7:
                        ReadLevel(r);
                        ulong number = r.Varint(), size = r.Varint();
                        byte[] smallest = r.Slice(), largest = r.Slice();
                        ValidateInternalKey(smallest);
                        ValidateInternalKey(largest);
                        if (CompareUser(smallest, largest) > 0 || size < 48)
                            throw Corrupt("Invalid manifest table range or size.");
                        added.Add(new Table(number, size, smallest[..^8], largest[..^8]));
                        break;
                    case 9: previousLog = r.Varint(); break;
                    default: throw new NotSupportedException("Unsupported LevelDB manifest edit tag.");
                }
            }
            foreach (ulong number in deleted) tables.Remove(number);
            foreach (Table table in added) tables[table.Number] = table;
        }
        if (!comparatorSeen || !sequenceSeen || !logSeen)
            throw Corrupt("Incomplete LevelDB manifest.");
        recoveryLog = logNumber;
        previousRecoveryLog = previousLog;

        var logs = new SortedDictionary<ulong, string>();
        foreach (string file in Directory.EnumerateFiles(this.directory, "*.log"))
        {
            cancellation.ThrowIfCancellationRequested();
            if (ulong.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None,
                CultureInfo.InvariantCulture, out ulong number) && (number >= logNumber || number == previousLog))
                logs.Add(number, file);
        }
        foreach (string file in logs.Values)
            foreach (byte[] batch in LogRecords(ReadFile(file, MaxJournal)))
                ReadBatch(batch);
        CheckUnchanged();
    }

    public IEnumerable<(byte[] Key, byte[] Value)> ReadPrefix(byte[] prefix)
    {
        ObjectDisposedException.ThrowIf(disposed, this);
        cancellation.ThrowIfCancellationRequested();
        CheckUnchanged();
        byte[]? end = PrefixEnd(prefix);
        var result = new Dictionary<string, Entry>(StringComparer.Ordinal);
        foreach (Table table in tables.Values)
        {
            cancellation.ThrowIfCancellationRequested();
            if (Compare(table.Largest, prefix) < 0 || (end != null && Compare(table.Smallest, end) >= 0))
                continue;
            ReadTable(table, prefix, end, result);
        }
        foreach (Entry entry in journal.Values)
        {
            cancellation.ThrowIfCancellationRequested();
            if (entry.Key.AsSpan().StartsWith(prefix)) Merge(result, entry);
        }
        CheckUnchanged();
        return result.Values.Where(e => e.Value != null)
            .OrderBy(e => Convert.ToHexString(e.Key), StringComparer.Ordinal)
            .Select(e => (e.Key, e.Value!)).ToArray();
    }

    private void ReadTable(Table table, byte[] prefix, byte[]? end, Dictionary<string, Entry> result)
    {
        string file = Path.Combine(directory, table.Number.ToString("D6", CultureInfo.InvariantCulture) + ".ldb");
        if (!File.Exists(file)) file = Path.ChangeExtension(file, ".sst");
        using var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if ((ulong)stream.Length != table.Size) throw Corrupt($"Table {table.Number} has an unexpected length.");
        byte[] footer = ReadAt(stream, stream.Length - 48, 48);
        if (BinaryPrimitives.ReadUInt64LittleEndian(footer.AsSpan(40)) != 0xdb4775248b80fb57UL)
            throw new NotSupportedException($"Unsupported LevelDB table magic in {Path.GetFileName(file)}.");
        var f = new Cursor(footer[..40]);
        ReadHandle(f); // Metaindex is not required to read user data.
        Handle index = ReadHandle(f);
        byte[]? previous = null;
        foreach (var pair in BlockEntries(ReadBlock(stream, index)))
        {
            ValidateInternalKey(pair.Key);
            byte[] upper = pair.Key[..^8];
            if (Compare(upper, prefix) >= 0 && (end == null || previous == null || Compare(previous, end) < 0))
            {
                var h = new Cursor(pair.Value);
                Handle data = ReadHandle(h);
                if (h.Remaining != 0) throw Corrupt("Trailing bytes in table block handle.");
                foreach (var item in BlockEntries(ReadBlock(stream, data)))
                {
                    ValidateInternalKey(item.Key);
                    byte[] user = item.Key[..^8];
                    if (!user.AsSpan().StartsWith(prefix)) continue;
                    ulong trailer = BinaryPrimitives.ReadUInt64LittleEndian(item.Key.AsSpan(item.Key.Length - 8));
                    byte type = (byte)trailer;
                    if (type > 1) throw new NotSupportedException($"Unsupported LevelDB value type {type}.");
                    if (type == 0 && item.Value.Length != 0) throw Corrupt("Nonempty LevelDB deletion value.");
                    Merge(result, new Entry(user, type == 0 ? null : item.Value, trailer >> 8));
                }
            }
            previous = upper;
            if (end != null && Compare(upper, end) >= 0) break;
        }
    }

    private byte[] ReadBlock(FileStream stream, Handle handle)
    {
        cancellation.ThrowIfCancellationRequested();
        if (handle.Size > MaxBlock || handle.Offset > (ulong)stream.Length ||
            handle.Size + 5 > (ulong)stream.Length - handle.Offset)
            throw Corrupt("LevelDB block handle outside table or safety limit.");
        byte[] bytes = ReadAt(stream, (long)handle.Offset, checked((int)handle.Size + 5));
        int size = (int)handle.Size;
        uint stored = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(size + 1));
        if (Mask(Crc(bytes.AsSpan(0, size + 1))) != stored) throw Corrupt("LevelDB table block checksum mismatch.");
        byte kind = bytes[size];
        if (kind == 0) return bytes[..size];
        if (kind == 1) return Snappy(bytes.AsSpan(0, size));
        if (kind is not (2 or 4))
            throw new NotSupportedException($"Unsupported LevelDB compression type {kind}.");
        using var input = new MemoryStream(bytes, 0, size, false);
        using Stream compressed = kind == 2
            ? new ZLibStream(input, CompressionMode.Decompress)
            : new DeflateStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        byte[] buffer = new byte[8192];
        int count;
        while ((count = compressed.Read(buffer, 0, buffer.Length)) != 0)
        {
            cancellation.ThrowIfCancellationRequested();
            if (output.Length + count > MaxBlock) throw Corrupt("Decompressed LevelDB block exceeds safety limit.");
            output.Write(buffer, 0, count);
        }
        return output.ToArray();
    }

    private IEnumerable<(byte[] Key, byte[] Value)> BlockEntries(byte[] bytes)
    {
        if (bytes.Length < 8) throw Corrupt("Truncated LevelDB restart block.");
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 4));
        if (count == 0 || count > (bytes.Length - 4) / 4) throw Corrupt("Invalid LevelDB restart count.");
        int limit = bytes.Length - 4 - checked((int)count * 4);
        var restarts = new HashSet<int>();
        uint last = 0;
        for (int i = 0; i < count; i++)
        {
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(limit + i * 4));
            if ((i == 0 && offset != 0) || (i > 0 && offset <= last) || offset > limit ||
                (offset == limit && !(limit == 0 && count == 1)))
                throw Corrupt("Invalid LevelDB restart offset.");
            restarts.Add((int)offset);
            last = offset;
        }
        var r = new Cursor(bytes, limit);
        byte[] previous = Array.Empty<byte>();
        while (r.Remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            int start = r.Position;
            int shared = r.Length(), unshared = r.Length(), size = r.Length();
            if (shared > previous.Length || (restarts.Contains(start) && shared != 0) ||
                shared + (long)unshared > MaxBlock)
                throw Corrupt("Invalid LevelDB shared key prefix.");
            restarts.Remove(start);
            byte[] key = new byte[shared + unshared];
            previous.AsSpan(0, shared).CopyTo(key);
            r.Bytes(unshared).CopyTo(key, shared);
            byte[] value = r.Bytes(size);
            previous = key;
            yield return (key, value);
        }
        if (limit != 0 && restarts.Count != 0) throw Corrupt("Restart offset is not an entry boundary.");
    }

    private void ReadBatch(byte[] bytes)
    {
        if (bytes.Length < 12) throw Corrupt("Truncated LevelDB write batch.");
        ulong sequence = BinaryPrimitives.ReadUInt64LittleEndian(bytes);
        uint count = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(8));
        if (sequence > MaxSequence || count > bytes.Length - 12 ||
            (count > 0 && sequence > MaxSequence - (count - 1)))
            throw Corrupt("Invalid LevelDB write batch sequence/count.");
        var r = new Cursor(bytes) { Position = 12 };
        for (uint i = 0; i < count; i++)
        {
            cancellation.ThrowIfCancellationRequested();
            byte type = r.Byte();
            if (type > 1) throw new NotSupportedException($"Unsupported LevelDB write batch type {type}.");
            byte[] key = r.Slice();
            byte[]? value = type == 0 ? null : r.Slice();
            Merge(journal, new Entry(key, value, sequence + i));
        }
        if (r.Remaining != 0) throw Corrupt("Trailing bytes in LevelDB write batch.");
    }

    private IEnumerable<byte[]> LogRecords(byte[] bytes)
    {
        using var fragments = new MemoryStream();
        bool partial = false;
        int position = 0;
        while (position < bytes.Length)
        {
            cancellation.ThrowIfCancellationRequested();
            int available = Math.Min(32768 - position % 32768, bytes.Length - position);
            if (available < 7)
            {
                if (bytes.AsSpan(position, available).IndexOfAnyExcept((byte)0) >= 0)
                    throw Corrupt("Nonzero/truncated LevelDB log trailer.");
                position += available;
                continue;
            }
            uint crc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position));
            int length = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(position + 4));
            byte type = bytes[position + 6];
            if (type == 0 && crc == 0 && length == 0)
            {
                if (bytes.AsSpan(position, available).IndexOfAnyExcept((byte)0) >= 0)
                    throw Corrupt("Invalid LevelDB log padding.");
                position += available;
                continue;
            }
            if (length > available - 7) throw Corrupt("Truncated LevelDB log record; close the world before importing.");
            uint actual = Crc(bytes.AsSpan(position + 6, 1), bytes.AsSpan(position + 7, length));
            if (Mask(actual) != crc) throw Corrupt("LevelDB log checksum mismatch.");
            if (type is < 1 or > 4) throw new NotSupportedException($"Unsupported LevelDB log record type {type}.");
            if ((type is 1 or 2) == partial) throw Corrupt("Invalid LevelDB log fragment order.");
            if (type == 1)
                yield return bytes.AsSpan(position + 7, length).ToArray();
            else
            {
                if (fragments.Length + length > MaxJournal) throw Corrupt("LevelDB logical log record too large.");
                fragments.Write(bytes, position + 7, length);
                partial = type != 4;
                if (!partial)
                {
                    yield return fragments.ToArray();
                    fragments.SetLength(0);
                }
            }
            position += 7 + length;
        }
        if (partial) throw Corrupt("Incomplete LevelDB log fragment; use a closed-world snapshot.");
    }

    private byte[] Snappy(ReadOnlySpan<byte> compressed)
    {
        var r = new Cursor(compressed.ToArray());
        int length = r.Length();
        if (length > MaxBlock) throw Corrupt("Snappy block exceeds safety limit.");
        byte[] output = new byte[length];
        int position = 0;
        while (r.Remaining > 0)
        {
            cancellation.ThrowIfCancellationRequested();
            int tag = r.Byte(), type = tag & 3, count, offset;
            if (type == 0)
            {
                int encoded = tag >> 2;
                if (encoded < 60) count = encoded + 1;
                else
                {
                    uint value = 0;
                    for (int i = 0; i < encoded - 59; i++) value |= (uint)r.Byte() << (i * 8);
                    if (value >= MaxBlock) throw Corrupt("Invalid Snappy literal length.");
                    count = (int)value + 1;
                }
                if (count > output.Length - position) throw Corrupt("Snappy literal exceeds output.");
                r.Bytes(count).CopyTo(output, position);
            }
            else
            {
                if (type == 1) { count = 4 + ((tag >> 2) & 7); offset = ((tag & 224) << 3) | r.Byte(); }
                else
                {
                    count = 1 + (tag >> 2);
                    uint value = type == 2 ? r.UInt16() : r.UInt32();
                    if (value > int.MaxValue) throw Corrupt("Invalid Snappy copy offset.");
                    offset = (int)value;
                }
                if (offset <= 0 || offset > position || count > output.Length - position)
                    throw Corrupt("Invalid Snappy copy range.");
                for (int i = 0; i < count; i++) output[position + i] = output[position + i - offset];
            }
            position += count;
        }
        if (position != length) throw Corrupt("Truncated Snappy output.");
        return output;
    }

    private byte[] ReadFile(string path, int maximum)
    {
        cancellation.ThrowIfCancellationRequested();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        if (stream.Length > maximum) throw Corrupt($"LevelDB file exceeds safety limit: {Path.GetFileName(path)}.");
        var before = new FileInfo(path);
        watched[path] = (stream.Length, before.LastWriteTimeUtc);
        return ReadAt(stream, 0, checked((int)stream.Length));
    }

    private byte[] ReadAt(FileStream stream, long offset, int length)
    {
        if (offset < 0 || length < 0 || offset > stream.Length - length) throw Corrupt("Read outside LevelDB file.");
        stream.Position = offset;
        byte[] bytes = new byte[length];
        int read = 0;
        while (read < length)
        {
            cancellation.ThrowIfCancellationRequested();
            int count = stream.Read(bytes, read, Math.Min(length - read, 65536));
            if (count == 0) throw Corrupt("Truncated LevelDB file.");
            read += count;
        }
        return bytes;
    }

    private void CheckUnchanged()
    {
        cancellation.ThrowIfCancellationRequested();
        foreach (var pair in watched)
        {
            var info = new FileInfo(pair.Key);
            if (!info.Exists || info.Length != pair.Value.Length || info.LastWriteTimeUtc != pair.Value.Modified)
                throw new IOException("Bedrock database changed during import. Close Minecraft or use a consistent world copy.");
        }
        if (File.ReadAllText(Path.Combine(directory, "CURRENT"), Encoding.UTF8) != current)
            throw new IOException("Bedrock CURRENT changed during import. Use a closed-world snapshot.");
        foreach (string file in Directory.EnumerateFiles(directory, "*.log"))
        {
            cancellation.ThrowIfCancellationRequested();
            if (ulong.TryParse(Path.GetFileNameWithoutExtension(file), NumberStyles.None, CultureInfo.InvariantCulture, out ulong number) &&
                (number >= recoveryLog || number == previousRecoveryLog) && !watched.ContainsKey(file))
                throw new IOException("A Bedrock journal appeared during import. Use a closed-world snapshot.");
        }
    }

    private static void Merge(Dictionary<string, Entry> entries, Entry entry)
    {
        string key = Convert.ToHexString(entry.Key);
        if (!entries.TryGetValue(key, out Entry? old) || entry.Sequence > old.Sequence) entries[key] = entry;
        else if (entry.Sequence == old.Sequence &&
            (entry.Value == null != (old.Value == null) ||
             (entry.Value != null && !entry.Value.AsSpan().SequenceEqual(old.Value))))
            throw Corrupt("Conflicting LevelDB values at the same sequence.");
    }

    private static void ReadLevel(Cursor reader)
    {
        if (reader.Varint() > 6) throw new NotSupportedException("Unsupported LevelDB table level.");
    }

    private static Handle ReadHandle(Cursor r) => new(r.Varint(), r.Varint());
    private static int Compare(byte[] a, byte[] b) => a.AsSpan().SequenceCompareTo(b);
    private static int CompareUser(byte[] a, byte[] b) => a.AsSpan(0, a.Length - 8).SequenceCompareTo(b.AsSpan(0, b.Length - 8));
    private static void ValidateInternalKey(byte[] key)
    {
        if (key.Length < 8) throw Corrupt("Truncated LevelDB internal key.");
        if (key[^8] > 1) throw new NotSupportedException($"Unsupported LevelDB internal key type {key[^8]}.");
    }

    private static byte[]? PrefixEnd(byte[] prefix)
    {
        byte[] end = (byte[])prefix.Clone();
        for (int i = end.Length - 1; i >= 0; i--)
            if (end[i] != 255) { end[i]++; return end[..(i + 1)]; }
        return null;
    }

    private static readonly uint[] CrcTable = CreateCrcTable();
    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint value = i;
            for (int bit = 0; bit < 8; bit++) value = (value >> 1) ^ ((value & 1) == 0 ? 0U : 0x82f63b78U);
            table[i] = value;
        }
        return table;
    }

    private static uint Crc(ReadOnlySpan<byte> first, ReadOnlySpan<byte> second = default)
    {
        uint value = uint.MaxValue;
        foreach (byte b in first) value = CrcTable[(value ^ b) & 255] ^ (value >> 8);
        foreach (byte b in second) value = CrcTable[(value ^ b) & 255] ^ (value >> 8);
        return ~value;
    }

    private static uint Mask(uint crc) => unchecked(((crc >> 15) | (crc << 17)) + 0xa282ead8U);
    private static InvalidDataException Corrupt(string message) => new(message);

    internal sealed class Cursor
    {
        private readonly byte[] data;
        private readonly int limit;
        public int Position { get; set; }
        public int Remaining => limit - Position;
        public Cursor(byte[] data, int? limit = null) { this.data = data; this.limit = limit ?? data.Length; }
        public byte Byte() { Require(1); return data[Position++]; }
        public ushort UInt16() { Require(2); ushort n = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(Position)); Position += 2; return n; }
        public uint UInt32() { Require(4); uint n = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(Position)); Position += 4; return n; }
        public ulong UInt64() { Require(8); ulong n = BinaryPrimitives.ReadUInt64LittleEndian(data.AsSpan(Position)); Position += 8; return n; }
        public byte[] Bytes(int count) { Require(count); byte[] result = data.AsSpan(Position, count).ToArray(); Position += count; return result; }
        public ulong Varint()
        {
            ulong result = 0;
            for (int shift = 0; shift < 70; shift += 7)
            {
                byte b = Byte();
                if (shift == 63 && b > 1) throw Corrupt("Overflowing LevelDB varint.");
                result |= (ulong)(b & 127) << shift;
                if (b < 128) return result;
            }
            throw Corrupt("Invalid LevelDB varint.");
        }
        public int Length()
        {
            ulong n = Varint();
            if (n > int.MaxValue) throw Corrupt("LevelDB length exceeds managed limit.");
            return (int)n;
        }
        public byte[] Slice() => Bytes(Length());
        public void Require(int length)
        {
            if (length < 0 || Position < 0 || length > Remaining) throw Corrupt("Truncated binary data.");
        }
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        journal.Clear();
        tables.Clear();
        watched.Clear();
    }
}
