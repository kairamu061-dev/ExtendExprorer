# shell-icons 設計

## 技術選定

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| `SHGetFileInfoW`（SHGFI_ICON \| SHGFI_SMALLICON） | シェルアイコン（HICON）の取得 | エクスプローラーと同一のアイコン解決。拡張子キャッシュには `SHGFI_USEFILEATTRIBUTES` を併用しディスクアクセスを回避 |
| GDI（`GetIconInfo`/`GetDIBits`） | HICON → BGRA ピクセル変換 | 追加依存なしで WriteableBitmap に流し込める形式へ変換 |
| `WriteableBitmap` | WinUI の ImageSource 化 | `{x:Bind}` で Image.Source に直結。同一インスタンスを複数行で共有可能 |
| `LibraryImport` | 上記の P/Invoke | AOT 互換（context-menu と同方針） |

## アーキテクチャ

- `Services/ShellIconCache`（static）: キー→`Task<ImageSource?>` のキャッシュ。取得・変換は `Task.Run`、
  `WriteableBitmap` 生成のみ呼び出し元（UI）スレッド
- `EntryViewModel`: `Icon` プロパティ（初回アクセスで読込開始→完了時に `PropertyChanged`）と
  `FallbackIconVisibility`（未解決時のみ従来グリフを表示）
- `FileListView` テンプレート: `FontIcon`（フォールバック）と `Image`（シェルアイコン）を重ねて表示

```
FileListView(行の実体化, UIスレッド)
  → EntryViewModel.Icon getter（初回のみ）
    → ShellIconCache.GetAsync(fullPath, isDir)
        ├─ cache hit: 共有 Task を返す
        └─ miss: Task.Run[SHGetFileInfoW → GetIconInfo/GetDIBits → BGRA]
                 → (UI) WriteableBitmap 生成 → キャッシュ
    → 完了時 PropertyChanged(Icon) → {x:Bind Mode=OneWay} が差し替え
```

## データ構造

```csharp
// キャッシュキー
// フォルダ:                "<dir>"
// .exe/.ico/.lnk:          フルパス小文字（個別アイコン）
// その他ファイル:           拡張子小文字（例 ".txt"）、拡張子なしは "<none>"
Dictionary<string, Task<ImageSource?>>   // UI スレッドからのみ触る（ロック不要）
```

## インターフェース

```csharp
// Services/ShellIconCache
static Task<ImageSource?> GetAsync(string fullPath, bool isDirectory); // 失敗時 null

// EntryViewModel へ追加
string FullPath { get; }                    // ctor で folderPath から算出
ImageSource? Icon { get; }                  // 初回 get で非同期読込を開始
Visibility FallbackIconVisibility { get; }  // Icon 未解決時 Visible
```

- 追加 P/Invoke（`Interop/ShellInterop.cs`）: `SHGetFileInfoW` / `DestroyIcon` / `GetIconInfo` /
  `GetObjectW` / `GetDIBits` / `GetDC` / `ReleaseDC` / `DeleteObject`＋`SHFILEINFOW` / `ICONINFO` / `BITMAP` / `BITMAPINFOHEADER`

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| shell32.dll / user32.dll / gdi32.dll（OS 標準） | アイコン取得・ピクセル変換 |
| （NuGet 追加なし） | |

## リスク・検証ポイント

- 32bpp 以外（アルファなし）の旧式アイコンはアルファ全ゼロになるため、全ゼロ検出時は不透明へフォールバック
- `SHGetFileInfoW` は MTA スレッドプールからの呼び出しで動作する想定（問題が出たら UI スレッドへ退避）
- 仮想化 ListView で画面内の行しか Icon getter が走らないこと（スクロール性能）を実機確認

## キャッシュの方針（2026-07-30 更新・BUG-010）

**成功したアイコンだけを保持する。失敗は記録しない。**

| 辞書 | 内容 |
|------|------|
| `Resolved` | 取得できた `ImageSource`（キー: `<dir>` / 拡張子 / `.exe` 等はフルパス / ドライブはフルパス） |
| `InFlight` | 取得中の `Task`。完了時（成否によらず）必ず取り除く |

- 汎用フォルダアイコンは `<dir>` キーを**全フォルダで共有**するため、失敗した `Task` を残すと
  一度の失敗で以後すべてのフォルダがフォールバックのグリフ表示に落ち、再起動まで戻らない（BUG-010）
- リトライの抑止は VM 単位（`EntryViewModel._iconRequested`）で足りる。プロセス共有のキャッシュに
  失敗を残す必要はない
- 取得はワーカースレッドで行い、失敗したら**待ち時間を延ばしながら 4 回**試す（0 / 120 / 350 / 800ms）。
  起動直後は数百 ms のあいだ `SHGetFileInfoW` が空を返しつづけるため、即時リトライでは届かない（BUG-015）
- フォルダ行はキー `<dir>` を共有するので、**1 回の失敗がその画面の全行に広がる**。
  共有 Task の中で再試行することで、成功した時点で待っていた全行に一斉に反映される
- `ShellIconCache.WarmUp()` を `MainWindow` 生成時に呼び、**最初のフォルダを読み込むより前**に
  汎用フォルダアイコンの取得を始めておく（同じく BUG-015）
- 表示側は 3 箇所（一覧・ツリー・タブ）すべて **16×16 の `Grid` に `FontIcon`（フォールバック）と
  `Image` を重ねる**形に統一する。`IconSource` 系は固有サイズを持たず、寸法を与えない親の下では
  レイアウトされない（BUG-011）
