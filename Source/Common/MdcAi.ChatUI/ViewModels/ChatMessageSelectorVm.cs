namespace MdcAi.ChatUI.ViewModels;

using System.Windows.Forms;
using RxExt;

public class ChatMessageSelectorVm : ViewModel
{
    public ObservableCollection<ChatMessageVm> Versions { get; } = new();
    [Reactive] public int Version { get; private set; }
    [Reactive] public ChatMessageVm Message { get; set; }
    public ReactiveCommand<Unit, Unit> NextCmd { get; }
    public ReactiveCommand<Unit, Unit> PrevCmd { get; }
    public ReactiveCommand<Unit, Unit> DeleteCmd { get; }

    private readonly IConnectableObservable<Unit> _changed;

    public ChatMessageSelectorVm(ChatMessageVm message)
    {
        Versions.Add(Message = message);

        _changed = Observable.Merge(this.WhenAnyValue(vm => vm.Message)
                                        .Select(_ => Unit.Default),
                                    Versions.ObserveCollectionChanges()
                                            .Select(_ => Unit.Default))
                             .Publish();

        _changed.Connect();

        _changed.ObserveOnMainThread()
                .Do(_ => Version = GetCurrentIndex() + 1)
                .SubscribeSafe();

        PrevCmd = ReactiveCommand.Create(() => TraverseVersions(-1), CanTranverseLive(-1));
        NextCmd = ReactiveCommand.Create(() => TraverseVersions(1), CanTranverseLive(1));

        this.WhenAnyValue(vm => vm.Message)
            .PairWithPrevious()
            .Do(pair =>
            {
                var previous = pair.Item1?.Previous ?? pair.Item2?.Previous;

                if (previous == null)
                    return;

                foreach (var msg in previous.Selector.Versions)
                    msg.Next = pair.Item2;

                if (pair.Item1 != null && pair.Item2 == null)
                    pair.Item1.Previous = null; // Dereference
            })
            .SubscribeSafe();

        DeleteCmd = ReactiveCommand.Create(() =>
        {
            var ver = Version;
            Versions.Remove(Message);            
            Message = Versions.Any() ? Versions[Math.Min(ver, Versions.Count) - 1] : null;
        });
    }

    private int GetCurrentIndex() => Versions.IndexOf(Message);

    private bool CanTraverse(int dir)
    {
        var current = GetCurrentIndex();
        if (current == -1)
            return false;
        var proposed = current + dir;
        return proposed >= 0 && proposed < Versions.Count;
    }

    private IObservable<bool> CanTranverseLive(int dir) =>
        _changed.Select(_ => CanTraverse(dir))
                .ObserveOnMainThread();

    private void TraverseVersions(int direction)
    {
        if (!CanTraverse(direction))
            return;
        Message = Versions[GetCurrentIndex() + direction];
    }
}