using System.Text;
using OPS.IFSF.Abstractions.Attributes;
using OPS.IFSF.Abstractions.Models;

namespace OPS.IFSF.Abstractions.Buffers;

public class De63Class
{
    public static De63 ParseDE63(ChunkedPooledBufferWriter writer)
    {
        var totalLength = writer.ReadInt(IsoFieldFormat.NumPad, 3);
        var bytes = writer.ReadArray(IsoFieldFormat.Byte, totalLength);

        var reader = new SpanReader(bytes);

        var result = new De63
        {
            ServiceLevel = reader.ReadChar(IsoFieldFormat.CharPad, 1),
            ItemCount = reader.ReadInt(IsoFieldFormat.NumPad, 2),
            FormatId = reader.ReadChar(IsoFieldFormat.CharPad, 1),
            Items = new List<SaleItem>()
        };

        while (!reader.IsEnd)
        {
            var item = new SaleItem
            {
                PaymentType = reader.ReadChar(IsoFieldFormat.CharPad, 1),
                UnitOfMeasure = reader.ReadChar(IsoFieldFormat.CharPad, 1),
                VatCode = reader.ReadInt(IsoFieldFormat.NumPad, 1),
                ProductCode = reader.ReadStringUntilDelimiter('\\', 17),
                Quantity = reader.ReadDecimal(IsoFieldFormat.DecFrac3, 9, '\\'),
                UnitPrice = reader.ReadDecimal(IsoFieldFormat.DecFrac2, 9, '\\'),
                Amount = reader.ReadDecimal(IsoFieldFormat.DecFrac2, 12, '/')
            };

            result.Items.Add(item);
        }
        
        return result;
    }

}