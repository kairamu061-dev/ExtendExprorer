using System.Text.Json.Serialization;

namespace ExtendExprorer.Models.Session;

/// <summary>session.json のルート。Version はスキーマ版数（不一致は既定状態で起動）。</summary>
public sealed class SessionFile
{
    public int Version { get; set; } = 1;
    public WindowBounds? Bounds { get; set; }
    public LayoutSnapshot? Layout { get; set; }
}

public sealed class WindowBounds
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

/// <summary>レイアウト木のスナップショット。多相シリアライズは AOT で扱いが難しいため、
/// 継承ではなく Kind で pane/split を判別するタグ付き単一型にする。</summary>
public sealed class LayoutSnapshot
{
    public string Kind { get; set; } = "pane"; // "pane" | "split"

    // Kind == "split"
    public string? Direction { get; set; }     // "Horizontal" | "Vertical"
    public double Ratio { get; set; } = 0.5;
    public LayoutSnapshot? First { get; set; }
    public LayoutSnapshot? Second { get; set; }

    // Kind == "pane"
    public List<TabSnapshot>? Tabs { get; set; }
    public int ActiveTabIndex { get; set; }
    public bool IsActivePane { get; set; }
}

public sealed class TabSnapshot
{
    public string Path { get; set; } = ""; // 履歴は保存しない（spec）
}

// Native AOT では組み込みのリフレクション JSON が使えないため、ソース生成コンテキストを使う
[JsonSourceGenerationOptions(
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(SessionFile))]
internal sealed partial class SessionJsonContext : JsonSerializerContext
{
}
