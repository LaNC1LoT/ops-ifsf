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
        var withBitMapArray = (bool)attribute.ConstructorArguments[3].Value!;
        var beforeDelimiter = (char)attribute.ConstructorArguments[4].Value!;
        var itemSplitter = (char)attribute.ConstructorArguments[5].Value!;
        var isNullable = typeSymbol.NullableAnnotation == NullableAnnotation.Annotated;

        var underlyingType = isNullable && typeSymbol is INamedTypeSymbol named && named.IsGenericType
            ? named.TypeArguments[0].Name
            : typeSymbol.Name;

        var formatFull = $"{enumValue.Type!.ToDisplayString()}.{formatName}";

        var model = new IsoFieldModel(
            number, propName, formatFull, length, typeSymbol,
            underlyingType, withBitMapArray, beforeDelimiter, itemSplitter
        );

        // 👇 Добавляем сюда имя типа T для List<T>
        if (typeSymbol is INamedTypeSymbol listSymbol &&
            listSymbol.Name == "List" &&
            listSymbol.TypeArguments.Length == 1)
        {
            var itemType = listSymbol.TypeArguments[0];
            model.ItemTypeDisplay = itemType.ToDisplayString(SymbolDisplayFormat.MinimallyQualifiedFormat);
        }

        return model;
    }

    #region Writer

    private static string GenerateWriteTo(MessageClassModel model)
    {
        var sbMain = new StringBuilder();
        var sbNested = new StringBuilder();
        static bool IsPrintable(char c) => !char.IsControl(c) && !char.IsSurrogate(c);

        string EscapeCharForCSharp(char c)
        {
            return c switch
            {
                '\\' => "\\\\",
                '\"' => "\\\"",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                '\0' => "\\0",
                _ when char.IsControl(c) || !IsPrintable(c) => $"\\u{(int)c:X4}",
                _ => c.ToString()
            };
        }

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
                    var fullPropArray = $"value.{nf.PropertyName.Split('.').Last()}";

                    if (nf.IsArray)
                    {
                        nestedWrites.Add(
                            Iso8583CodeTemplatesWrite.WriteNestedArrayFieldStart
                                .Replace("{Prop}", fullPropArray)
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
                    
                            if (pf.BeforeDelimiter != ' ')
                            {
                                var escaped = EscapeCharForCSharp(pf.BeforeDelimiter);
                    
                                nestedWrites.Add(
                                    $"               writer.Write(\"{escaped}\", IsoFieldFormat.CharPad, 1);");
                            }
                    
                            nestedWrites.Add(baseLine);
                        }
                    
                        if (nf.ItemSplitter != ' ')
                        {
                            var escaped = EscapeCharForCSharp(nf.ItemSplitter);
                    
                            nestedWrites.Add(
                                $"              if (i < {fullPropArray}.Count - 1) writer.Write(\"{escaped}\", IsoFieldFormat.CharPad, 1);");
                        }
                    
                        nestedWrites.Add(f.WithBitMapArray
                            ? Iso8583CodeTemplatesWrite.WriteNestedArrayFieldEnd(nf.Number, f.Number.ToString())
                            : Iso8583CodeTemplatesWrite.WriteNestedArrayFieldEndWithOutBitMap(nf.Number,
                                f.Number.ToString()
                            )
                        );
                    
                        continue;
                    }

                    var writeNestedField = f.WithBitMapArray
                        ? Iso8583CodeTemplatesWrite.WriteNestedField(
                            nf.Number, fullPropArray, nf.Format, nf.Length, nf.ToSummary(), f.Number.ToString())
                        : Iso8583CodeTemplatesWrite.WriteNestedFieldWithOutBitMap(
                            fullPropArray, nf.Format, nf.Length, nf.ToSummary());

                    var fieldCode = nf.IsNullable
                        ? Iso8583CodeTemplatesWrite.WriteNestedNullableField(
                            nf.Number, fullPropArray, nf.Format, nf.Length, nf.ToSummary(), f.Number.ToString(),
                            nf.IsReferenceType ? " != null" : ".HasValue",
                            nf.IsReferenceType ? "" : ".Value")
                        : writeNestedField;

                    nestedWrites.Add(fieldCode);
                }

                sbNested.AppendLine(
                    f.WithBitMapArray
                        ? Iso8583CodeTemplatesWrite.WriteNestedMethod(f.Number, f.PropertyTypeDisplay, nestedWrites)
                        : Iso8583CodeTemplatesWrite.WriteNestedMethodWithOutBitMap(f.Number, f.PropertyTypeDisplay,
                            nestedWrites));
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

    #region Parse
    
    private static string EscapeCharForCSharp(char c)
    {
        return c switch
        {
            '\\' => "\\\\",
            '\'' => "\\\'",
            '\"' => "\\\"",
            '\n' => "\\n",
            '\r' => "\\r",
            '\t' => "\\t",
            '\0' => "\\0",
            _ when char.IsControl(c) || !char.IsLetterOrDigit(c) => $"\\u{(int)c:X4}",
            _ => c.ToString()
        };
    }

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

            // Генерация тела вложенного метода
            var nestedParsers = new List<string>();

            foreach (var nf in f.NestedFields.OrderBy(n => n.Number))
            {
                // 👇 пока пропускаем массив — вернёмся позже
                if (nf.IsArray)
                {
                    var nestedArrayFieldParsers = new List<string>();

                    foreach (var itemField in nf.ItemFields.OrderBy(x => x.Number))
                    {
                        var itemReadMethod = GetReadMethod(itemField.PropertyTypeDisplay);

                        string? beforeDelimiterLine = null;
                        string? delimiter = null;
                        if (itemField.BeforeDelimiter != ' ')
                        {
                            var escaped = EscapeCharForCSharp(itemField.BeforeDelimiter);
                            delimiter = $", '{escaped}'";
                        }

                        string itemFieldLine = itemReadMethod is null
                            ? Iso8583CodeTemplatesParse.ParseUnsupportedField(
                                itemField.Number,
                                itemField.PropertyName,
                                itemField.PropertyTypeDisplay)
                            : Iso8583CodeTemplatesParse.ParseField(
                                itemField.Number,
                                itemField.PropertyName.Split('.').Last(),
                                "item",
                                itemReadMethod,
                                itemField.Format,
                                itemField.Length,
                                null,
                                delimiter
                            );

                        nestedArrayFieldParsers.Add(itemFieldLine);
                    }


                    nestedParsers.Add(
                        Iso8583CodeTemplatesParse.ParseNestedArrayFieldStart
                            .Replace("{FieldNumber}", nf.Number.ToString())
                            .Replace("{ItemType}", nf.ItemTypeDisplay ?? "object")
                    );

                    nestedParsers.Add(string.Join("\n", nestedArrayFieldParsers));

                    nestedParsers.Add(Iso8583CodeTemplatesParse.ParseNestedArrayFieldEnd
                        .Replace("{FieldNumber}", nf.Number.ToString())
                        .Replace("{InnerProp}", nf.PropertyName.Split('.').Last())
                    );

                    continue;
                }

                var readMethod = GetReadMethod(nf.PropertyTypeDisplay);

                string nestedLine = readMethod is null
                    ? Iso8583CodeTemplatesParse.ParseUnsupportedField(nf.Number, nf.PropertyName, nf.PropertyTypeDisplay)
                    : Iso8583CodeTemplatesParse.ParseField(
                            nf.Number,
                            nf.PropertyName.Split('.').Last(),
                            "nested",
                            readMethod,
                            nf.Format,
                            nf.Length
                        );

                nestedParsers.Add(nestedLine);
            }

            sbNested.AppendLine(
                f.WithBitMapArray
                    ? Iso8583CodeTemplatesParse.ParseNestedMethod(f.Number, f.PropertyTypeDisplay, nestedParsers)
                    : Iso8583CodeTemplatesParse.ParseNestedMethodWithoutBitmap(f.Number, f.PropertyTypeDisplay, nestedParsers)
            );
        }
        else
        {
            var readMethod = GetReadMethod(f.PropertyTypeDisplay);

            string line = readMethod is null
                ? Iso8583CodeTemplatesParse.ParseUnsupportedField(f.Number, f.PropertyName, f.PropertyTypeDisplay)
                : Iso8583CodeTemplatesParse.ParseField(
                    f.Number,
                    f.PropertyName,
                    "response",
                    readMethod,
                    f.Format,
                    f.Length
                );

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
        "char" => "Char", // TODO: Пока так но потом добавить для чтения Char
        _ => null
    };

    #endregion
}