namespace OPS.IFSF.Generator;

public sealed class IsoFieldModel(int number, string propertyName
    , string format, int length, string propertyType, bool isNullable)
{
    public int Number { get; } = number;
    public string PropertyName { get; } = propertyName;
    public string Format { get; } = format;
    public int Length { get; } = length;
    public string PropertyType { get; } = propertyType;
    public bool IsNullable { get; } = isNullable;
    public List<IsoFieldModel> NestedFields { get; } = [];
    public bool IsNested => NestedFields.Count > 0;
    public string ToSummary()
    {
        return $"Number = {Number}, PropertyName = {PropertyName}, Format = {Format}, Length = {Length}";
    }
}
