using System.Globalization;
using ExtendExprorer.Models;
using Xunit;

namespace ExtendExprorer.Tests;

/// <summary>サイズと容量の書式。<b>1024 の境目は off-by-one の巣</b>なので、
/// 境目とその ±1 を並べて置く。
///
/// <para><c>:N0</c> は桁区切りが文化圏依存なので、<b>不変文化で固定</b>する。
/// 実機は <c>ja-JP</c> だが、そこもカンマなので結論は変わらない。</para></summary>
public sealed class EntryFormatTests : IDisposable
{
    private readonly CultureInfo _original = CultureInfo.CurrentCulture;

    public EntryFormatTests() => CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;

    public void Dispose() => CultureInfo.CurrentCulture = _original;

    private static Entry File(long size) => new("a.txt", false, size, default, false);

    [Theory]
    // KB は切り上げ。0 バイトでも「1 KB」（エクスプローラーと同じ）
    [InlineData(0, "1 KB")]
    [InlineData(1, "1 KB")]
    [InlineData(1024, "1 KB")]
    [InlineData(1025, "2 KB")]
    // 1MB の境目
    [InlineData(1024 * 1024 - 1, "1,024 KB")]
    [InlineData(1024 * 1024, "1.0 MB")]
    // 1GB の境目
    [InlineData(1024L * 1024 * 1024 - 1, "1024.0 MB")]
    [InlineData(1024L * 1024 * 1024, "1.0 GB")]
    public void SizeLabel_境目(long bytes, string expected) =>
        Assert.Equal(expected, EntryFormat.SizeLabel(File(bytes)));

    [Fact]
    public void SizeLabel_フォルダはダッシュ() =>
        Assert.Equal("—", EntryFormat.SizeLabel(new Entry("d", true, 0, default, false)));

    [Theory]
    // ドライブは MB / GB / TB。ファイルとは丸め方が違う（KB にしない）
    [InlineData(0, "1 MB")]
    [InlineData(1024L * 1024, "1 MB")]
    [InlineData(1024L * 1024 * 1024 - 1, "1,023 MB")]
    [InlineData(1024L * 1024 * 1024, "1 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024 - 1, "1,024 GB")]
    [InlineData(1024L * 1024 * 1024 * 1024, "1.00 TB")]
    public void CapacityLabel_境目(long bytes, string expected) =>
        Assert.Equal(expected, EntryFormat.CapacityLabel(bytes));

    [Theory]
    [InlineData("a.txt", false, "TXT")]
    [InlineData("a.TXT", false, "TXT")]          // 大文字に揃える
    [InlineData("a.tar.gz", false, "GZ")]        // 最後の拡張子だけ
    [InlineData("noext", false, "ファイル")]
    [InlineData("a.", false, "ファイル")]         // 「.」だけは拡張子と見ない
    [InlineData("anything", true, "フォルダ")]
    public void TypeLabel(string name, bool isDirectory, string expected) =>
        Assert.Equal(expected, EntryFormat.TypeLabel(new Entry(name, isDirectory, 0, default, false)));
}
