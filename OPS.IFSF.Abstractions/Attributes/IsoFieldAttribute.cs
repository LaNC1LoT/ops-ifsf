namespace OPS.IFSF.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Property)]
public sealed class IsoFieldAttribute(int number, IsoFieldFormat format, int length, char? delimiter = null)
    : Attribute
{
    public int Number { get; } = number;
    public IsoFieldFormat Format { get; } = format;
    public int Length { get; } = length;
    public char? Delimiter { get; } = delimiter;
}
