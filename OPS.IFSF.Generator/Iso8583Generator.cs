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
        var sb = new StringBuilder();
        sb.AppendLine(Iso8583CodeTemplates.WriteToHeader(model.Namespace, model.ClassName, model.MessageId));

        foreach (var f in model.Fields.OrderBy(f => f.Number))
        {
            if (f.IsNested)
            {
                sb.AppendLine(Iso8583CodeTemplates.WriteNestedHeader(f.Number));

                foreach (var nf in f.NestedFields.OrderBy(n => n.Number))
                {
                    var fullProp = $"{f.PropertyName}.{nf.PropertyName}";
                    string code = nf.IsNullable
                        ? Iso8583CodeTemplates.WriteNullableField(
                            nf.Number, fullProp, nf.Format, nf.Length, nf.ToSummary(),
                            f.Number.ToString(),
                            nf.IsReferenceType ? " != null" : ".HasValue",
                            nf.IsReferenceType ? "" : ".Value")
                        : Iso8583CodeTemplates.WriteField(
                            nf.Number, fullProp, nf.Format, nf.Length, nf.ToSummary(), f.Number.ToString());

                    sb.AppendLine(code);
                }

                sb.AppendLine(Iso8583CodeTemplates.WriteNestedFooter(f.Number));
            }
            else
            {
                string code = f.IsNullable
                    ? Iso8583CodeTemplates.WriteNullableField(
                        f.Number, f.PropertyName, f.Format, f.Length, f.ToSummary(),
                        "0", f.IsReferenceType ? " != null" : ".HasValue",
                        f.IsReferenceType ? "" : ".Value")
                    : Iso8583CodeTemplates.WriteField(
                        f.Number, f.PropertyName, f.Format, f.Length, f.ToSummary(), "0");

                sb.AppendLine(code);
            }
        }

        sb.AppendLine(Iso8583CodeTemplates.WriteToFooter());
        return sb.ToString();
    }

    #endregion

    #region Reader

    private static string GenerateParse(MessageClassModel model)
    {
        var sb = new StringBuilder();
        sb.AppendLine(Iso8583CodeTemplates.ParseHeader(model.Namespace, model.ClassName));

        foreach (var f in model.Fields.OrderBy(f => f.Number))
        {
            if (f.IsNested)
            {
                sb.AppendLine(Iso8583CodeTemplates.ParseNestedHeader(f.Number, f.PropertyType));

                foreach (var nf in f.NestedFields.OrderBy(n => n.Number))
                {
                    string? readMethod = GetReadMethod(nf.PropertyType);
                    string target = $"nested{f.Number}";

                    string code = readMethod is null
                        ? Iso8583CodeTemplates.ParseUnsupportedField(nf.Number, nf.PropertyName, nf.PropertyType)
                        : Iso8583CodeTemplates.ParseField(nf.Number, nf.PropertyName, target, readMethod, nf.Format, nf.Length);

                    sb.AppendLine(code);
                }

                sb.AppendLine(Iso8583CodeTemplates.ParseNestedFooter(f.Number, f.PropertyName, f.Number));
            }
            else
            {
                string? readMethod = GetReadMethod(f.PropertyType);

                string code = readMethod is null
                    ? Iso8583CodeTemplates.ParseUnsupportedField(f.Number, f.PropertyName, f.PropertyType)
                    : Iso8583CodeTemplates.ParseField(f.Number, f.PropertyName, "response", readMethod, f.Format, f.Length);

                sb.AppendLine(code);
            }
        }

        sb.AppendLine(Iso8583CodeTemplates.ParseFooter());
        return sb.ToString();

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

