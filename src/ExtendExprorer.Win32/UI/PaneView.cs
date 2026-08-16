using ExtendExprorer.Services;
using ExtendExprorer.ViewModels;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>ペイン 1 つ分の見た目。<c>[タブ帯][一覧]</c> を縦に積む。
///
/// <para>分割されたペインはこれが複数並ぶ。それぞれが自分のタブの束と一覧を持ち、
/// 一覧の実体はペインごとに 1 つだけ（タブの枚数によらない）。</para></summary>
internal sealed class PaneView
{
    internal PaneModel Model { get; }
    internal TabStripView TabStrip { get; }
    internal FileListView FileList { get; }

    private RECT _bounds;

    /// <summary>このペインが操作された（キー操作の宛先を切り替える合図）。</summary>
    internal event Action<PaneView>? Activated;

    /// <summary>タブ帯の行数が変わって、内訳を取り直す必要がある。</summary>
    internal event Action? LayoutChanged;

    internal PaneView(IFileSystemService fs)
    {
        Model = new PaneModel(fs);
        TabStrip = new TabStripView(Model);
        FileList = new FileListView(Model.FileList);
        TabStrip.HeightChanged += () => LayoutChanged?.Invoke();
        TabStrip.Clicked += () => Activated?.Invoke(this);
        FileList.Focused += () => Activated?.Invoke(this);
    }

    internal void Create(nint parent, nint instance, RECT bounds, nint font, uint dpi)
    {
        _bounds = bounds;
        var (tab, list) = Split(bounds);
        TabStrip.Create(parent, instance, tab, font, dpi);
        FileList.Create(parent, instance, list, font, dpi);
    }

    internal void SetBounds(RECT bounds)
    {
        _bounds = bounds;
        var (tab, list) = Split(bounds);
        TabStrip.SetBounds(tab);
        FileList.SetBounds(list);
    }

    internal void SetFont(nint font, uint dpi)
    {
        TabStrip.SetFont(font, dpi);
        SetBounds(_bounds);
    }

    /// <summary>上にタブ帯、残りが一覧。帯の高さは折り返しの行数で変わる。</summary>
    private (RECT Tab, RECT List) Split(RECT bounds)
    {
        var height = Math.Min(TabStrip.PreferredHeight, Math.Max(0, bounds.Height));
        return (bounds with { Bottom = bounds.Top + height },
                bounds with { Top = bounds.Top + height });
    }

    internal void Focus() => FileList.Focus();

    internal void Dispose() => Model.Dispose();
}
