namespace ExtendExprorer.Models;

/// <summary>「PC」を開いたときの 1 行。<b><see cref="Entry"/> には足さない。</b>
///
/// <para>容量の数字が要るのは「PC」を開いているときだけで、<c>Entry</c> は
/// ツリー・一覧・差分更新・並べ替えが共有している。使うのが 1 か所しかない項目を
/// 足すと、**残り全部が「いつも null」の枝**を抱えることになる。
/// 行と同じ並びで別に持ち、「PC」のときだけ読む。</para></summary>
public sealed record DriveRow(string Root, string Label, string TypeName, long Total, long Free);
