# tabs 設計

## 技術選定

**2026-08-09 変更: WinUI 3 の `TabView` をやめ、独自のタブ列（`TabStrip`）に置き換えた。**

`TabView` は 1 行固定で、入りきらないタブは左右スクロールになる。**2 列目への折り返しは
テンプレート差し替えでは実現できない**（内部の `ScrollViewer` と横方向前提のレイアウトに依存しているため）。
折り返しは本アプリの使い勝手の要というユーザー判断（2026-08-09）により、タブ帯を自作する。

- **作り直すのは「タブ帯」だけ**。`TabViewModel` / `PaneViewModel.Tabs` / `MainViewModel` の
  タブ規則（追加・複製・閉じる・最終タブ・session 復元）は一切変更しない
- 段階的に入れる（第 1 段のみ本ドキュメントの範囲）

| 段 | 内容 | 状態 |
|---|------|------|
| 1 | `TabView` → `TabStrip` 差し替え。折り返し・幅を名前に合わせる・× 廃止・アクティブを上に広げる | 実装済み |
| 2 | 同じ帯の中でのドラッグ並べ替え | 未着手 |
| 3 | 別ペインへのドラッグ移動＋無効な場所なら元に戻る | 未着手 |

## アーキテクチャ

```
PaneView
└ TabStrip                     … タブ帯（UserControl）
  └ TabWrapPanel               … 折り返しレイアウト（Panel 派生）
    ├ TabStripItem × N         … タブ 1 枚（UserControl）
    └ Button「＋」             … 最後のタブの直後に流れる
```

- `TabStrip.ItemsSource` に `PaneViewModel.Tabs` を渡す。`CollectionChanged` を購読し、
  **増減のときだけ**子要素を組み直す（タイトル・アイコンの変化は `TabStripItem` 内の `x:Bind` が拾う）
- 選択は `TabStrip.SelectedItem` ⇔ `PaneViewModel.ActiveTab`。`PaneView` が両方向を突き合わせる
  （`TabView` のときと同じ形なので `PaneView` 側の配線はほぼそのまま）
- **仮想化しない**。1 ペインのタブ数上限は 50（`MainViewModel.MaxTabsPerPane`）で、
  全部実体化しても問題にならない。仮想化を入れると折り返しの測定が複雑になる

> **AOT 注意**: `Panel` / `UserControl` を継承する自作型は **`partial` 必須**（BUG-013）。
> 現在は `CsWinRT1028` をビルドエラーに昇格してあるので、付け忘れはビルドで落ちる。

## 折り返しレイアウト（`TabWrapPanel`）

```
MeasureOverride(available)
  各子を無限幅で測る → 希望幅を TabMinWidth(60) 〜 TabMaxWidth(240) にクランプ
  左から詰め、available.Width を超えたら次の行へ
  → 希望サイズ = (最大行幅, 行数 × RowHeight)

ArrangeOverride(final)
  同じ規則で配置。各行は下端を揃え、
  アクティブなタブだけ ActiveExtraHeight(3px) ぶん上に伸ばす
```

- **行の下端を揃える**のがポイント。アクティブタブは高さが違うので、上端を揃えると下辺がずれる
- 帯の高さは行数で変わる。`PaneView` のタブ行は `Height="Auto"` なので自然に伸びる
- 幅 0 のとき（初期化直後など）は 1 行として扱い、0 除算・無限ループを避ける

| 定数 | 値 | 意味 |
|------|-----|------|
| `RowHeight` | 23 | 1 行の高さ |
| `TabHeight` | 20 | 非アクティブなタブの高さ |
| `ActiveExtraHeight` | 3 | アクティブタブを上に伸ばす量 |
| `TabMinWidth` / `TabMaxWidth` | 60 / 240 | タブ幅のクランプ |

## タブ 1 枚（`TabStripItem`）

- アイコン（14px）＋タイトル。タイトルは `TextTrimming="CharacterEllipsis"`
- 状態ごとの背景（`VisualStateManager` は使わず、ポインタイベントで直接塗る。状態が 3 つだけで
  テンプレート差し替えも不要なため）

| 状態 | 背景 | 備考 |
|------|------|------|
| 通常 | `#F0F0F0` | 帯と同色 |
| ホバー | `#E5F1FB` | 一覧のホバーと同じ |
| アクティブ | `#FFFFFF` | 一覧の背景と地続きに見せる。下線 `#0078D4` |

- **閉じる操作**（× ボタンは置かない）
  - ホイールクリック（`PointerPressed` の `IsMiddleButtonPressed`）
  - 右クリック → `ContextFlyout`「タブを閉じる」「他のタブを閉じる」

## インターフェース

```csharp
// TabStrip
public IList<TabViewModel>? ItemsSource { get; set; }  // ObservableCollection を渡すと増減に追随
public TabViewModel? SelectedItem { get; set; }
public bool IsAddButtonVisible { get; set; }
public event Action<TabViewModel>? SelectionChanged;
public event Action? AddRequested;
public event Action<TabViewModel>? CloseRequested;
public event Action<TabViewModel>? CloseOthersRequested;
public void Detach();                                   // 購読解除（BUG-002 のリーク対策と同じ扱い）

// MainViewModel（既存 + 追加）
public void CloseOtherTabs(PaneViewModel pane, TabViewModel keep);
```

- 最終タブ・最終ペインの規則は従来どおり `MainViewModel` に委譲する
- 「他のタブを閉じる」は `CloseTab` を繰り返すだけ（最終タブの規則もそのまま効く）

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| なし（WinUI 3 標準の `Panel` / `UserControl` のみ） | タブ帯 |

## 見た目

サンプルツール準拠のコンパクトさは維持する（2026-08-04 に詰めた寸法を踏襲）。

| 対象 | 値 |
|------|-----|
| 帯の背景 | `#F0F0F0` |
| 1 行の高さ | 23px（タブ 20px ＋ アクティブの伸び 3px） |
| 見出しの文字 / アイコン | 12px / 14px |
| タブ幅 | 名前に合わせて 60〜240px |
| 内側の余白 | `6,0,6,0`・アイコンと文字の間 4px |
