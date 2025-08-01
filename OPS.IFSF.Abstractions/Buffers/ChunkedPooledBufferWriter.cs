﻿using OPS.IFSF.Abstractions.Attributes;
using System;
using System.Buffers;
using System.Globalization;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;

namespace OPS.IFSF.Abstractions.Buffers;

public sealed class ChunkedPooledBufferWriter : IDisposable
{
    private readonly int defaultChunkSize = 64;

    private Chunk? _head;
    private Chunk? _tail;

    private sealed class Chunk(int size)
    {
        public byte[] Buffer = ArrayPool<byte>.Shared.Rent(size);
        public int Length;
        public Chunk? Next;

        public void Return()
        {
            ArrayPool<byte>.Shared.Return(Buffer, true);
            Buffer = null!;
        }
    }

    public int TotalLength { get; private set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> GetSpan(int length)
    {
        EnsureChunk(length);
        Span<byte> span = _tail!.Buffer.AsSpan(_tail.Length, length);
        Advance(length);
        return span;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Memory<byte> GetMemory(int length)
    {
        EnsureChunk(length);
        var memory = _tail!.Buffer.AsMemory(_tail.Length, length);
        Advance(length);
        return memory;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Advance(int length)
    {
        _tail!.Length += length;
        TotalLength += length;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void EnsureChunk(int length)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(length, 1);

        if (_head is null)
        {
            _head = _tail = new Chunk(Math.Max(defaultChunkSize, length));
        }
        else if (_tail is null || _tail.Buffer.Length - _tail.Length < length)
        {
            var newChunk = new Chunk(Math.Max(defaultChunkSize, length));
            _tail!.Next = newChunk;
            _tail = newChunk;
        }
    }

    public async ValueTask ToStreamAsync(NetworkStream stream, CancellationToken cancellationToken = default)
    {
        for (var chunk = _head; chunk != null; chunk = chunk.Next)
        {
            await stream.WriteAsync(chunk.Buffer.AsMemory(0, chunk.Length), cancellationToken);
        }
    }

    public byte[] ToArray()
    {
        var result = new byte[TotalLength];
        int offset = 0;

        for (var c = _head; c != null; c = c.Next)
        {
            Array.Copy(c.Buffer, 0, result, offset, c.Length);
            offset += c.Length;
        }

        return result;
    }

    public bool IsReadFinished()
    {
        return _readChunk?.Next == null && _readChunk?.Length == _readOffset;
    }

    #region Write buffer

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(DateTime value, IsoFieldFormat format, int maxLength,
        [CallerArgumentExpression(nameof(value))]
        string? memberName = null)
    {
        int totalLength = format switch
        {
            IsoFieldFormat.DateTimeLong => 12,
            IsoFieldFormat.DateTimeShort => 10,
            _ => throw new ArgumentException("Unsupported format", memberName)
        };

        long result = 0;
        if (totalLength == 12)
            result += value.Year % 100 * 100_00_00_00_00L;

        result += value.Month * 1_00_00_00_00L;
        result += value.Day * 1_00_00_00L;
        result += value.Hour * 1_00_00L;
        result += value.Minute * 100L;
        result += value.Second;

        WriteNumberPadChunkedDirect(result, totalLength);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void WriteNumberPadChunkedDirect(long value, int totalLength)
    {
        long temp = value;
        int digitCount = 0;
        do
        {
            temp /= 10;
            digitCount++;
        } while (temp != 0);

        if (digitCount > totalLength)
            throw new InvalidOperationException("Value does not fit into the buffer.");

        int padCount = totalLength - digitCount;

        while (padCount > 0)
        {
            Span<byte> span = TryGetAvailableSpan(padCount);
            int len = Math.Min(padCount, span.Length);
            span.Slice(0, len).Fill((byte)'0');
            padCount -= len;
        }

        long divisor = Pow10(digitCount - 1);
        while (digitCount > 0)
        {
            Span<byte> span = TryGetAvailableSpan(digitCount);
            int len = Math.Min(span.Length, digitCount);

            for (int i = 0; i < len; i++)
            {
                long digit = (value / divisor) % 10;
                span[i] = (byte)('0' + digit);
                divisor /= 10;
            }

            digitCount -= len;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static long Pow10(int exp)
    {
        long result = 1;
        for (int i = 0; i < exp; i++)
            result *= 10;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public Span<byte> TryGetAvailableSpan(int requestedLength)
    {
        if (_tail is null || _tail.Length == _tail.Buffer.Length)
        {
            var newChunk = new Chunk(Math.Max(defaultChunkSize, requestedLength));
            if (_head is null)
                _head = newChunk;
            else
                _tail!.Next = newChunk;
            _tail = newChunk;
        }

        int available = _tail.Buffer.Length - _tail.Length;
        int take = Math.Min(available, requestedLength);

        var span = _tail.Buffer.AsSpan(_tail.Length, take);
        Advance(take);
        return span;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(Span<byte> value, IsoFieldFormat format, int maxLength,
        [CallerArgumentExpression(nameof(value))]
        string? memberName = null)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maxLength, memberName);

        switch (format)
        {
            case IsoFieldFormat.Byte:
                // Без префикса, но не использовать TryGetAvailableSpan + Advance
            {
                EnsureChunk(value.Length); // резервируем место
                var span = _tail!.Buffer.AsSpan(_tail.Length, value.Length);
                value.CopyTo(span);
                Advance(value.Length); // только 1 раз
                return;
            }
            case IsoFieldFormat.LLVar:
                WriteNumberPadChunkedDirect(value.Length, 2);
                break;
            default:
                throw new ArgumentException("Unsupported format for byte span", nameof(format));
        }

        ;

        int remaining = value.Length;
        int offset = 0;

        while (remaining > 0)
        {
            var target = TryGetAvailableSpan(remaining);
            int toCopy = Math.Min(remaining, target.Length);

            value.Slice(offset, toCopy).CopyTo(target);

            offset += toCopy;
            remaining -= toCopy;
        }

        return;
    }

    public void Write(char value, IsoFieldFormat format, int maxLength,
        [CallerArgumentExpression(nameof(value))]
        string? memberName = null)
    {
        ReadOnlySpan<char> span = [value];
        Write(span, format, maxLength, memberName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(ReadOnlySpan<char> value, IsoFieldFormat format, int maxLength,
        [CallerArgumentExpression(nameof(value))]
        string? memberName = null)
    {
        //ArgumentException.ThrowIfNullOrEmpty(value, memberName);
        ArgumentOutOfRangeException.ThrowIfZero(value.Length, memberName);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Length, maxLength, memberName);

        int prefixLen = format switch
        {
            IsoFieldFormat.LVar => 1,
            IsoFieldFormat.LLVar => 2,
            IsoFieldFormat.LLLVar => 3,
            IsoFieldFormat.CharPad => 0,
            IsoFieldFormat.CharPadWithOutFixedLength => 0, // ⬅️ добавлено
            _ => throw new ArgumentException("Not supported format", nameof(format))
        };

        // ⬇️ новое поведение: длина по value.Length, без паддинга
        int contentLength = format switch
        {
            IsoFieldFormat.CharPad => maxLength,
            IsoFieldFormat.CharPadWithOutFixedLength => value.Length, // ⬅️ ключевая разница
            _ => value.Length
        };

        if (prefixLen > 0)
        {
            WriteNumberPadChunkedDirect(contentLength, prefixLen);
        }

        int remaining = contentLength;
        int offset = 0;

        while (remaining > 0)
        {
            var target = TryGetAvailableSpan(remaining);
            int toWrite = target.Length;

            for (int i = 0; i < toWrite; i++)
            {
                char c = offset + i < value.Length ? value[offset + i] : ' ';
                if (c is < (char)0x20 or > (char)0x7E)
                    throw new ArgumentException($"Non-ASCII character '{c}' at position {offset + i}");

                // 👇 убираем паддинг только если CharPad2
                if (offset + i >= value.Length && format == IsoFieldFormat.CharPadWithOutFixedLength)
                    break;

                target[i] = (byte)c;
            }

            remaining -= toWrite;
            offset += toWrite;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Write(decimal value, IsoFieldFormat format, int maxLength,
        [CallerArgumentExpression(nameof(value))]
        string? memberName = null)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);

        switch (format)
        {
            case IsoFieldFormat.NumDecPad:
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Scale, 2, nameof(value));
                WriteNumberPadChunkedDirect((long)(value * 100), maxLength);
                return;

            case IsoFieldFormat.NumPad:
                ArgumentOutOfRangeException.ThrowIfNotEqual(value.Scale, 0, nameof(value));
                WriteNumberPadChunkedDirect((long)value, maxLength);
                return;

            case IsoFieldFormat.DecFrac3:
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Scale, 3, nameof(value));
                var c2 = CountDecimalAsciiLength(value, 3);
                WriteDecimalAscii(value, 3, GetSpan(c2), memberName);
                return;

            case IsoFieldFormat.DecFrac2:
                ArgumentOutOfRangeException.ThrowIfGreaterThan(value.Scale, 2, nameof(value));
                var c3 = CountDecimalAsciiLength(value, 2);
                WriteDecimalAscii(value, 2, GetSpan(c3), memberName);
                return;

            default:
                throw new ArgumentOutOfRangeException(nameof(format));
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteDecimalAscii(decimal value, int scale, Span<byte> buffer, string? memberName)
    {
        int offset = 0;

        long integral = (long)value;
        decimal frac = value - integral;

        // Целая часть
        int start = offset;
        do
        {
            buffer[offset++] = (byte)('0' + integral % 10);
            integral /= 10;
        } while (integral > 0);

        buffer[start..offset].Reverse();

        // Дробная часть
        if (frac > 0)
        {
            buffer[offset++] = (byte)'.';
            for (int i = 0; i < scale && frac > 0; i++)
            {
                frac *= 10;
                int digit = (int)frac;
                buffer[offset++] = (byte)('0' + digit);
                frac -= digit;
            }
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int CountDecimalAsciiLength(decimal value, int scale)
    {
        int intDigits = 0;
        decimal intPart = decimal.Truncate(value);
        if (intPart == 0)
        {
            intDigits = 1;
        }
        else
        {
            while (intPart >= 1)
            {
                intPart /= 10;
                intDigits++;
            }
        }

        int fracDigits = 0;
        decimal frac = value - decimal.Truncate(value);
        while (frac != 0 && fracDigits < scale)
        {
            frac *= 10;
            frac -= decimal.Truncate(frac);
            fracDigits++;
        }

        return fracDigits > 0 ? intDigits + 1 + fracDigits : intDigits;
    }

    #endregion

    #region Read buffer

    private Chunk? _readChunk;
    private int _readOffset;

    public void BeginRead()
    {
        _readChunk = _head;
        _readOffset = 0;
    }

    private void ValidateRead(int length)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(length);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(length, TotalLength);
        ArgumentNullException.ThrowIfNull(_readChunk);
    }

    public decimal ReadDecimal(IsoFieldFormat format, int maxLength, char? untilDelimiter = null)
    {
        Span<byte> buffer = stackalloc byte[maxLength];
        int written = 0;
        bool hasDot = false;
        var trace = new List<byte>();
        var log = new StringBuilder();

        log.AppendLine(
            $"[START] ReadDecimal: Format={format}, MaxLength={maxLength}, Delimiter={(untilDelimiter.HasValue ? $"'{untilDelimiter}'" : "null")}");

        // 👉 Удалим лишний разделитель, оставшийся от предыдущего чтения
        if (untilDelimiter.HasValue && _readChunk is not null)
        {
            byte current = _readChunk.Buffer[_readOffset];
            log.AppendLine($"Peek first byte: 0x{current:X2}");

            if (current == (byte)untilDelimiter.Value)
            {
                _readOffset++;
                log.AppendLine($"Skipped delimiter '{untilDelimiter}' at start");
            }
        }

        if (_readChunk is null)
        {
            log.AppendLine("[ERROR] _readChunk is null at method entry");
        }
        else
        {
            int remaining = _readChunk.Length - _readOffset;
            var preview = _readChunk.Buffer.AsSpan(_readOffset, Math.Min(16, remaining)).ToArray();
            log.AppendLine($"_readChunk.Length = {_readChunk.Length}, _readOffset = {_readOffset}");
            log.AppendLine($"Preview bytes: {BitConverter.ToString(preview)}");
        }

        try
        {
            if (format == IsoFieldFormat.NumDecPad)
            {
                ValidateRead(maxLength);
                long value = 0;

                while (maxLength > 0)
                {
                    if (_readChunk is null)
                        throw new EndOfStreamException("Unexpected end of stream while reading fixed-length decimal.");

                    var span = _readChunk.Buffer;
                    int available = _readChunk.Length - _readOffset;

                    if (available == 0)
                    {
                        _readChunk = _readChunk.Next;
                        _readOffset = 0;
                        continue;
                    }

                    int take = Math.Min(maxLength, available);
                    for (int i = 0; i < take; i++)
                    {
                        byte b = span[_readOffset++];
                        trace.Add(b);

                        if ((uint)(b - '0') > 9)
                        {
                            int errOffset = GetCurrentOffset() + i;
                            string traceStr = BitConverter.ToString(trace.ToArray());
                            throw new FormatException(
                                $"Invalid char '{(char)b}' (0x{b:X2}) in decimal at offset {errOffset}. Trace: {traceStr}");
                        }

                        value = value * 10 + (b - '0');
                    }

                    maxLength -= take;
                }

                return value / 100m;
            }

            // 👉 DecFrac2 / DecFrac3
            while (true)
            {
                if (_readChunk is null)
                {
                    log.AppendLine("[EOF] _readChunk is null before delimiter");
                    goto PARSE;
                }

                int available = _readChunk.Length - _readOffset;

                if (available == 0)
                {
                    _readChunk = _readChunk.Next;
                    _readOffset = 0;
                    continue;
                }

                var span = _readChunk.Buffer.AsSpan(_readOffset, available);
                for (int i = 0; i < available; i++)
                {
                    byte b = span[i];
                    trace.Add(b);

                    if (b == 0x00)
                    {
                        log.AppendLine($"Skipped 0x00 at offset +{i}");
                        continue;
                    }

                    if (untilDelimiter.HasValue && b == (byte)untilDelimiter.Value)
                    {
                        _readOffset += i + 1;
                        log.AppendLine($"Found delimiter '{untilDelimiter}' at +{i}, advancing to {_readOffset}");
                        goto PARSE;
                    }

                    if (written >= maxLength)
                    {
                        string traceStr = BitConverter.ToString(trace.ToArray());
                        throw new FormatException($"Decimal value too long (max {maxLength}). Trace: {traceStr}");
                    }

                    if (b >= '0' && b <= '9')
                    {
                        buffer[written++] = b;
                    }
                    else if (b == '.' && !hasDot)
                    {
                        buffer[written++] = b;
                        hasDot = true;
                    }
                    else
                    {
                        int errOffset = GetCurrentOffset() + i;
                        string traceStr = BitConverter.ToString(trace.ToArray());
                        throw new FormatException(
                            $"Invalid char '{(char)b}' (0x{b:X2}) in decimal at offset {errOffset}. Trace: {traceStr}");
                    }
                }

                _readOffset += available;
            }

            PARSE:
            if (written == 0)
            {
                string traceStr = trace.Count == 0 ? "<empty>" : BitConverter.ToString(trace.ToArray());
                log.AppendLine($"[ERROR] Empty decimal field. Offset={_readOffset}, Trace: {traceStr}");

                if (_readChunk != null)
                {
                    var remaining =
                        _readChunk.Buffer.AsSpan(_readOffset, Math.Min(16, _readChunk.Length - _readOffset));
                    log.AppendLine($"Remaining buffer: {BitConverter.ToString(remaining.ToArray())}");
                }
                else
                {
                    log.AppendLine("No remaining _readChunk");
                }

                throw new FormatException($"Empty decimal field. Trace: {traceStr}. Log:\n{log}");
            }

            var str = Encoding.ASCII.GetString(buffer.Slice(0, written));
            if (decimal.TryParse(str, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var result))
            {
                log.AppendLine($"[OK] Parsed value: {result}");
                return result;
            }

            string finalTrace = BitConverter.ToString(trace.ToArray());
            throw new FormatException($"Failed to parse decimal from '{str}'. Trace: {finalTrace}. Log:\n{log}");
        }
        catch (Exception ex)
        {
            throw new FormatException($"{ex.Message}\n--- Internal Log ---\n{log}", ex);
        }
    }

    public int ReadInt(IsoFieldFormat format, int maxLength)
    {
        if (format != IsoFieldFormat.NumPad)
            throw new ArgumentOutOfRangeException(nameof(format));

        ValidateRead(maxLength);

        int value = 0;
        var trace = new List<byte>();

        while (maxLength > 0)
        {
            if (_readChunk is null) throw new EndOfStreamException();

            var span = _readChunk.Buffer;
            int available = _readChunk.Length - _readOffset;

            if (available == 0)
            {
                _readChunk = _readChunk.Next;
                _readOffset = 0;
                continue;
            }

            int take = Math.Min(maxLength, available);

            for (int i = 0; i < take; i++)
            {
                byte b = span[_readOffset++];
                trace.Add(b);

                if ((uint)(b - '0') > 9)
                {
                    var offset = GetCurrentOffset();
                    var hex = BitConverter.ToString(trace.ToArray());
                    throw new FormatException(
                        $"Invalid character '{(char)b}' (0x{b:X2}) in integer field at offset {offset - trace.Count + 1 + i}. Bytes: {hex}");
                }

                value = value * 10 + (b - '0');
            }

            maxLength -= take;
        }

        return value;
    }

    public int PeekFieldNumber()
    {
        if (_readChunk is null) throw new EndOfStreamException();

        var span = _readChunk.Buffer;
        int available = _readChunk.Length - _readOffset;

        if (available == 0 && _readChunk.Next != null)
        {
            span = _readChunk.Next.Buffer;
            available = _readChunk.Next.Length;
        }

        if (available < 2) // минимально 2 символа для DE: "01".."99"
            throw new EndOfStreamException("Not enough data to peek field number");

        byte b1 = span[_readOffset];
        byte b2 = span[_readOffset + 1];

        if ((b1 < '0' || b1 > '9') || (b2 < '0' || b2 > '9'))
            throw new FormatException("Invalid characters in field number");

        return (b1 - '0') * 10 + (b2 - '0');
    }

    public int GetCurrentOffset()
    {
        int offset = 0;
        var chunk = _head;
        while (chunk != null && chunk != _readChunk)
        {
            offset += chunk.Length;
            chunk = chunk.Next;
        }

        if (chunk == _readChunk)
        {
            offset += _readOffset;
        }

        return offset;
    }

    public long ReadLong(IsoFieldFormat format, int maxLength)
    {
        if (format != IsoFieldFormat.NumPad)
        {
            throw new ArgumentOutOfRangeException(nameof(format));
        }

        ValidateRead(maxLength);

        long value = 0;
        while (maxLength > 0)
        {
            if (_readChunk is null) throw new EndOfStreamException();

            var span = _readChunk.Buffer;
            int available = _readChunk.Length - _readOffset;

            if (available == 0)
            {
                _readChunk = _readChunk.Next;
                _readOffset = 0;
                continue;
            }

            int take = Math.Min(maxLength, available);

            for (int i = 0; i < take; i++)
            {
                byte b = span[_readOffset++];
                if ((uint)(b - '0') > 9) throw new FormatException();
                value = value * 10 + (b - '0');
            }

            maxLength -= take;
        }

        return value;
    }

    public char ReadChar(IsoFieldFormat format, int maxLength)
    {
        /// TODO: сделать проверки и тд
        var arr = _readChunk.Buffer.AsSpan(_readOffset, 1);
        _readOffset += 1;
        return (char)arr[0];
    }

    public string ReadString(IsoFieldFormat format, int maxLength)
    {
        int length = format switch
        {
            IsoFieldFormat.LVar => ReadInt(IsoFieldFormat.NumPad, 1),
            IsoFieldFormat.LLVar => ReadInt(IsoFieldFormat.NumPad, 2),
            IsoFieldFormat.LLLVar => ReadInt(IsoFieldFormat.NumPad, 3),
            IsoFieldFormat.CharPad => maxLength,
            IsoFieldFormat.CharPadWithOutFixedLength => maxLength,
            _ => throw new FormatException("Unsupported format")
        };

        ValidateRead(length);

        char[] chars = ArrayPool<char>.Shared.Rent(length);
        int written = 0;
        int actualLength = 0;

        while (written < length)
        {
            if (_readChunk is null)
                throw new EndOfStreamException();

            int available = _readChunk.Length - _readOffset;

            if (available == 0)
            {
                _readChunk = _readChunk.Next;
                _readOffset = 0;
                continue;
            }

            int take = Math.Min(length - written, available);
            var span = _readChunk.Buffer.AsSpan(_readOffset, take);

            for (int i = 0; i < take; i++)
            {
                byte b = span[i];
                if ((uint)(b - 0x20) > 0x5E)
                {
                    ArrayPool<char>.Shared.Return(chars);
                    throw new FormatException($"Non-ASCII character '{b}' at {written + i}");
                }

                char c = (char)b;
                chars[written + i] = c;

                if (format != IsoFieldFormat.CharPad || c != ' ')
                    actualLength = written + i + 1;
            }

            _readOffset += take;
            written += take;
        }

        string result = new(chars, 0, format == IsoFieldFormat.CharPad ? actualLength : written);
        ArrayPool<char>.Shared.Return(chars);
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public DateTime ReadDateTime(IsoFieldFormat format, int maxLength)
    {
        int length = format switch
        {
            IsoFieldFormat.DateTimeLong => 12,
            IsoFieldFormat.DateTimeShort => 10,
            _ => throw new FormatException("Unsupported format.")
        };

        long raw = ReadLong(IsoFieldFormat.NumPad, length);

        int second = (int)(raw % 100);
        raw /= 100;
        int minute = (int)(raw % 100);
        raw /= 100;
        int hour = (int)(raw % 100);
        raw /= 100;
        int day = (int)(raw % 100);
        raw /= 100;
        int month = (int)(raw % 100);
        raw /= 100;

        int year = format == IsoFieldFormat.DateTimeLong
            ? 2000 + (int)(raw % 100)
            : DateTime.Now.Year;

        return new DateTime(year, month, day, hour, minute, second);
    }

    public byte[] ReadArray(IsoFieldFormat format, int maxLength)
    {
        if (format != IsoFieldFormat.Byte && format != IsoFieldFormat.LLVar)
            throw new FormatException("Unsupported format");

        if (format == IsoFieldFormat.LLVar)
        {
            maxLength = ReadInt(IsoFieldFormat.NumPad, 2);
        }

        ValidateRead(maxLength);

        if (_readChunk is null)
            throw new EndOfStreamException();

        int available = _readChunk.Length - _readOffset;
        if (available >= maxLength)
        {
            var span = _readChunk.Buffer.AsSpan(_readOffset, maxLength);
            _readOffset += maxLength;
            return span.ToArray();
        }

        throw new NotSupportedException("Cannot return span across chunk boundaries");
    }

    #endregion

    public void Clear()
    {
        _readChunk = null;
        var current = _head;
        while (current != null)
        {
            var next = current.Next;
            current.Return();
            current = next;
        }

        _head = _tail = null;
        TotalLength = 0;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        Clear();
    }
}