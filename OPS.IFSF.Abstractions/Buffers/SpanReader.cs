using System;
using OPS.IFSF.Abstractions.Attributes;
using System.Buffers;
using System.Globalization;
using System.Text;

namespace OPS.IFSF.Abstractions.Buffers
{
    /// <summary>
    /// Структура для последовательного чтения из Span<byte> с поддержкой различных форматов ISO8583.
    /// </summary>
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

        /// <summary>
        /// Читает 1 байт.
        /// </summary>
        public byte ReadByte()
        {
            if (_position >= _span.Length)
                throw new IndexOutOfRangeException("Attempted to read beyond the end of the span.");
            return _span[_position++];
        }

        /// <summary>
        /// Читает указанное количество байтов.
        /// </summary>
        public ReadOnlySpan<byte> ReadBytes(int count)
        {
            if (_position + count > _span.Length)
                throw new IndexOutOfRangeException("Attempted to read beyond the end of the span.");

            var result = _span.Slice(_position, count);
            _position += count;
            return result;
        }

        /// <summary>
        /// Читает 1 байт как символ.
        /// </summary>
        public char ReadChar()
        {
            return (char)ReadByte();
        }

        /// <summary>
        /// Заглушка для совместимости с интерфейсом. Не используется.
        /// </summary>
        public char ReadChar(IsoFieldFormat format, int maxLength)
        {
            return ReadChar();
        }

        /// <summary>
        /// Читает целое число заданной длины.
        /// </summary>
        public int ReadInt(int length)
        {
            var bytes = ReadBytes(length);

            for (int i = 0; i < bytes.Length; i++)
            {
                byte b = bytes[i];
                if ((uint)(b - '0') > 9)
                    throw new FormatException($"Invalid character '{(char)b}' in integer field.");
            }

            var str = Encoding.ASCII.GetString(bytes);
            return int.Parse(str);
        }

        /// <summary>
        /// Читает байты до разделителя или до максимальной длины.
        /// </summary>
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

        /// <summary>
        /// Чтение decimal до указанного символа-разделителя.
        /// </summary>
        public decimal ReadDecimal(IsoFieldFormat format, int maxLength, char untilDelimiter)
        {
            var bytes = ReadBytesUntilDelimiter(untilDelimiter, maxLength);
            var str = Encoding.ASCII.GetString(bytes);

            if (!decimal.TryParse(str, NumberStyles.Number, CultureInfo.InvariantCulture, out var result))
                throw new FormatException($"Invalid decimal format: '{str}'");

            return result;
        }

        /// <summary>
        /// Чтение int с форматированием.
        /// </summary>
        public int ReadInt(IsoFieldFormat format, int maxLength)
        {
            if (format != IsoFieldFormat.NumPad)
                throw new ArgumentOutOfRangeException(nameof(format));

            return ReadInt(maxLength);
        }

        /// <summary>
        /// Читает строку до указанного разделителя или до maxLength.
        /// </summary>
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

            return Encoding.ASCII.GetString(buffer.Slice(0, length)); // поддержка нуля внутри строки
        }

        /// <summary>
        /// Читает строку с учётом формата поля.
        /// </summary>
        public string ReadString(IsoFieldFormat format, int maxLength, char? fieldDelimiter = null)
        {
            if (format == IsoFieldFormat.CharPadWithOutFixedLength)
            {
                if (fieldDelimiter == null)
                    throw new ArgumentNullException(nameof(fieldDelimiter), "Delimiter is required for CharPadWithOutFixedLength");

                return ReadStringUntilDelimiter(fieldDelimiter.Value, maxLength);
            }

            int length = format switch
            {
                IsoFieldFormat.LVar => ReadInt(1),
                IsoFieldFormat.LLVar => ReadInt(2),
                IsoFieldFormat.LLLVar => ReadInt(3),
                IsoFieldFormat.CharPad => maxLength,
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
