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
