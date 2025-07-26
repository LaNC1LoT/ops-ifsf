using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using System.Text;

namespace OPS.IFSF.Generator;

[Generator]
public sealed class Iso8583Generator : IIncrementalGenerator
{
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // TODO: раскоментировать если хочется подебажить генератор
        //System.Diagnostics.Debugger.Launch();
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

                            var itemFieldModel =
                                GetIsoFieldModel(itemAttr, $"{nestedName}.{itemProp.Name}", itemProp.Type);
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
        return new IsoFieldModel(number, propName, formatFull, length, typeSymbol, underlyingType);
    }

    #region Writer

    private static string GenerateWriteTo(MessageClassModel model)
    {
        var sbMain = new StringBuilder();
        var sbNested = new StringBuilder();

        sbMain.AppendLine(Iso8583CodeTemplatesWrite.WriteToHeader(model.Namespace, model.ClassName, model.MessageId));

        foreach (var f in model.Fields.OrderBy(f => f.Number))
        {
            if (f.IsNested)
            {
                // Вызов вложенного метода
                sbMain.AppendLine(Iso8583CodeTemplatesWrite.WriteNestedCall(f.Number, f.PropertyName));

                // Генерация тела вложенного метода
                var nestedWrites = new List<string>();

                foreach (var nf in f.NestedFields.OrderBy(n => n.Number))
                {
                    var fullProp = $"value.{nf.PropertyName}";
                    
                    if (nf.IsArray)
                    {
                        nestedWrites.Add(
                            Iso8583CodeTemplatesWrite.WriteNestedArrayFieldStart
                                .Replace("{Prop}", fullProp)
                        );

                        var itemFields = nf.ItemFields.OrderBy(x => x.Number).ToList();

                        for (int j = 0; j < itemFields.Count; j++)
                        {
                            var pf = itemFields[j];

                            // Генерация поля
                            string baseLine = Iso8583CodeTemplatesWrite.WriteArrayFieldPart
                                .Replace("{Prop}", pf.PropertyName.Split('.').Last())
                                .Replace("{Format}", pf.Format)
                                .Replace("{Length}", pf.Length.ToString());

                            nestedWrites.Add(baseLine);

                            // Поля 4, 5, 6 — добавляем '\' после себя
                            if (pf.Number is 4 or 5 or 6)
                            {
                                nestedWrites.Add("               writer.Write(\"\\\\\", IsoFieldFormat.CharPad, 1);");
                            }
                        }

                        // 👉 Вставляем '/' только если это не последний item — но В КОНЦЕ айтема
                        nestedWrites.Add(
                            $"              if (i < {fullProp}.Count - 1) writer.Write(\"/\", IsoFieldFormat.CharPad, 1);");

                        nestedWrites.Add(
                            Iso8583CodeTemplatesWrite.WriteNestedArrayFieldEnd(nf.Number,f.Number.ToString())
                        );

                        continue;
                    }

                    var fieldCode = nf.IsNullable
                        ? Iso8583CodeTemplatesWrite.WriteNestedNullableField(
                            nf.Number, fullProp, nf.Format, nf.Length, nf.ToSummary(), f.Number.ToString(),
                            nf.IsReferenceType ? " != null" : ".HasValue",
                            nf.IsReferenceType ? "" : ".Value")
                        : Iso8583CodeTemplatesWrite.WriteNestedField(
                            nf.Number, fullProp, nf.Format, nf.Length, nf.ToSummary(), f.Number.ToString());

                    nestedWrites.Add(fieldCode);
                }

                sbNested.AppendLine(
                    Iso8583CodeTemplatesWrite.WriteNestedMethod(f.Number, f.PropertyTypeDisplay, nestedWrites));
            }
            else
            {
                string code = f.IsNullable
                    ? Iso8583CodeTemplatesWrite.WriteNullableField(
                        f.Number, f.PropertyName, f.Format, f.Length, f.ToSummary(),
                        "0", f.IsReferenceType ? " != null" : ".HasValue",
                        f.IsReferenceType ? "" : ".Value")
                    : Iso8583CodeTemplatesWrite.WriteField(
                        f.Number, f.PropertyName, f.Format, f.Length, f.ToSummary(), "0");

                sbMain.AppendLine(code);
            }
        }

        sbMain.AppendLine(Iso8583CodeTemplatesWrite.WriteToFooter(sbNested.ToString()));
        return sbMain.ToString();
    }

    #endregion

    #region Reader

    private static string GenerateParse(MessageClassModel model)
    {
        var sbMain = new StringBuilder();
        var sbNested = new StringBuilder();

        sbMain.AppendLine(Iso8583CodeTemplatesParse.ParseHeader(model.Namespace, model.ClassName));

        foreach (var f in model.Fields.OrderBy(f => f.Number))
        {
            if (f.IsNested)
            {
                // Вставляем вызов вложенного метода в основной switch
                sbMain.AppendLine(Iso8583CodeTemplatesParse.ParseNestedCall(f.Number, f.PropertyName));

                // Генерируем тело вложенного метода
                var nestedSwitches = new List<string>();
                foreach (var nf in f.NestedFields.OrderBy(n => n.Number))
                {
                    if (nf.IsArray)
                    {
                        // Begin array field parsing block
                        nestedSwitches.Add(
                            Iso8583CodeTemplatesParse.ParseNestedArrayFieldStart
                                .Replace("{FieldNumber}", nf.Number.ToString())
                                .Replace("{ItemType}", "SaleItem")
                        );

                        // Loop through each sub-field of the item
                        var itemFields = nf.ItemFields.OrderBy(x => x.Number).ToList();
                        for (int j = 0; j < itemFields.Count; j++)
                        {
                            var pf = itemFields[j];
                            string? readMethodArray = GetReadMethod(pf.PropertyType);
                            if (pf.PropertyType == "Char")
                            {
                                // Use the special char template (reads a one-character string and takes [0])
                                nestedSwitches.Add(
                                    Iso8583CodeTemplatesParse.ParseArrayFieldPartChar
                                        .Replace("{Prop}", pf.PropertyName.Split('.').Last())
                                        .Replace("{Format}", pf.Format)
                                        .Replace("{Length}", pf.Length.ToString())
                                );
                            }
                            else if (readMethodArray is null)
                            {
                                // Unsupported type in item – generate a throw (using existing template for unsupported nested field)
                                nestedSwitches.Add(
                                    Iso8583CodeTemplatesParse.ParseUnsupportedNestedField
                                        .Replace("{FieldNumber}", pf.Number.ToString())
                                        .Replace("{ParentNumber}", f.Number.ToString())
                                        .Replace("{Type}", pf.PropertyType)
                                );
                            }
                            else
                            {
                                // Supported field – generate normal field parsing line
                                nestedSwitches.Add(
                                    Iso8583CodeTemplatesParse.ParseArrayFieldPart
                                        .Replace("{Prop}", pf.PropertyName.Split('.').Last())
                                        .Replace("{ReadMethod}", readMethodArray)
                                        .Replace("{Format}", pf.Format)
                                        .Replace("{Length}", pf.Length.ToString())
                                );
                            }

                            // If this field is one of those followed by a '\' delimiter in the data, generate a skip for it
                            if (pf.Number is 4 or 5 or 6)
                            {
                                nestedSwitches.Add(Iso8583CodeTemplatesParse.ParseArrayFieldSkipDelimiter);
                            }
                        }

                        // End of array field parsing block
                        nestedSwitches.Add(
                            Iso8583CodeTemplatesParse.ParseNestedArrayFieldEnd
                                .Replace("{FieldNumber}", nf.Number.ToString())
                                .Replace("{ParentNumber}", f.Number.ToString())
                                .Replace("{InnerProp}", nf.PropertyName.Split('.').Last())
                        );

                        continue; // move to next nested field
                    }


                    var readMethod = GetReadMethod(nf.PropertyTypeDisplay);
                    string nestedLine = readMethod is null
                        ? Iso8583CodeTemplatesParse.ParseUnsupportedField(nf.Number, nf.PropertyName,
                            nf.PropertyTypeDisplay)
                        : Iso8583CodeTemplatesParse.ParseField(nf.Number, nf.PropertyName, "nested", readMethod,
                            nf.Format, nf.Length);
                    nestedSwitches.Add(nestedLine);
                }

                sbNested.AppendLine(
                    Iso8583CodeTemplatesParse.ParseNestedMethod(f.Number, f.PropertyTypeDisplay, nestedSwitches));
            }
            else
            {
                var readMethod = GetReadMethod(f.PropertyTypeDisplay);
                string line = readMethod is null
                    ? Iso8583CodeTemplatesParse.ParseUnsupportedField(f.Number, f.PropertyName, f.PropertyTypeDisplay)
                    : Iso8583CodeTemplatesParse.ParseField(f.Number, f.PropertyName, "response", readMethod, f.Format,
                        f.Length);
                sbMain.AppendLine(line);
            }
        }

        sbMain.AppendLine(Iso8583CodeTemplatesParse.ParseFooter(sbNested.ToString()));
        return sbMain.ToString();
    }


    private static string? GetReadMethod(string propertyType) => propertyType.ToLower() switch
    {
        "datetime" => "DateTime",
        "string" => "String",
        "int32" => "Int",
        "int64" => "Long",
        "decimal" => "Decimal",
        "byte[]" => "Array",
        _ => null
    };

    #endregion
}