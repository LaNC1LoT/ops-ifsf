using System.Diagnostics;
using System.Reflection;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace OPS.IFSF.Generator;

[Generator]
public sealed class Iso8583Generator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var messages = context.SyntaxProvider
            .ForAttributeWithMetadataName(
                "OPS.IFSF.Abstractions.Attributes.IsoMessageAttribute",
                predicate: static (node, _) => node is ClassDeclarationSyntax,
                transform: static (ctx, _) => GetModel(ctx))
            .Where(static model => model is not null);

        context.RegisterSourceOutput(messages, static (ctx, model) =>
        {
            var write = GenerateWriteTo(model!);
            ctx.AddSource($"{model!.ClassName}_Write.g.cs", SourceText.From(write, Encoding.UTF8));

            var parseCode = GenerateParse(model!);
            ctx.AddSource($"{model.ClassName}_Parse.g.cs", SourceText.From(parseCode, Encoding.UTF8));
        });
    }

    private static MessageClassModel? GetModel(GeneratorAttributeSyntaxContext context)
    {
        var classSymbol = (INamedTypeSymbol)context.TargetSymbol;
        var msgAttr = classSymbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "IsoMessageAttribute");
        if (msgAttr is null) return null;

        string messageId = (string)msgAttr.ConstructorArguments[0].Value!;

        var classModel = new MessageClassModel(
            classSymbol.ContainingNamespace.ToDisplayString(),
            classSymbol.Name,
            messageId);

        var usedFieldNumbers = new HashSet<int>();

        foreach (var prop in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            var attr = prop.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "IsoFieldAttribute");
            if (attr is null) continue;

            var fieldModel = GetIsoFieldModel(attr, prop.Name, prop.Type);
            if (!usedFieldNumbers.Add(fieldModel.Number))
                throw new InvalidOperationException($"Duplicate field number {fieldModel.Number}");

            if (prop.Type.TypeKind == TypeKind.Class && prop.Type.Name != "String")
            {
                foreach (var nestedProp in prop.Type.GetMembers().OfType<IPropertySymbol>())
                {
                    var na = nestedProp.GetAttributes()
                        .FirstOrDefault(a => a.AttributeClass?.Name == "IsoFieldAttribute");
                    if (na is null)
                        continue;

                    string nestedName = $"{prop.Name}.{nestedProp.Name}";
                    var nestedFieldModel = GetIsoFieldModel(na, nestedName, nestedProp.Type);

                    // Обработка массива List<T>
                    if (nestedProp.Type is INamedTypeSymbol listType &&
                        listType.Name == "List" &&
                        listType.TypeArguments.Length == 1)
                    {
                        nestedFieldModel.IsArray = true;
                        var itemType = listType.TypeArguments[0];

                        foreach (var itemProp in itemType.GetMembers().OfType<IPropertySymbol>())
                        {
                            var itemAttr = itemProp.GetAttributes()
                                .FirstOrDefault(a => a.AttributeClass?.Name == "IsoFieldAttribute");
                            if (itemAttr is null) continue;

                            var itemFieldModel = GetIsoFieldModel(itemAttr, $"{nestedName}.{itemProp.Name}", itemProp.Type);
                            nestedFieldModel.ItemFields.Add(itemFieldModel);
                        }
                    }

                    fieldModel.NestedFields.Add(nestedFieldModel);
                }
            }

            classModel.Fields.Add(fieldModel);
        }
        return classModel;
    }
    
    private static IsoFieldModel GetIsoFieldModel(AttributeData attribute, string propName, ITypeSymbol typeSymbol)
    {
        var number = (int)attribute.ConstructorArguments[0].Value!;
        var enumValue = attribute.ConstructorArguments[1];
        var formatName = enumValue.Type!
            .GetMembers()
            .OfType<IFieldSymbol>()
            .FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, enumValue.Value))?
            .Name!;
        var length = (int)attribute.ConstructorArguments[2].Value!;
        var isNullable = typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;
        var underlyingType = isNullable && typeSymbol is INamedTypeSymbol named && named.IsGenericType
            ? named.TypeArguments[0].Name
            : typeSymbol.Name;
        var formatFull = $"{enumValue.Type!.ToDisplayString()}.{formatName}";
        return new IsoFieldModel(number, propName, formatFull, length, underlyingType, isNullable);
    }

    #region Writer

    private static string GenerateWriteTo(MessageClassModel model)
    {
        var sb = new StringBuilder(
            Iso8583CodeTemplates.WriteToHeader
                .Replace("{Namespace}", model.Namespace)
                .Replace("{ClassName}", model.ClassName)
                .Replace("{MessageId}", model.MessageId)
        );

        foreach (var f in model.Fields.OrderBy(f => f.Number))
        {
            if (f.IsNested)
            {
                // 1. Шапка composite DExx
                sb.AppendLine(
                    Iso8583CodeTemplates.WriteNestedHeader
                        .Replace("{Number}", f.Number.ToString())
                );

                foreach (var nf in f.NestedFields.OrderBy(n => n.Number))
                {
                    if (nf.IsArray)
                    {
                        sb.AppendLine(
                            Iso8583CodeTemplates.WriteNestedArrayFieldStart
                                .Replace("{Prop}", nf.PropertyName)
                        );

                        var itemFields = nf.ItemFields.OrderBy(x => x.Number).ToList();

                        for (int j = 0; j < itemFields.Count; j++)
                        {
                            var pf = itemFields[j];

                            // Генерация поля
                            string baseLine = Iso8583CodeTemplates.WriteArrayFieldPart
                                .Replace("{Prop}", pf.PropertyName.Split('.').Last())
                                .Replace("{Format}", pf.Format)
                                .Replace("{Length}", pf.Length.ToString());

                            sb.AppendLine(baseLine);

                            // Поля 4, 5, 6 — добавляем '\' после себя
                            if (pf.Number is 4 or 5 or 6)
                            {
                                sb.AppendLine("        writer.Write(\"\\\\\", IsoFieldFormat.CharPad, 1);");
                            }
                        }

                        // 👉 Вставляем '/' только если это не последний item — но В КОНЦЕ айтема
                        sb.AppendLine($"        if (i < {nf.PropertyName}.Count - 1) writer.Write(\"/\", IsoFieldFormat.CharPad, 1);");

                        sb.AppendLine(
                            Iso8583CodeTemplates.WriteNestedArrayFieldEnd
                                .Replace("{ParentNumber}", f.Number.ToString())
                        );

                        continue;
                    }

                    var tpl = (nf.IsNullable && nf.PropertyType != "String")
                        ? Iso8583CodeTemplates.WriteNestedNullableField
                        : Iso8583CodeTemplates.WriteNestedField;

                    sb.AppendLine(
                        tpl
                            .Replace("{ParentNumber}", f.Number.ToString())
                            .Replace("{Number}", nf.Number.ToString())
                            .Replace("{Comment}", nf.ToSummary())
                            .Replace("{Prop}", nf.PropertyName)
                            .Replace("{Format}", nf.Format)
                            .Replace("{Length}", nf.Length.ToString())
                    );
                }

                // 3. Футер composite
                sb.AppendLine(
                    Iso8583CodeTemplates.WriteNestedFooter
                        .Replace("{Number}", f.Number.ToString())
                );
            }
            else
            {
                // обычное поле
                var tpl = (f.IsNullable && f.PropertyType != "String")
                    ? Iso8583CodeTemplates.WriteNullableField
                    : Iso8583CodeTemplates.WriteSimpleField;

                sb.AppendLine(
                    tpl
                        .Replace("{Comment}", f.ToSummary())
                        .Replace("{Prop}", f.PropertyName)
                        .Replace("{Format}", f.Format)
                        .Replace("{Length}", f.Length.ToString())
                        .Replace("{Number}", f.Number.ToString())
                );
            }
        }

        sb.Append(Iso8583CodeTemplates.WriteToFooter);
        return sb.ToString();
    }

    #endregion

    #region Reader

    private static string GenerateParse(MessageClassModel model)
    {
        var sb = new StringBuilder(
            Iso8583CodeTemplates.ParseHeader
                .Replace("{Namespace}", model.Namespace)
                .Replace("{ClassName}", model.ClassName)
        );

        foreach (var f in model.Fields.OrderBy(f => f.Number))
        {
            if (f.IsNested)
            {
                // 1) Header для composite DExx
                sb.AppendLine(
                    Iso8583CodeTemplates.ParseNestedHeader
                        .Replace("{Number}", f.Number.ToString())
                        .Replace("{PropClass}", f.PropertyType)
                );

                // 2) Вложенные поля
                foreach (var nf in f.NestedFields.OrderBy(n => n.Number))
                {
                    
                    if (nf.IsArray)
                    {
                        // Begin array field parsing block
                        sb.AppendLine(
                            Iso8583CodeTemplates.ParseNestedArrayFieldStart
                                .Replace("{FieldNumber}", nf.Number.ToString())
                                .Replace("{ItemType}", "SaleItem")
                        );

                        // Loop through each sub-field of the item
                        var itemFields = nf.ItemFields.OrderBy(x => x.Number).ToList();
                        for (int j = 0; j < itemFields.Count; j++)
                        {
                            var pf = itemFields[j];
                            string? readMethod = GetReadMethod(pf.PropertyType);
                            if (pf.PropertyType == "Char")
                            {
                                // Use the special char template (reads a one-character string and takes [0])
                                sb.AppendLine(
                                    Iso8583CodeTemplates.ParseArrayFieldPartChar
                                        .Replace("{Prop}", pf.PropertyName.Split('.').Last())
                                        .Replace("{Format}", pf.Format)
                                        .Replace("{Length}", pf.Length.ToString())
                                );
                            }
                            else if (readMethod is null)
                            {
                                // Unsupported type in item – generate a throw (using existing template for unsupported nested field)
                                sb.AppendLine(
                                    Iso8583CodeTemplates.ParseUnsupportedNestedField
                                        .Replace("{FieldNumber}", pf.Number.ToString())
                                        .Replace("{ParentNumber}", f.Number.ToString())
                                        .Replace("{Type}", pf.PropertyType)
                                );
                            }
                            else
                            {
                                // Supported field – generate normal field parsing line
                                sb.AppendLine(
                                    Iso8583CodeTemplates.ParseArrayFieldPart
                                        .Replace("{Prop}", pf.PropertyName.Split('.').Last())
                                        .Replace("{ReadMethod}", readMethod)
                                        .Replace("{Format}", pf.Format)
                                        .Replace("{Length}", pf.Length.ToString())
                                );
                            }

                            // If this field is one of those followed by a '\' delimiter in the data, generate a skip for it
                            if (pf.Number is 4 or 5 or 6)
                            {
                                sb.AppendLine(Iso8583CodeTemplates.ParseArrayFieldSkipDelimiter);
                            }
                        }

                        // End of array field parsing block
                        sb.AppendLine(
                            Iso8583CodeTemplates.ParseNestedArrayFieldEnd
                                .Replace("{FieldNumber}", nf.Number.ToString())
                                .Replace("{ParentNumber}", f.Number.ToString())
                                .Replace("{InnerProp}", nf.PropertyName.Split('.').Last())
                        );

                        continue; // move to next nested field
                    }
                    
                    // определяем readMethod (или null)
                    string? readMethodNested = GetReadMethod(nf.PropertyType);

                    // выбираем шаблон: поддерживаемый или «unsupported»
                    var tplNested = readMethodNested is null
                        ? Iso8583CodeTemplates.ParseUnsupportedNestedField
                        : Iso8583CodeTemplates.ParseNestedField;

                    sb.AppendLine(
                        tplNested
                            .Replace("{ParentNumber}", f.Number.ToString())
                            .Replace("{FieldNumber}", nf.Number.ToString())
                            .Replace("{Type}", nf.PropertyType)
                            .Replace("{InnerProp}", nf.PropertyName.Split('.').Last())
                            .Replace("{ReadMethod}", readMethodNested ?? "")
                            .Replace("{Format}", nf.Format)
                            .Replace("{Length}", nf.Length.ToString())
                    );
                }

                // 3) Footer для composite
                sb.AppendLine(
                    Iso8583CodeTemplates.ParseNestedFooter
                        .Replace("{ParentNumber}", f.Number.ToString())
                        .Replace("{Prop}", f.PropertyName)
                        .Replace("{Number}", f.Number.ToString())
                );
            }
            else
            {
                // 4) Простое поле
                string? readMethod = GetReadMethod(f.PropertyType);

                var tplSimple = readMethod is null
                    ? Iso8583CodeTemplates.ParseUnsupportedSimpleField
                    : Iso8583CodeTemplates.ParseSimpleField;

                sb.AppendLine(
                    tplSimple
                        .Replace("{Number}", f.Number.ToString())
                        .Replace("{Type}", f.PropertyType)
                        .Replace("{Prop}", f.PropertyName)
                        .Replace("{ReadMethod}", readMethod ?? "")
                        .Replace("{Format}", f.Format)
                        .Replace("{Length}", f.Length.ToString())
                );
            }
        }

        sb.Append(Iso8583CodeTemplates.ParseFooter);
        return sb.ToString();
    }

    private static string? GetReadMethod(string propertyType) => propertyType switch
    {
        "DateTime" => "DateTime",
        "String" => "String",
        "Int32" => "Int",
        "Int64" => "Long",
        "Decimal" => "Decimal",
        "byte[]" => "Array",
        _ => null
    };

    #endregion
}