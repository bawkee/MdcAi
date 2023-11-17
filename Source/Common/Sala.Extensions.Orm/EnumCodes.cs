namespace Sala.Extensions.Orm;

public static class EnumCodes
{
    public static string ToCode(Enum value)
    {
        var type = value.GetType();
        return type.GetField(Enum.GetName(type, value))
                   .GetCustomAttributes(false)
                   .OfType<CodeAttribute>()
                   .SingleOrDefault()
                   ?.Code ?? throw new NotSupportedException();
    }

    public static TEnum FromCode<TEnum>(string code, TEnum defaultValue = default)
        where TEnum : struct, Enum
    {
        return (TEnum)FromCode(code, typeof(TEnum), defaultValue);
    }

    public static Enum FromCode(string code, Type enumType, Enum defaultValue = default)
    {
        foreach (var field in enumType.GetFields())
            if (Attribute.GetCustomAttribute(field, typeof(CodeAttribute)) is CodeAttribute attribute)
                if (string.Compare(attribute.Code, code ?? "", StringComparison.Ordinal) == 0)
                    return (Enum)Convert.ChangeType(field.GetValue(null), enumType);
        return defaultValue;
    }
}