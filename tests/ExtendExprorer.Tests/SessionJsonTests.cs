using System.Text.Json;
using ExtendExprorer.Models.Session;
using Xunit;

namespace ExtendExprorer.Tests;

/// <summary>session.json の往復。<b>AOT でいちばん静かに壊れる場所</b>——
/// ソース生成コンテキストに型を足し忘れても、リフレクションが無効なので
/// 「null が返るだけ」で済んでしまい、画面上は「設定が消えた」にしか見えない。</summary>
public sealed class SessionJsonTests
{
    private static string Write(SessionFile file) =>
        JsonSerializer.Serialize(file, SessionJsonContext.Default.SessionFile);

    private static SessionFile? Read(string json) =>
        JsonSerializer.Deserialize(json, SessionJsonContext.Default.SessionFile);

    [Fact]
    public void 往復して同じ形になる()
    {
        var original = new SessionFile
        {
            Version = 1,
            Bounds = new WindowBounds { X = 900, Y = 80, Width = 1000, Height = 820 },
            TreeWidth = 240.5,
            TreeCollapsed = true,
            FolderSort = [new FolderSortSnapshot { Path = @"C:\Users\x\Downloads", FoldersFirst = false }],
            Layout = new LayoutSnapshot
            {
                Kind = "split",
                Direction = "Vertical",
                Ratio = 0.42,
                First = new LayoutSnapshot
                {
                    Kind = "pane",
                    ActiveTabIndex = 1,
                    IsActivePane = true,
                    Tabs = [new TabSnapshot { Path = @"C:\A" }, new TabSnapshot { Path = @"C:\B" }],
                },
                Second = new LayoutSnapshot
                {
                    Kind = "pane",
                    Tabs = [new TabSnapshot { Path = "::drives" }],
                },
            },
        };

        var round = Read(Write(original));

        Assert.NotNull(round);
        Assert.Equal(1, round!.Version);
        Assert.Equal(900, round.Bounds!.X);
        Assert.Equal(820, round.Bounds.Height);
        Assert.Equal(240.5, round.TreeWidth);
        Assert.True(round.TreeCollapsed);

        Assert.Equal("split", round.Layout!.Kind);
        Assert.Equal("Vertical", round.Layout.Direction);
        Assert.Equal(0.42, round.Layout.Ratio);

        var left = round.Layout.First!;
        Assert.Equal(2, left.Tabs!.Count);
        Assert.Equal(@"C:\B", left.Tabs[1].Path);
        Assert.Equal(1, left.ActiveTabIndex);
        Assert.True(left.IsActivePane);

        // ★ 「PC」の合言葉が素通しで残ること（パスとして加工されない）
        Assert.Equal("::drives", round.Layout.Second!.Tabs![0].Path);

        Assert.Single(round.FolderSort!);
        Assert.False(round.FolderSort![0].FoldersFirst);
    }

    /// <summary>知らない項目は読み飛ばす（旧版が書いた／新版が書いた JSON と行き違っても壊れない）。</summary>
    [Fact]
    public void 知らない項目があっても読める()
    {
        var json = """
        {
          "Version": 1,
          "SomethingNobodyKnows": { "a": [1, 2, 3] },
          "Layout": { "Kind": "pane", "Tabs": [ { "Path": "C:\\A" } ] }
        }
        """;
        var file = Read(json);
        Assert.NotNull(file);
        Assert.Equal(@"C:\A", file!.Layout!.Tabs![0].Path);
    }

    [Fact]
    public void 壊れたJSONは例外になる_呼び出し側が受ける() =>
        Assert.ThrowsAny<JsonException>(() => Read("{ this is not json"));

    /// <summary>null を書かない設定なので、既定のままの項目は JSON に出ない。</summary>
    [Fact]
    public void nullは書かない()
    {
        var json = Write(new SessionFile { Version = 1 });
        Assert.DoesNotContain("\"Bounds\"", json);
        Assert.DoesNotContain("\"Layout\"", json);
    }
}
