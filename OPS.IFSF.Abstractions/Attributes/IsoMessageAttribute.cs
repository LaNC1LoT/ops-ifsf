namespace OPS.IFSF.Abstractions.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public sealed class IsoMessageAttribute(string messageId)
    : Attribute
{
    public string MessageId { get; } = messageId;
}
