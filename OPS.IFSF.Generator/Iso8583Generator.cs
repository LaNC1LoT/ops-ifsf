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
        foreach (var prop in classSymbol.GetMembers().OfType<IPropertySymbol>())
        {
            var attr = prop.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "IsoFieldAttribute");
            if (attr is null) continue;

            var fieldModel = GetIsoFieldModel(attr, prop.Name, prop.Type);

            if (prop.Type.TypeKind == TypeKind.Class)
            {
                foreach (var nestedProp in prop.Type.GetMembers().OfType<IPropertySymbol>())
                {
                    var na = nestedProp.GetAttributes().FirstOrDefault(a => a.AttributeClass?.Name == "IsoFieldAttribute");
                    if (na is null)
                        continue;

                    string nestedName = nestedProp.Name;
                    var nestedFieldModel = GetIsoFieldModel(na, nestedName, nestedProp.Type);
                    fieldModel.NestedFields.Add(nestedFieldModel);
                }
            }
            else if (prop.Type.TypeKind == TypeKind.Array)
            {

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
        var underlyingType = typeSymbol switch
        {
            IArrayTypeSymbol arr when arr.ElementType.SpecialType == SpecialType.System_Byte => arr.ToDisplayString(),
            INamedTypeSymbol named when isNullable && named.IsGenericType => named.TypeArguments[0].Name,
            _ => typeSymbol.Name
        };
        var formatFull = $"{enumValue.Type!.ToDisplayString()}.{formatName}";
        return new IsoFieldModel(number, propName, formatFull, length, underlyingType, isNullable, typeSymbol.IsReferenceType);
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
                sb.AppendLine(
                    Iso8583CodeTemplates.WriteNestedHeader
                        .Replace("{Number}", f.Number.ToString())
                );

                foreach (var nf in f.NestedFields.OrderBy(n => n.Number))
                {
                    var fullProp = $"{f.PropertyName}.{nf.PropertyName}";

                    sb.AppendLine(
                        GenerateWriteField(number: nf.Number, prop: fullProp, format: nf.Format, length: nf.Length,
                            comment: nf.ToSummary(), isNullable: nf.IsNullable, isReferenceType: nf.IsReferenceType,
                            parentNumber: f.Number.ToString()));
                }

                sb.AppendLine(
                    Iso8583CodeTemplates.WriteNestedFooter
                        .Replace("{Number}", f.Number.ToString())
                );
            }
            else
            {
                sb.AppendLine(
                    GenerateWriteField(number: f.Number, prop: f.PropertyName, format: f.Format, length: f.Length,
                        comment: f.ToSummary(), isNullable: f.IsNullable, isReferenceType: f.IsReferenceType, parentNumber: "0"));
            }
        }

        sb.Append(Iso8583CodeTemplates.WriteToFooter);
        return sb.ToString();
    }

    private static string GenerateWriteField(int number, string prop, string format, int length,
        string comment, bool isNullable, bool isReferenceType, string parentNumber)
    {
        var tpl = isNullable
            ? Iso8583CodeTemplates.WriteNullableField
            : Iso8583CodeTemplates.WriteField;

        return tpl
            .Replace("{Cond}", isReferenceType ? " != null" : ".HasValue")
            .Replace("{Value}", isReferenceType ? string.Empty : ".Value")
            .Replace("{Comment}", comment)
            .Replace("{Prop}", prop)
            .Replace("{Format}", format)
            .Replace("{Length}", length.ToString())
            .Replace("{Number}", number.ToString())
            .Replace("{ParentNumber}", parentNumber);
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
                sb.AppendLine(
                    Iso8583CodeTemplates.ParseNestedHeader
                        .Replace("{Number}", f.Number.ToString())
                        .Replace("{PropClass}", f.PropertyType)
                );

                foreach (var nf in f.NestedFields.OrderBy(n => n.Number))
                {
                    string? readMethodNested = GetReadMethod(nf.PropertyType);
                    string nestedTarget = $"nested{f.Number}";
                    sb.AppendLine(
                        GenerateParseField(nf.Number, nf.PropertyName, nestedTarget,
                            readMethodNested, nf.Format, nf.Length, nf.PropertyType)
                    );
                }

                sb.AppendLine(
                    Iso8583CodeTemplates.ParseNestedFooter
                        .Replace("{ParentNumber}", f.Number.ToString())
                        .Replace("{Prop}", f.PropertyName)
                        .Replace("{Number}", f.Number.ToString())
                );
            }
            else
            {
                string? readMethod = GetReadMethod(f.PropertyType);
                sb.AppendLine(
                    GenerateParseField(f.Number, f.PropertyName, "response",
                        readMethod, f.Format, f.Length, f.PropertyType)
                );
            }
        }

        sb.Append(Iso8583CodeTemplates.ParseFooter);
        return sb.ToString();
    }

    private static string GenerateParseField(int number, string prop, string target,
        string? readMethod, string format, int length, string type)
    {
        var tpl = readMethod is null
            ? Iso8583CodeTemplates.ParseUnsupportedField
            : Iso8583CodeTemplates.ParseField;

        return tpl
            .Replace("{Number}", number.ToString())
            .Replace("{Prop}", prop)
            .Replace("{Target}", target)
            .Replace("{ReadMethod}", readMethod ?? "")
            .Replace("{Format}", format)
            .Replace("{Length}", length.ToString())
            .Replace("{Type}", type);
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

