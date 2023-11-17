namespace Sala.Extensions.Orm;

using System.Reflection;

public class MappingProperty
{
    public MappingProperty(PropertyInfo propInfo)
    {
        PropInfo = propInfo;
        Attribute = propInfo.GetCustomAttribute<MapAttribute>();
        HasAttribute = Attribute != null;

        if (HasAttribute && !string.IsNullOrEmpty(Attribute.ToField))
            MapTo = Attribute.ToField;
        else
            MapTo = PropInfo.Name;
    }

    public PropertyInfo PropInfo { get; }
    public MapAttribute Attribute { get; }
    public bool HasAttribute { get; }
    public string MapTo { get; }
}