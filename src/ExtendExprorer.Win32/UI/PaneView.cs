using ExtendExprorer.Services;
using ExtendExprorer.ViewModels;
using static ExtendExprorer.Interop.Win32;

namespace ExtendExprorer.UI;

/// <summary>ペイン 1 つ分の見た目。<c>[タブ帯][ナビ＋アドレス帯][一覧]</c> を縦に積む。
///
/// <para>分割されたペインはこれが複数並ぶ。それぞれが自分のタブの束と一覧を持ち、
/// 一覧の実体はペインごとに 1 つだけ（タブの枚数によらない）。</para></summary>
internal sealed class PaneView
{
    private readonly IFileSystemService _fs;

    internal PaneModel Model { get; }
    internal TabStripView TabStrip { get; }
    internal PaneBandView Band { get; }
    internal FileListView FileList { get; }

    private RECT _bounds;
    private uint _dpi = 96;

    /// <summary>このペインが操作された（キー操作の宛先を切り替える合図）。</summary>
    internal event Action<PaneView>? Activated;

    /// <summary>タブ帯の行数が変わって、内訳を取り直す必要がある。</summary>
    internal event Action? LayoutChanged;

    /// <summary>帯の分割ボタンが押された。割るのは <b>このペイン</b>。</summary>
    internal event Action<PaneView, SplitDirection>? SplitRequested;

    internal PaneView(IFileSystemService fs)
    {
        _fs = fs;
        Model = new PaneModel(fs);
        TabStrip = new TabStripView(Model);
        Band = new PaneBandView();
        FileList = new FileListView(Model.FileList);

        TabStrip.HeightChanged += () => LayoutChanged?.Invoke();
        TabStrip.Clicked += () => Activated?.Invoke(this);
        FileList.Focused += () => Activated?.Invoke(this);

        Band.Clicked += () => Activated?.Invoke(this);
        Band.BackRequested += () => Model.FileList.GoBack();
        Band.ForwardRequested += () => Model.FileList.GoForward();
        Band.UpRequested += () => Model.FileList.GoUp();
        Band.NavigateRequested += path => Model.FileList.Navigate(path);
        Band.SplitRequested += direction => SplitRequested?.Invoke(this, direction);
        Band.PathEntered += OnPathEntered;

        // 移動・タブ切替のたびに、パンくずと戻る/進む/上への有効・無効を作り直す
        Model.FileList.StateChanged += UpdateBand;
    }

    /// <summary>アドレスバーに入力されたパスの解決。ディスクを見るので非同期で、
    /// 結果は UI スレッドへ戻してから反映する。</summary>
    private void OnPathEntered(string input)
    {
        _ = Task.Run(async () =>
        {
            string? target;
            try
            {
                target = await _fs.ResolveNavigationTargetAsync(input);
            }
            catch (Exception ex)
            {
                Diagnostics.Report($"PaneView.ResolveNavigationTarget({input})", ex);
                target = null;
            }
            UiDispatcher.Post(() =>
            {
                if (target is null)
                {
                    // 見つからない。編集は続けたまま知らせる（address-bar 仕様）
                    Band.ShowPathNotFound();
                    return;
                }
                Model.FileList.Navigate(target);
            });
        });
    }

    private void UpdateBand()
    {
        var list = Model.FileList;
        Band.CanGoBack = list.CanGoBack;
        Band.CanGoForward = list.CanGoForward;
        Band.CanGoUp = list.CanGoUp;
        Band.SetPath(list.Path);
    }

    internal void Create(nint parent, nint instance, RECT bounds, nint font, uint dpi)
    {
        _bounds = bounds;
        _dpi = dpi;
        var (tab, band, list) = Split(bounds);
        TabStrip.Create(parent, instance, tab, font, dpi);
        Band.Create(parent, instance, band, font, dpi);
        FileList.Create(parent, instance, list, font, dpi);
        UpdateBand();
    }

    internal void SetBounds(RECT bounds)
    {
        _bounds = bounds;
        var (tab, band, list) = Split(bounds);
        TabStrip.SetBounds(tab);
        Band.SetBounds(band);
        FileList.SetBounds(list);
    }

    internal void SetFont(nint font, uint dpi)
    {
        _dpi = dpi;
        TabStrip.SetFont(font, dpi);
        Band.SetFont(font, dpi);
        SetBounds(_bounds);
    }

    /// <summary>上からタブ帯・ナビ帯・一覧。タブ帯の高さは折り返しの行数で変わる。
    /// 帯は、残りの高さが足りないときは削る（一覧を先に潰さない）。</summary>
    private (RECT Tab, RECT Band, RECT List) Split(RECT bounds)
    {
        var tabHeight = Math.Min(TabStrip.PreferredHeight, Math.Max(0, bounds.Height));
        var tabBottom = bounds.Top + tabHeight;
        var bandHeight = Math.Min(Scale(PaneBandView.Height, _dpi), Math.Max(0, bounds.Bottom - tabBottom));
        var bandBottom = tabBottom + bandHeight;
        return (bounds with { Bottom = tabBottom },
                bounds with { Top = tabBottom, Bottom = bandBottom },
                bounds with { Top = bandBottom });
    }

    internal void Focus() => FileList.Focus();

    internal void Dispose()
    {
        Band.Destroy();
        Model.Dispose();
    }
}
