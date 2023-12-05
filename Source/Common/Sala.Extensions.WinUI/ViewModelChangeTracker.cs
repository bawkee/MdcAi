namespace Sala.Extensions.WinUI;

using System.Collections;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using MdcAi.Extensions;

// TODO: Use nuget packages for stuff like this, where possible

/// <summary>
/// Tracks all the properties which fire change notifications in order to determine whether a property is dirty or not.
/// </summary>
public class ViewModelChangeTracker : IDisposable, IObservable<TrackedChange>
{
    private readonly Dictionary<string, object> _values = new();
    private readonly ViewModel _vm;
    private readonly CompositeDisposable _disposables = new();
    private readonly Subject<TrackedChange> _notifier = new();
    private bool _suspend;

    public ViewModelChangeTracker(ViewModel vm, string[] properties = null)
    {
        _vm = vm;

        bool PropRelevant(string prop) => properties == null ||
                                          properties.Length == 0 ||
                                          properties.Contains(prop);

        bool ShouldRecord(string prop) => PropRelevant(prop) && !_values.ContainsKey(prop);

        Observable.Merge(
                      _vm.Changed
                         .Select(c => c.PropertyName)
                         .Where(PropRelevant)
                         .Do(FireNotification),
                      _vm.Changing
                         .Select(c => c.PropertyName)
                         .Where(ShouldRecord)
                         .Do(RecordValue))
                  .Subscribe()
                  .DisposeWith(_disposables);
    }

    private object GetCurrentValue(string propertyName)
    {
        var type = _vm.GetType();
        var prop = type.GetProperty(propertyName, BindingFlags.Public | BindingFlags.Instance);
        if (prop == null)
            throw new Exception($"Could not find property {propertyName} on Type {type.Name}");
        return prop.GetValue(_vm);
    }

    private void RecordValue(string propertyName)
    {
        _values[propertyName] = GetCurrentValue(propertyName);
        FireNotification(propertyName);
    }

    private void FireNotification(string propertyName)
    {
        if (_suspend)
            return;
        _notifier.OnNext(new TrackedChange(
                             propertyName,
                             _values[propertyName],
                             IsDirty(propertyName)));
    }

    /// <summary>
    /// Resets changes so that it's no longer dirty. First change will define the default value and 2nd change
    /// will mark the property as dirty.
    /// </summary>
    public ViewModelChangeTracker Reset()
    {
        foreach (var propertyName in _values.Keys)
            Reset(propertyName);
        return this;
    }

    public ViewModelChangeTracker Reset(string propertyName)
    {
        if (_values.ContainsKey(propertyName))
            _values.Remove(propertyName);
        return this;
    }

    /// <summary>
    /// Marks current state of object as clean (not dirty).
    /// </summary>
    public ViewModelChangeTracker Clean()
    {
        foreach (var propertyName in _values.Keys.ToArray())
            RecordValue(propertyName);
        return this;
    }

    public ViewModelChangeTracker Clean(string propertyName)
    {
        if (_values.ContainsKey(propertyName))
            RecordValue(propertyName);
        return this;
    }

    public bool IsDirty() => _values.Keys.Any(IsDirty);

    public bool IsDirty(string propertyName)
    {
        if (!_values.TryGetValue(propertyName, out var value))
            return false;
        return Comparer.Default.Compare(GetCurrentValue(propertyName), value) != 0;
    }

    public void Dispose() => _disposables.Dispose();

    // Subscribe to a stream which ticks only when IsDirty changes for each property. It disposes 
    // along with the class.
    public IDisposable Subscribe(IObserver<TrackedChange> observer) =>
        _notifier.DistinctUntilChanged(c => (c.PropertyName, c.IsDirty))
                 .Subscribe(observer)
                 .DisposeWith(_disposables);

    public IObservable<TrackedChange> WatchProperties(params string[] names) =>
        this.Where(c => names.Contains(c.PropertyName));

    public IDisposable TemporarySuspend()
    {
        _suspend = true;
        return Disposable.Create(() => _suspend = false);
    }

    public object GetOriginalValue(string propertyName) =>
        _values.GetValueOrDefault(propertyName);

    public T GetOriginalValue<T>(string propertyName) =>
        GetOriginalValue(propertyName).ChangeType<T>();

    public bool GetOriginalValue(string propertyName, out object val) =>
        _values.TryGetValue(propertyName, out val);
}