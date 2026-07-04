# explorer 設計

本機能エリアはサブ項目に分割されている。個別の設計は各サブ項目の design.md を参照。

## サブ項目

- [file-list](./file-list/design.md) — ファイル一覧表示とフォルダナビゲーション
- [tabs](./tabs/design.md) — タブ管理（追加・複製・切替・クローズ）
- [pane-split](./pane-split/design.md) — 画面分割とスプリッター
- [address-bar](./address-bar/design.md) — フォルダパス表示・入力
- [session](./session/design.md) — セッション保存・復元

## 横断的関心事

### 技術選定

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| WinUI 3 + C# / .NET 8 | UI・アプリ実装 | 軽量・省メモリ方針（プロジェクト概要参照） |
| CommunityToolkit.Mvvm | MVVM 基盤 | ObservableProperty / RelayCommand の定型削減 |
| CommunityToolkit.WinUI.Controls.Sizers | GridSplitter | ペインサイズ調整 |

### 共有データ構造（全サブ項目が参照する状態モデル）

ViewModel 層が唯一の情報源。View は XAML バインディングで追従する。

```csharp
// レイアウトは二分木。leaf がペイン、node が分割
public abstract partial class LayoutNodeViewModel : ObservableObject
{
    public string Id { get; }  // Guid.NewGuid().ToString()
}

public partial class SplitNodeViewModel : LayoutNodeViewModel
{
    public Orientation Direction { get; set; }   // Horizontal / Vertical
    [ObservableProperty] private double ratio;   // 先頭側の比率 0.1–0.9
    public LayoutNodeViewModel First { get; set; }
    public LayoutNodeViewModel Second { get; set; }
}

public partial class PaneViewModel : LayoutNodeViewModel
{
    public ObservableCollection<TabViewModel> Tabs { get; }
    [ObservableProperty] private TabViewModel? activeTab;
}

public partial class TabViewModel : ObservableObject
{
    [ObservableProperty] private string path;
    public List<string> History { get; }         // 戻る/進む用
    public int HistoryIndex { get; set; }
    public ObservableCollection<EntryViewModel> Entries { get; }
}

public partial class MainViewModel : ObservableObject
{
    [ObservableProperty] private LayoutNodeViewModel layout;
    [ObservableProperty] private PaneViewModel activePane;
}
```

### サービス層インターフェース

| サービス | 内容 |
|---------|------|
| `IFileSystemService.ListAsync(path)` | フォルダ内容の列挙。`ListResult`（成功: エントリ列 / 失敗: エラー種別）を返す |
| `IFileSystemService.HomePath` | ホームディレクトリ（`Environment.SpecialFolder.UserProfile`） |
| `ISessionService.LoadAsync()` / `SaveAsync(snapshot)` | セッション JSON の読み書き |

- ViewModel はサービスをコンストラクタ注入で受け取る（テスト時はフェイクに差し替え）

### 状態変更の通知

- 状態変更は ViewModel のプロパティ変更（INotifyPropertyChanged）と ObservableCollection の変更で伝搬
- セッション自動保存は MainViewModel が配下の変更を集約し、デバウンス付きで ISessionService に流す

### ディレクトリ構成（実装）

```
src/ExtendExprorer/
├── App.xaml / MainWindow.xaml
├── Views/         # PaneView, FileListView, AddressBarView, LayoutHost
├── ViewModels/    # MainViewModel, PaneViewModel, TabViewModel, ...
├── Models/        # SessionFile, Entry, ListResult
└── Services/      # FileSystemService, SessionService
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| Microsoft.WindowsAppSDK | WinUI 3 本体 |
| CommunityToolkit.Mvvm | MVVM |
| CommunityToolkit.WinUI.Controls.Sizers | GridSplitter |
