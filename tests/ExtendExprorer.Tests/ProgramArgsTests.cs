using ExtendExprorer;
using ExtendExprorer.Models.Session;
using Xunit;

namespace ExtendExprorer.Tests;

/// <summary>コマンドラインの解釈。<c>--panes=</c> の clamp は
/// <b>メモリ実測の再現性</b>がここに乗っているので、黙って壊れると困る。</summary>
public sealed class ProgramArgsTests
{
    [Theory]
    [InlineData(new string[0], 1)]                       // 既定は 1
    [InlineData(new[] { "--panes=4" }, 4)]
    [InlineData(new[] { "--PANES=4" }, 4)]               // 大文字小文字は区別しない
    [InlineData(new[] { "--panes=0" }, 1)]               // 下は 1 で止める
    [InlineData(new[] { "--panes=-5" }, 1)]
    [InlineData(new[] { "--panes=16" }, 16)]
    [InlineData(new[] { "--panes=99" }, 16)]             // 上は 16 で止める
    [InlineData(new[] { "--panes=abc" }, 1)]             // 数にならなければ既定
    [InlineData(new[] { "--panes=" }, 1)]
    [InlineData(new[] { "--panes=2", "--panes=5" }, 5)]  // 後のものが勝つ
    [InlineData(new[] { "--diag", "--panes=3" }, 3)]
    public void PaneCount(string[] args, int expected) =>
        Assert.Equal(expected, Program.PaneCount(args));

    [Fact]
    public void StartPaths_実在するフォルダだけを返す()
    {
        var dir = Directory.CreateTempSubdirectory("ee-test-");
        try
        {
            var args = new[]
            {
                "--diag",                      // ★ 「--」で始まるものは外す
                dir.FullName,
                Path.Combine(dir.FullName, "nope"),  // 実在しない
                "",                            // 空
            };
            Assert.Equal([dir.FullName], Program.StartPaths(args).ToArray());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void StartPaths_引用符と前後の空白を落とす()
    {
        var dir = Directory.CreateTempSubdirectory("ee-test-");
        try
        {
            Assert.Equal([dir.FullName],
                Program.StartPaths([$"  \"{dir.FullName}\"  "]).ToArray());
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    // --- session から「最初のタブのパス」を拾う ---

    [Fact]
    public void FirstTabPath_null()
        => Assert.Null(Program.FirstTabPath(null));

    [Fact]
    public void FirstTabPath_木の左を先に見る()
    {
        var dir = Directory.CreateTempSubdirectory("ee-test-");
        try
        {
            var tree = new LayoutSnapshot
            {
                Kind = "split",
                First = new LayoutSnapshot { Kind = "pane", Tabs = [new TabSnapshot { Path = dir.FullName }] },
                Second = new LayoutSnapshot { Kind = "pane", Tabs = [new TabSnapshot { Path = @"Z:\nope" }] },
            };
            Assert.Equal(dir.FullName, Program.FirstTabPath(tree));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void FirstTabPath_左が実在しなければ右へ降りる()
    {
        var dir = Directory.CreateTempSubdirectory("ee-test-");
        try
        {
            var tree = new LayoutSnapshot
            {
                Kind = "split",
                First = new LayoutSnapshot { Kind = "pane", Tabs = [new TabSnapshot { Path = @"Z:\nope" }] },
                Second = new LayoutSnapshot { Kind = "pane", Tabs = [new TabSnapshot { Path = dir.FullName }] },
            };
            Assert.Equal(dir.FullName, Program.FirstTabPath(tree));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    /// <summary>★ <c>ActiveTabIndex</c> が範囲外でも落ちない（clamp される）。</summary>
    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(99)]
    public void FirstTabPath_手前タブの番号が範囲外でも落ちない(int activeIndex)
    {
        var dir = Directory.CreateTempSubdirectory("ee-test-");
        try
        {
            var pane = new LayoutSnapshot
            {
                Kind = "pane",
                ActiveTabIndex = activeIndex,
                Tabs = [new TabSnapshot { Path = dir.FullName }],
            };
            Assert.Equal(dir.FullName, Program.FirstTabPath(pane));
        }
        finally
        {
            dir.Delete(recursive: true);
        }
    }

    [Fact]
    public void FirstTabPath_タブが空ならnull() =>
        Assert.Null(Program.FirstTabPath(new LayoutSnapshot { Kind = "pane", Tabs = [] }));
}
