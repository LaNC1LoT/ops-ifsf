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
        var formatFull = $"{enumValue.Type!.ToDisplayString()}.{formatName}";
        return new IsoFieldModel(number, propName, formatFull, length, typeSymbol);
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
                    var fieldCode = nf.IsNullable
                        ? Iso8583CodeTemplatesWrite.WriteNestedNullableField(
                            nf.Number, fullProp, nf.Format, nf.Length, nf.ToSummary(), f.Number.ToString(),
                            nf.IsReferenceType ? " != null" : ".HasValue",
                            nf.IsReferenceType ? "" : ".Value")
                        : Iso8583CodeTemplatesWrite.WriteNestedField(
                            nf.Number, fullProp, nf.Format, nf.Length, nf.ToSummary(), f.Number.ToString());

                    nestedWrites.Add(fieldCode);
                }

                sbNested.AppendLine(Iso8583CodeTemplatesWrite.WriteNestedMethod(f.Number, f.PropertyTypeDisplay, nestedWrites));
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
                    var readMethod = GetReadMethod(nf.PropertyTypeDisplay);
                    string nestedLine = readMethod is null
                        ? Iso8583CodeTemplatesParse.ParseUnsupportedField(nf.Number, nf.PropertyName, nf.PropertyTypeDisplay)
                        : Iso8583CodeTemplatesParse.ParseField(nf.Number, nf.PropertyName, "nested", readMethod, nf.Format, nf.Length);
                    nestedSwitches.Add(nestedLine);
                }

                sbNested.AppendLine(Iso8583CodeTemplatesParse.ParseNestedMethod(f.Number, f.PropertyTypeDisplay, nestedSwitches));
            }
            else
            {
                var readMethod = GetReadMethod(f.PropertyTypeDisplay);
                string line = readMethod is null
                    ? Iso8583CodeTemplatesParse.ParseUnsupportedField(f.Number, f.PropertyName, f.PropertyTypeDisplay)
                    : Iso8583CodeTemplatesParse.ParseField(f.Number, f.PropertyName, "response", readMethod, f.Format, f.Length);
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

