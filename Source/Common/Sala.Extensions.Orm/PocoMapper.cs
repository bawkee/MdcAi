namespace Sala.Extensions.Orm;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public static class PocoMapper
{    
    public static IDictionary<string, object> MapClr(object source) =>
        source.GetType()
              .GetMappingInfo()
              .Properties
              .ToDictionary(k => k.PropInfo.Name, v => v.PropInfo.GetValue(source));

    public static void Map(object source, object dest, Func<PropertyInfo, string> mapField = null) =>
        Map(MapClr(source), dest, mapField);

    public static void Map(IDictionary<string, object> source, object dest, Func<PropertyInfo, string> mapField = null)
    {
        var mapInfo = dest.GetType().GetMappingInfo();
        var caseInsSource = new Dictionary<string, object>(source, StringComparer.OrdinalIgnoreCase);

        foreach (var p in mapInfo.TargetProperties)
        {
            var fieldName = mapField == null ? p.MapTo : mapField(p.PropInfo);

            if (!caseInsSource.TryGetValue(fieldName, out var value))
                continue;

            if (value is string stringValue && stringValue == "")
                value = null;

            if (value != null)
            {
                if (p.PropInfo.PropertyType.IsEnum)
                    value = EnumCodes.FromCode(value.ToString(), p.PropInfo.PropertyType);
                else
                    value = ChangeType(value, p.PropInfo.PropertyType);
            }

            p.PropInfo.SetValue(dest, value);
        }
    }

    private static object ChangeType(object value, Type type)
    {
        var underlyingType = Nullable.GetUnderlyingType(type) ?? type;
        return Convert.ChangeType(value, underlyingType);
    }

    public static void Map(IReadOnlyDictionary<string, object> source, object dest) =>
        Map((IDictionary<string, object>)source, dest);

    public static T MapFrom<T>(IDictionary<string, object> source)
    {
        var obj = Activator.CreateInstance<T>();
        Map(source, obj);
        return obj;
    }

    public static T MapFrom<T>(IReadOnlyDictionary<string, object> source) =>
        MapFrom<T>((IDictionary<string, object>)source);

    public static T MapFrom<T>(object source) =>
        MapFrom<T>(MapClr(source));

    public static IEnumerable<T> MapFrom<T>(IEnumerable source) =>
        source.Cast<object>().Select(MapFrom<T>);

    public static IEnumerable<T> MapFrom<T>(IEnumerable<IDictionary<string, object>> source) =>
        source.Select(MapFrom<T>);

    public static IEnumerable<T> MapFrom<T>(IEnumerable<IReadOnlyDictionary<string, object>> source) =>
        source.Select(MapFrom<T>);

    /// <summary>
    /// If <paramref name="source"/> is null it returns a default instance of <typeparamref name="TIn"/>. Otherwise, it returns the
    /// instance created by <paramref name="mapping"/> delegate.
    /// </summary>
    public static TOut MapTo<TIn, TOut>(this TIn source, Func<TIn, TOut> mapping) => source == null ? default : mapping(source);

    public static TOut MapTo<TOut>(this object source) => MapFrom<TOut>(source);

    public static void MapTo<TOut>(this object source, TOut dest) => Map(source, dest);

    public static IEnumerable<TOut> MapTo<TOut>(this IEnumerable source) =>
        MapFrom<TOut>(source);

    public static TOut MapTo<TOut>(this IDictionary<string, object> source) =>
        MapFrom<TOut>(source);

    public static TypeMapInfo GetMappingInfo(this Type type) =>
        new TypeMapInfo(type);

    public static void TrimStrings(object source)
    {
        foreach (var prop in source.GetType()
                                   .GetMappingInfo()
                                   .Properties)
        {
            if (prop.PropInfo.GetSetMethod() != null && prop.PropInfo.PropertyType == typeof(string))
                prop.PropInfo.SetValue(source, ((string)prop.PropInfo.GetValue(source))?.Trim());
        }
    }
}