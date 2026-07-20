using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ExtendExprorer.Views;

/// <summary>フォルダパスのブレッドクラム表示 ＋ 編集可能パス（address-bar）。
/// 各フォルダ名クリックでそのフォルダへ移動、余白クリックでフルパス全選択の編集モードへ。
/// 実際の移動判定・実行は呼び出し元（PaneView → TabViewModel.TryNavigateAsync）に委ねる。</summary>
public sealed partial class AddressBar : UserControl
{
    private string _currentPath = "";
    private bool _editing;
    private readonly DispatcherTimer _errorTimer = new() { Interval = TimeSpan.FromSeconds(3) };

    /// <summary>移動要求。存在すれば true（呼び出し側が検証・移動）。false なら「パスが見つかりません」を表示。</summary>
    public Func<string, Task<bool>>? NavigateRequested;

    public AddressBar()
    {
        InitializeComponent();
        _errorTimer.Tick += (_, _) => HideError();
    }

    /// <summary>現在パスを反映してブレッドクラムを組み直す（PaneView がタブのパス変更時に呼ぶ）。</summary>
    public void SetPath(string path)
    {
        _currentPath = path ?? "";
        // 移動が起きたら編集モードは解除（別タブ切替・履歴移動など）
        if (_editing)
        {
            ExitEdit(revert: false);
        }
        BuildBreadcrumb(_currentPath);
    }

    private void BuildBreadcrumb(string path)
    {
        Segments.Children.Clear();
        if (string.IsNullOrEmpty(path))
        {
            return;
        }
        var trimmed = path.TrimEnd('\\', '/');
        var root = System.IO.Path.GetPathRoot(trimmed) ?? "";
        if (string.IsNullOrEmpty(root))
        {
            // ルートが取れない特殊パスは 1 セグメントで表示（編集は可能）
            AddSegment(trimmed, trimmed);
            return;
        }

        var rootName = root.TrimEnd('\\', '/');
        AddSegment(rootName.Length == 0 ? root : rootName, root);

        var cumulative = root;
        var rest = trimmed.Length > root.Length ? trimmed.Substring(root.Length) : "";
        foreach (var part in rest.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries))
        {
            cumulative = System.IO.Path.Combine(cumulative, part);
            AddSeparator();
            AddSegment(part, cumulative);
        }
    }

    private void AddSegment(string name, string fullPath)
    {
        var button = new Button
        {
            Content = name,
            Tag = fullPath,
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(6, 2, 6, 2),
            FontSize = 12,
            MinWidth = 0,
            MinHeight = 0, // 既定の 32px が 28px バーを超えないよう抑える
            VerticalAlignment = VerticalAlignment.Center,
        };
        button.Click += OnSegmentClick;
        Segments.Children.Add(button);
    }

    private void AddSeparator()
    {
        Segments.Children.Add(new TextBlock
        {
            Text = "\u203A", // › セグメント区切り
            FontSize = 12,
            Foreground = new SolidColorBrush(Windows.UI.Color.FromArgb(0xFF, 0x80, 0x80, 0x80)),
            VerticalAlignment = VerticalAlignment.Center,
        });
    }

    private async void OnSegmentClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string fullPath } && NavigateRequested is not null)
        {
            // 祖先フォルダは必ず存在するので結果は基本 true。SetPath は移動後に呼ばれて組み直される
            await NavigateRequested(fullPath);
        }
    }

    private void OnBackgroundTapped(object sender, TappedRoutedEventArgs e)
    {
        // セグメントのボタン上のタップは無視（余白のタップだけ編集モードにする）
        if (IsWithinButton(e.OriginalSource as DependencyObject))
        {
            return;
        }
        EnterEdit();
    }

    private static bool IsWithinButton(DependencyObject? source)
    {
        while (source is not null)
        {
            if (source is Button)
            {
                return true;
            }
            source = VisualTreeHelper.GetParent(source);
        }
        return false;
    }

    private void EnterEdit()
    {
        _editing = true;
        HideError();
        Editor.Text = _currentPath;
        BreadcrumbRoot.Visibility = Visibility.Collapsed;
        Editor.Visibility = Visibility.Visible;
        Editor.Focus(FocusState.Programmatic);
        Editor.SelectAll();
    }

    private void ExitEdit(bool revert)
    {
        _editing = false;
        Editor.Visibility = Visibility.Collapsed;
        BreadcrumbRoot.Visibility = Visibility.Visible;
        if (revert)
        {
            Editor.Text = _currentPath;
        }
    }

    private async void OnEditorKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            e.Handled = true;
            var text = Editor.Text.Trim();
            if (text.Length == 0 || NavigateRequested is null)
            {
                ExitEdit(revert: true);
                return;
            }
            if (await NavigateRequested(text))
            {
                // 成功: 移動により SetPath が呼ばれてブレッドクラムが更新される
                ExitEdit(revert: false);
            }
            else
            {
                // 失敗: 編集を続けたままエラー表示（3 秒）
                ShowError();
            }
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            e.Handled = true;
            ExitEdit(revert: true);
        }
    }

    private void OnEditorLostFocus(object sender, RoutedEventArgs e)
    {
        // Enter 以外でフォーカスが外れたら元のパス表示に戻す
        if (_editing)
        {
            ExitEdit(revert: true);
        }
    }

    private void ShowError()
    {
        ErrorTip.Visibility = Visibility.Visible;
        _errorTimer.Stop();
        _errorTimer.Start();
    }

    private void HideError()
    {
        _errorTimer.Stop();
        ErrorTip.Visibility = Visibility.Collapsed;
    }
}
