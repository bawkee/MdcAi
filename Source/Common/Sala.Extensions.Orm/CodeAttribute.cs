namespace Sala.Extensions.Orm;

[AttributeUsage(AttributeTargets.Field)]
public class CodeAttribute : Attribute
{
    public static readonly CodeAttribute Default = new CodeAttribute();

    public CodeAttribute()
        : this(string.Empty) { }

    public CodeAttribute(string code) { Code = code; }

    public virtual string Code { get; }

    public override bool Equals(object obj)
    {
        if (obj == this)
            return true;
        if (obj is CodeAttribute attr)
            return attr.Code == Code;
        return false;
    }

    public override int GetHashCode() => Code.GetHashCode();

    public override bool IsDefaultAttribute() => Equals(Default);
}