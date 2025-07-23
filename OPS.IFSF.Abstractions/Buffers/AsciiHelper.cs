using System.Runtime.CompilerServices;
using System.Text;

namespace OPS.IFSF.Abstractions.Buffers;

public static class AsciiHelper
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetBitMap(int field, Span<byte> buffer)
    {
        if (buffer.Length != 8)
            throw new ArgumentException("Буфер должен быть длиной 8 байт", nameof(buffer));

        if (field is < 1 or > 64)
            throw new ArgumentOutOfRangeException(nameof(field), $"Поле {field} вне диапазона 1–64");

        int bitIndex = field - 1;
        int byteIndex = bitIndex / 8;
        int bitOffset = 7 - bitIndex % 8;

        buffer[byteIndex] |= (byte)(1 << bitOffset);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static void SetLength(int value, Span<byte> buffer,
         [CallerArgumentExpression(nameof(value))] string? memberName = null)
    {
        WriteNumberPad(value, buffer, memberName);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteNumberPad(long value, Span<byte> buffer, string? memberName)
    {
        for (int i = buffer.Length - 1; i >= 0; i--)
        {
            buffer[i] = (byte)('0' + value % 10);
            value /= 10;
        }

        if (value != 0)
            throw new InvalidOperationException("Value does not fit into the buffer.");

        Console.WriteLine($"{memberName} = {Encoding.ASCII.GetString(buffer)}");
    }
}
