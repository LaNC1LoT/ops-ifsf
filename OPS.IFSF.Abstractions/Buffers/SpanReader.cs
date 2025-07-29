using System;
using OPS.IFSF.Abstractions.Attributes;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace OPS.IFSF.Abstractions.Buffers
{
    public ref struct SpanReader
    {
        private ReadOnlySpan<byte> _span;
        private int _position;

        public SpanReader(ReadOnlySpan<byte> span)
        {
            _span = span;
            _position = 0;
        }

        public bool IsEnd => _position >= _span.Length;
        public int Position => _position;

        public byte ReadByte()
        {
            if (_position >= _span.Length)
                throw new IndexOutOfRangeException("Attempted to read beyond the end of the span.");
            return _span[_position++];
        }

        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (_position + count > _span.Length)
                throw new IndexOutOfRangeException("Attempted to read beyond the end of the span.");

            var result = _span.Slice(_position, count);
            _position += count;
            return result;
        }

        public char ReadChar()
        {
            return (char)ReadByte();
        }

        public char ReadChar(IsoFieldFormat format, int maxLength)
        {
            return ReadChar();
        }
        
        public void SkipToEnd()
        {
            _position = _span.Length;
        }

        public int ReadInt(int length)
        {
            var bytes = ReadBytes(length);

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if ((uint)(b - '0') > 9)
                    throw new FormatException($"Invalid character '{(char)b}' in integer field.");
            }

            var str = System.Text.Encoding.ASCII.GetString(bytes);
            return int.Parse(str);
        }
        private ReadOnlySpan<byte> ReadBytesUntilDelimiter(char delimiter, int maxLength)
        {
            int start = _position;
            int end = start;
    
            while (end < _span.Length && end - start < maxLength)
            {
                if (_span[end] == (byte)delimiter)
                    break;

                end++;
            }

            var result = _span.Slice(start, end - start);
            _position = (end < _span.Length && _span[end] == (byte)delimiter) ? end + 1 : end;

            return result;
        }
        public decimal ReadDecimal(IsoFieldFormat format, int maxLength, char untilDelimiter)
        {
            var bytes = ReadBytesUntilDelimiter(untilDelimiter, maxLength);
    
            var str = System.Text.Encoding.ASCII.GetString(bytes);
            if (!decimal.TryParse(str, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
                throw new FormatException($"Invalid decimal format: '{str}'");

            return result;
        }

        public int ReadInt(IsoFieldFormat format, int maxLength)
        {
            if (format != IsoFieldFormat.NumPad)
                throw new ArgumentOutOfRangeException(nameof(format));

            return ReadInt(maxLength);
        }

        public decimal ReadDecimal(int length)
        {
            var bytes = ReadBytes(length);

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if (!((b >= '0' && b <= '9') || b == '.'))
                    throw new FormatException($"Invalid character '{(char)b}' in decimal field.");
            }

            var str = System.Text.Encoding.ASCII.GetString(bytes);
            if (!decimal.TryParse(str, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
                throw new FormatException($"Invalid decimal format: '{str}'");

            return result;
        }

        public decimal ReadDecimal(IsoFieldFormat format, int maxLength)
        {
            return ReadDecimal(maxLength);
        }
        
        public string ReadStringUntilDelimiter(char delimiter, int maxLength)
        {
            Span<byte> buffer = stackalloc byte[maxLength];
            int length = 0;

            while (!IsEnd && length < maxLength)
            {
                byte b = ReadByte();
                if (b == delimiter)
                    break;

                buffer[length++] = b;
            }

            return Encoding.ASCII.GetString(buffer.Slice(0, length)); // ✅ сохраняет '0'
        }
        
        public string ReadString(IsoFieldFormat format, int maxLength)
        {
            int length = format switch
            {
                IsoFieldFormat.LVar => ReadInt(1),
                IsoFieldFormat.LLVar => ReadInt(2),
                IsoFieldFormat.LLLVar => ReadInt(3),
                IsoFieldFormat.CharPad => maxLength,
                IsoFieldFormat.CharPadWithOutFixedLength => maxLength,
                _ => throw new FormatException("Unsupported format")
            };

            var bytes = ReadBytes(length);

            char[] chars = ArrayPool<char>.Shared.Rent(length);
            int actualLength = 0;

            for (int i = 0; i < length; i++)
            {
                byte b = bytes[i];
                if ((uint)(b - 0x20) > 0x5E)
                {
                    ArrayPool<char>.Shared.Return(chars);
                    throw new FormatException($"Non-ASCII character '{(char)b}' at position {i}");
                }

                char c = (char)b;
                chars[i] = c;

                if (format != IsoFieldFormat.CharPad || c != ' ')
                    actualLength = i + 1;
            }

            string result = new(chars, 0, format == IsoFieldFormat.CharPad ? actualLength : length);
            ArrayPool<char>.Shared.Return(chars);
            return result;
        }
    }
}
