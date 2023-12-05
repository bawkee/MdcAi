namespace MdcAi.Extensions;

/// <summary>
/// "mystring123".IsNumeric(), typeof(...).IsNumericType() etc.
/// </summary>
public static class TypeExtensions
{
    private static readonly HashSet<Type> NumericTypes = new HashSet<Type>
    {
        typeof(byte),
        typeof(sbyte),
        typeof(ushort),
        typeof(uint),
        typeof(ulong),
        typeof(short),
        typeof(int),
        typeof(long),
        typeof(decimal),
        typeof(double),
        typeof(float)
    };

    private static readonly HashSet<Type> BuiltInConvertibleTypes = new HashSet<Type>
    {
        typeof(byte),
        typeof(DBNull),
        typeof(bool),
        typeof(char),
        typeof(DateTime),
        typeof(string),
        typeof(sbyte),
        typeof(ushort),
        typeof(uint),
        typeof(ulong),
        typeof(short),
        typeof(int),
        typeof(long),
        typeof(decimal),
        typeof(double),
        typeof(float)
    };

    private static readonly HashSet<Type> FloatingTypes = new HashSet<Type>
    {
        typeof(decimal),
        typeof(double),
        typeof(float)
    };

    public static bool IsBuiltInConvertibleType(this Type type) => BuiltInConvertibleTypes.Contains(type) ||
                                                                   BuiltInConvertibleTypes.Contains(Nullable.GetUnderlyingType(type));

    public static bool IsNumericType(this Type type) => NumericTypes.Contains(type) ||
                                                        NumericTypes.Contains(Nullable.GetUnderlyingType(type));

    public static bool IsFloatingNumber(this Type type) => FloatingTypes.Contains(type) ||
                                                           FloatingTypes.Contains(Nullable.GetUnderlyingType(type));

    public static bool IsDateType(this Type type) => type == typeof(DateTime) ||
                                                     Nullable.GetUnderlyingType(type) == typeof(DateTime);

    public static bool IsNullableType(this Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
}