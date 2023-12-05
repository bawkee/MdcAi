namespace MdcAi.Extensions;
using System;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

public static class ObjectExtensions
{
    /// <summary>
    /// Converts one type to another by combining multiple techniques.
    /// </summary>
    public static T ChangeType<T>(this object value) => (T)value.ChangeType(typeof(T));

    /// <summary>
    /// Converts one type to another by combining multiple techniques.
    /// </summary>
    /// <remarks>
    /// A common sense extension that will probably be introduced to .net at some point in the exact
    /// same fashion as done here.
    /// </remarks>
    public static object ChangeType(this object value, Type targetType)
    {
        if (targetType.IsNullableType())
        {
            if (value == null)
                return default; // No-brainer
            targetType = Nullable.GetUnderlyingType(targetType);
        }

        if (value == null)
        {
            if (targetType == typeof(string) || !targetType.IsValueType)
                return null;
            throw new InvalidCastException("Can't convert a null value to a non-nullable type.");
        }

        // If the value is a IConvertible type then we'll go with that. The IConvertible is an all or nothing
        // affair because you have no way of knowing whether something can be converted or not (other than catching
        // exceptions). Although, with standard built-in types at least we know that they can only convert between
        // themselves.
        if (value is IConvertible convertible)
        {
            if (convertible.GetTypeCode() != TypeCode.Object && // This is one of the standard convertible types
                !targetType.IsBuiltInConvertibleType()) // Target type is not one of the standard types neither
            {
                // Using IConvertible here is guaranteed to not work so we'll try TypeConverter
                if (TypeDescriptor.GetConverter(targetType) is { } converterFrom && converterFrom.IsValid(value))
                    return converterFrom.ConvertFrom(value); // Guaranteed to work
                                                             // Resort to explicit cast.
                return value;
            }

            // Use the standard converter mechanism.
            return Convert.ChangeType(value, targetType);
        }

        // This isn't an IConvertible so try other things
        var sourceType = value.GetType();

        // See if we're on the same thing (nullable and non-nullable are assignable)
        if (targetType == sourceType || sourceType.IsAssignableFrom(targetType))
            return value;

        // Try the TypeConverter
        if (TypeDescriptor.GetConverter(sourceType) is { } converterTo && converterTo.CanConvertTo(targetType))
            return converterTo.ConvertTo(value, targetType);

        // Final resort
        return value;
    }

    /// <summary>
    /// Returns an array with a single item containing the provided <paramref name="obj"/> value or null if the
    /// <paramref name="obj"/> is null.
    /// Used when array is expected but a single value is available.
    /// </summary>
    public static T[] ExpandToArray<T>(this T obj) => obj == null ? null : new[] { obj };
}