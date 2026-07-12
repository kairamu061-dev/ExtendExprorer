using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ExtendExprorer.ViewModels;

/// <summary>folder-tree の 1 ノード。子は展開時に遅延読込される。</summary>
public sealed partial class FolderNodeViewModel : ObservableObject
{
    public string Name { get; }
    public string Path { get; }
    public string Glyph { get; }

    // 隠し・システム属性は file-list と同じく行全体を減光する
    public double RowOpacity { get; }

    public ObservableCollection<FolderNodeViewModel> Children { get; } = new();

    // [ObservableProperty] は AOT 非対応(MVVMTK0045)のため手書きプロパティにしている
    private bool _hasUnrealizedChildren = true;
    public bool HasUnrealizedChildren
    {
        get => _hasUnrealizedChildren;
        set => SetProperty(ref _hasUnrealizedChildren, value);
    }

    /// <summary>展開の再入防止（Expanding が読込完了前に再発火した場合用）。</summary>
    public bool IsLoadingChildren { get; set; }

    public FolderNodeViewModel(string name, string path, bool isHiddenOrSystem, string glyph = "\uE8B7")
    {
        Name = name;
        Path = path;
        RowOpacity = isHiddenOrSystem ? 0.55 : 1.0;
        Glyph = glyph;
    }

    // UI オートメーション / ナレーターがノード名を読めるようにする
    public override string ToString() => Name;
}
