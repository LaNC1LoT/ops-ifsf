namespace OPS.IFSF.Generator;

public sealed class MessageClassModel(string @namespace, string className, string messageId)
{
    public string Namespace { get; } = @namespace;
    public string ClassName { get; } = className;
    public string MessageId { get; } = messageId;
    public List<IsoFieldModel> Fields { get; } = [];
}
