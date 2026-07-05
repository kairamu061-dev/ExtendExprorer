using CommunityToolkit.Mvvm.ComponentModel;

namespace ExtendExprorer.ViewModels;

public enum SplitDirection { Horizontal, Vertical }

public abstract partial class LayoutNodeViewModel : ObservableObject
{
    public string Id { get; } = Guid.NewGuid().ToString();
}

public partial class SplitNodeViewModel : LayoutNodeViewModel
{
    public required SplitDirection Direction { get; init; }

    [ObservableProperty]
    private double ratio = 0.5;

    public required LayoutNodeViewModel First { get; set; }
    public required LayoutNodeViewModel Second { get; set; }
}
