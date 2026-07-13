# shell-icons 開発メモ

## 実装上の判断

| 判断内容 | 理由 |
|----------|------|
| `SHGetFileInfoW`＋GDI 変換（`IShellItemImageFactory` ではなく） | 小アイコン用途には十分で、P/Invoke 宣言が少なく AOT リスクが小さい。サムネイル対応が必要になったら ImageFactory を再検討 |
| 拡張子キャッシュは `SHGFI_USEFILEATTRIBUTES` でダミー名（"dummy.txt" 等）から取得 | 実ファイルに触れないため消えたファイルでも安全、かつ高速 |
| `Icon` は getter 初回アクセスで読込開始（プロパティに副作用） | 仮想化 ListView で「画面に見えた行だけ」読み込む一番単純な方法。`{x:Bind}` の初回評価がトリガーになる |
| フォールバックは FontIcon と Image の重ね表示 | 解決前・失敗時に空白を出さない。解決後は Visibility で FontIcon を消す |
| キャッシュは UI スレッド専用の Dictionary（ロックなし） | Icon getter は UI スレッドからしか呼ばれない。Task を値にすることで同キーの二重取得も防げる |
| モノクロアイコン（hbmColor なし）は非対応でグリフのまま | 現代の Windows では実質発生しない。GDI マスク合成のコードを持たない |

## 発生した問題と対処

| 問題 | 対処 |
|------|------|
| CS4004: クラス全体を `unsafe` にすると async メソッドの `await` がコンパイル不可 | `unsafe` はポインタを使う `ExtractIconPixels` / `IconToBgra` の 2 メソッドだけに付与（`e63cef4`）。教訓: unsafe と async は同一スコープに置かない |

## 設計からの変更点

| 変更内容 | 理由 |
|----------|------|
| なし（design.md どおり） | |

## 今後の課題

- folder-tree ノードのシェルアイコン化（ツリーにも同じキャッシュを使えば安価）
- オーバーレイ（ショートカット矢印）・サムネイル
- DPI スケール（現状 16px 固定。高 DPI では `SHGFI_LARGEICON`＋縮小の方が綺麗な可能性）

## ユーザへの要望

- なし
