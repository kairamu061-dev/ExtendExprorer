namespace ExtendExprorer.Services;

/// <summary>この実行ファイルの版数。
///
/// <para><b>出どころは exe に焼かれた版数の資源</b>（<c>csproj</c> の <c>&lt;Version&gt;</c> →
/// ビルド時に PE へ入る）。C# の定数に書き写さないのは、
/// <b>版数を 2 か所に書かない</b>ため——人が片方だけ直すと、
/// 画面と「アプリと機能」の表示が食い違う。</para>
///
/// <para><b>エクスプローラーのプロパティが見せるのと同じ値</b>になる。
/// CI も「<c>csproj</c> の値」と「exe に焼かれた値」を突き合わせてから配っているので、
/// 画面・ファイルのプロパティ・インストーラーの 3 つが同じ数字になる。</para>
///
/// <para><b>読めなければ空。</b>推測で何か出すより、出さない方がよい——
/// 間違った版数は、無いより悪い。</para></summary>
internal static class AppVersion
{
    private static string? _short;
    private static string? _full;

    /// <summary>画面に出す短い形（<c>0.1.0</c>）。読めなければ空。</summary>
    internal static string Short => _short ??= Load().Short;

    /// <summary>記録に残す長い形（<c>0.1.0+&lt;コミット&gt; / ファイル 0.1.0.0</c>）。読めなければ空。
    ///
    /// <para><b>コミットまで入る</b>のが要点。「どのビルドですか」を
    /// このプロジェクトでは何度も聞かれている。</para></summary>
    internal static string Full => _full ??= Load().Full;

    private static (string Short, string Full) Load()
    {
        try
        {
            var path = Environment.ProcessPath;
            if (string.IsNullOrEmpty(path))
            {
                return ("", "");
            }
            var info = System.Diagnostics.FileVersionInfo.GetVersionInfo(path);

            // ProductVersion は InformationalVersion——CI では `0.1.0+<40 桁のコミット>` になる。
            // 画面には `+` より前だけ出す（帯は狭い）
            var product = info.ProductVersion ?? "";
            var file = info.FileVersion ?? "";
            var plus = product.IndexOf('+');
            var shortForm = plus >= 0 ? product[..plus] : product;
            if (shortForm.Length == 0)
            {
                shortForm = file;
            }

            var full = product.Length > 0 ? product : file;
            if (file.Length > 0 && product.Length > 0 && !string.Equals(file, product, StringComparison.Ordinal))
            {
                full = $"{product} / ファイル {file}";
            }
            return (shortForm, full);
        }
        catch (Exception ex)
        {
            // 版数が出ないことより、起動が止まる方が困る
            UI.Diagnostics.Report("AppVersion.Load", ex);
            return ("", "");
        }
    }
}
