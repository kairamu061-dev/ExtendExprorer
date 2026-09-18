using System.Text.Json.Serialization;

namespace ExtendExprorer.Models.Session;

/// <summary>session.json のルート。Version はスキーマ版数（不一致は既定状態で起動）。</summary>
public sealed class SessionFile
{
    public int Version { get; set; } = 1;
    public WindowBounds? Bounds { get; set; }
    public LayoutSnapshot? Layout { get; set; }

    /// <summary>フォルダツリーの幅（px）。0 以下・未設定なら既定幅で開く。</summary>
    public double TreeWidth { get; set; }

    /// <summary>フォルダツリーを折りたたんだ状態で終了したか。</summary>
    public bool TreeCollapsed { get; set; }

    /// <summary>フォルダごとの並べ替えの設定。<b>既定と違うものだけ</b>入る
    /// （全部入れると際限なく太る）。旧 WinUI 3 版はこの項目を知らないが、
    /// 知らない項目は読み飛ばされるだけなので互換は壊れない。</summary>
    public List<FolderSortSnapshot>? FolderSort { get; set; }
}

/// <summary>1 フォルダぶんの並べ替えの設定。</summary>
public sealed class FolderSortSnapshot
{
    public string Path { get; set; } = "";

    /// <summary>フォルダを先頭にまとめるか。</summary>
    public bool FoldersFirst { get; set; } = true;
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
