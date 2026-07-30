using ExtendExprorer.Interop;

namespace ExtendExprorer.Services;

/// <summary>エクスプローラーと同じ自然順（数字を数値として扱う）で名前を比較する。
/// `.NET` の文字列比較では `File10` が `File2` より前に来てしまい、エクスプローラーと
/// 並び順が変わるため、シェルと同じ <c>StrCmpLogicalW</c> に委ねる。</summary>
internal sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    private NaturalStringComparer()
    {
    }

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y))
        {
            return 0;
        }
        return NativeMethods.StrCmpLogicalW(x ?? "", y ?? "");
    }
}
