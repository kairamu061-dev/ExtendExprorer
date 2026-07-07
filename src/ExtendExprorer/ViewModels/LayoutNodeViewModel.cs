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

    // [ObservableProperty] は AOT 非対応(MVVMTK0045)のため手書きプロパティにしている
    private double _ratio = 0.5;
    public double Ratio
    {
        get => _ratio;
        set => SetProperty(ref _ratio, value);
    }

    public required LayoutNodeViewModel First { get; set; }
    public required LayoutNodeViewModel Second { get; set; }
}
