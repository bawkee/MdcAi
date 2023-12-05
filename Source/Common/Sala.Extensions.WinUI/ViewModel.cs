namespace Sala.Extensions.WinUI;

using ReactiveUI;

public class ViewModel : ReactiveObject
{
    protected ViewModelChangeTracker TrackChanges(params string[] properties) => new(this, properties);
}

public class TrackedChange
{
    public string PropertyName { get; }
    public object OriginalValue { get; } // The originally recorded value which makes it 'dirty'
    public bool IsDirty { get; }

    public TrackedChange(string propertyName, object originalValue, bool isDirty)
    {
        PropertyName = propertyName;
        OriginalValue = originalValue;
        IsDirty = isDirty;
    }
}