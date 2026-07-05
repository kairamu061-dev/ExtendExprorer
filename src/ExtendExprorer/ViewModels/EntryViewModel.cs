using ExtendExprorer.Models;

namespace ExtendExprorer.ViewModels;

public sealed class EntryViewModel
{
    public Entry Model { get; }

    public string Name => Model.Name;
    public bool IsDirectory => Model.IsDirectory;
    public string TypeLabel { get; }
    public string SizeLabel { get; }
    public string ModifiedLabel { get; }
    // Segoe Fluent Icons: フォルダ / ドキュメント
    public string Glyph => IsDirectory ? "\uE8B7" : "\uE7C3";

    public EntryViewModel(Entry model)
    {
        Model = model;
        TypeLabel = model.IsDirectory
            ? "フォルダ"
            : System.IO.Path.GetExtension(model.Name) is { Length: > 1 } ext
                ? ext.TrimStart('.').ToUpperInvariant()
                : "ファイル";
        SizeLabel = model.IsDirectory ? "—" : FormatSize(model.Size);
        ModifiedLabel = model.Modified.ToString("yyyy/MM/dd HH:mm");
    }

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
