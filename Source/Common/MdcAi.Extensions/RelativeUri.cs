namespace MdcAi.Extensions;

public class RelativeUri : Uri
{
    public RelativeUri(string uriString)
        : base(uriString, UriKind.Relative)
    {
        // Stupid I know but after 3000 uses it begins to make a difference, like,
        // how fucking often do you use an absolute Uri? 
    }
}