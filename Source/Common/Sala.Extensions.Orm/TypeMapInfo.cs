namespace Sala.Extensions.Orm;

using System.Reflection;

public class TypeMapInfo
{
    public TypeMapInfo(IReflect type)
    {
        Properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public)
                         .Where(p => p.GetCustomAttribute<DoNotMapAttribute>() == null &&
                                     p.GetIndexParameters().Length == 0)
                         .Select(p => new MappingProperty(p))
                         .ToArray();
        UsesMapAttributes = Properties.Any(p => p.HasAttribute);
    }

    public bool UsesMapAttributes { get; }
    public MappingProperty[] Properties { get; }

    public IEnumerable<MappingProperty> TargetProperties =>
        UsesMapAttributes ? Properties.Where(p => p.HasAttribute) : Properties;
}