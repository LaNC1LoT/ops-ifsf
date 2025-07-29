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
                ProductCode = reader.ReadStringUntilDelimiter('\\', 17), // ⬅️ Чтение до \
                Quantity = reader.ReadDecimal(IsoFieldFormat.DecFrac3, 9, '\\'),
                UnitPrice = reader.ReadDecimal(IsoFieldFormat.DecFrac2, 9, '\\'),
                Amount = reader.ReadDecimal(IsoFieldFormat.DecFrac2, 12, '/') // до /
            };

            result.Items.Add(item);
        }
        // 👇 Логгируем результат перед возвратом
        var sb = new StringBuilder();
        sb.AppendLine("[DE63 parsed result]");
        sb.AppendLine($"[Length] {totalLength}");
        sb.AppendLine($"[RawBytes] {BitConverter.ToString(bytes)}");
        sb.AppendLine($"[AsText] {System.Text.Encoding.ASCII.GetString(bytes)}");
        sb.AppendLine($"[ServiceLevel] {result.ServiceLevel}, [ItemCount] {result.ItemCount}, [FormatId] {result.FormatId}");
        
        for (int i = 0; i < result.Items.Count; i++)
        {
            var item = result.Items[i];
            sb.AppendLine($"-- Item {i + 1} --");
            sb.AppendLine($"PaymentType: {item.PaymentType}");
            sb.AppendLine($"UnitOfMeasure: {item.UnitOfMeasure}");
            sb.AppendLine($"VatCode: {item.VatCode}");
            sb.AppendLine($"ProductCode: {item.ProductCode}");
            sb.AppendLine($"Quantity: {item.Quantity}");
            sb.AppendLine($"UnitPrice: {item.UnitPrice}");
            sb.AppendLine($"Amount: {item.Amount}");
        }

        // throw new Exception(sb.ToString());
        return result;
    }

}