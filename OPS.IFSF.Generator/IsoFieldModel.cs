﻿using Microsoft.CodeAnalysis;

namespace OPS.IFSF.Generator;

public sealed class IsoFieldModel(int number, string propertyName
    , string format, int length, ITypeSymbol typeSymbol, bool withBitMapArray, Char beforeDelimiter, Char itemSplitter)
{
    public int Number { get; } = number;
    public string PropertyName { get; } = propertyName;
    public string Format { get; } = format;
    public int Length { get; } = length;
    public ITypeSymbol TypeSymbol { get; } = typeSymbol;
    public List<IsoFieldModel> NestedFields { get; } = [];
    public List<IsoFieldModel> ItemFields { get; } = new();
    public Char BeforeDelimiter { get; } = beforeDelimiter;
    public string? ItemTypeDisplay { get; set; } 
    public Char ItemSplitter { get; } = itemSplitter;
    public bool IsArray { get; set; }
    public bool WithBitMapArray { get; set; } = withBitMapArray;
    public bool IsNested => NestedFields.Count > 0;
    public bool IsReferenceType => TypeSymbol.IsReferenceType;

    public bool IsNullable =>
        TypeSymbol.NullableAnnotation == NullableAnnotation.Annotated;

    public string PropertyTypeDisplay =>
        TypeSymbol is IArrayTypeSymbol arr
            ? arr.ToDisplayString()
            : IsNullable && TypeSymbol is INamedTypeSymbol named && named.IsGenericType
                ? named.TypeArguments[0].Name
                : TypeSymbol.Name;
    public string ToSummary()
    {
        return $"Number = {Number}, PropertyName = {PropertyName}, Format = {Format}, Length = {Length}";
    }
}
