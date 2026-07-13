using CommunityToolkit.Mvvm.ComponentModel;
using ExtendExprorer.Models;
using ExtendExprorer.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace ExtendExprorer.ViewModels;

public sealed partial class EntryViewModel : ObservableObject
{
    public Entry Model { get; }
    public string FullPath { get; }

    public string Name => Model.Name;
    public bool IsDirectory => Model.IsDirectory;
    public string TypeLabel { get; }
    public string SizeLabel { get; }
    public string ModifiedLabel { get; }
    // Segoe Fluent Icons: フォルダ / ドキュメント（シェルアイコン解決までのフォールバック）
    public string Glyph => IsDirectory ? "\uE8B7" : "\uE7C3";

    // 隠し・システム属性は行全体を減光して薄灰色に見せる
    public double RowOpacity => Model.IsHiddenOrSystem ? 0.55 : 1.0;

    // shell-icons: 初回アクセス（=行の実体化）で読込を開始する。UI スレッド専用
    private ImageSource? _icon;
    private bool _iconRequested;
    public ImageSource? Icon
    {
        get
        {
            if (!_iconRequested)
            {
                _iconRequested = true;
                _ = LoadIconAsync();
            }
            return _icon;
        }
    }

    public Visibility FallbackIconVisibility => _icon is null ? Visibility.Visible : Visibility.Collapsed;

    private async Task LoadIconAsync()
    {
        var icon = await ShellIconCache.GetAsync(FullPath, IsDirectory);
        if (icon is not null)
        {
            _icon = icon;
            OnPropertyChanged(nameof(Icon));
            OnPropertyChanged(nameof(FallbackIconVisibility));
        }
    }

    public EntryViewModel(Entry model, string folderPath)
    {
        Model = model;
        FullPath = System.IO.Path.Combine(folderPath, model.Name);
        TypeLabel = model.IsDirectory
            ? "フォルダ"
            : System.IO.Path.GetExtension(model.Name) is { Length: > 1 } ext
                ? ext.TrimStart('.').ToUpperInvariant()
                : "ファイル";
        SizeLabel = model.IsDirectory ? "—" : FormatSize(model.Size);
        ModifiedLabel = model.Modified.ToString("yyyy/MM/dd HH:mm");
    }

    // UI オートメーション / ナレーターが行名としてファイル名を読めるようにする
    public override string ToString() => Name;

    private static string FormatSize(long bytes)
    {
        // エクスプローラー同様に KB 切り上げ、1MB 以上は単位を上げる
        if (bytes < 1024 * 1024)
        {
            return $"{Math.Max(1, (bytes + 1023) / 1024):N0} KB";
        }
        if (bytes < 1024L * 1024 * 1024)
        {
            return $"{bytes / (1024.0 * 1024):0.0} MB";
        }
        return $"{bytes / (1024.0 * 1024 * 1024):0.0} GB";
    }
}
