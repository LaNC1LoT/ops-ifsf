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

                    string nestedName = $"{prop.Name}.{nestedProp.Name}";
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

                // 2. Вложенные поля
                foreach (var nf in f.NestedFields.OrderBy(n => n.Number))
                {
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

