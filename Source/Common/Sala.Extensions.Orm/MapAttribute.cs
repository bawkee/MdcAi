namespace Sala.Extensions.Orm;

public class MapAttribute : Attribute
{
    public string ToField { get; set; }
}

public class DontMapAttribute : Attribute { }