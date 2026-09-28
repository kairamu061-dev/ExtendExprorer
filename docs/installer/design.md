# installer 設計

## 分割の評価（`agent-rules.md` の分割ルール）

**分割しない。**例外条件の両方を満たしている。

- **片方だけでは確かめられない**——インストーラーのスクリプトは
  **CI が組み立ててくれないと存在しない**し、CI の手順は
  **スクリプトが無ければ何も出さない**。バージョン番号も、
  exe と インストーラーの両方に入って初めて意味がある
- **コードが極めて小さい**——`.iss` **1 ファイル**、CI の手順 **1 つ**、`csproj` **1 行**

## 技術選定

| 技術 | 用途 | 選定理由 |
|------|------|----------|
| **Inno Setup 6** | インストーラー | **ユーザーごとインストールが 1 行**（`PrivilegesRequired=lowest`）。起動中の検出・上書き・アンインストーラー登録が定型で済む。単一の `.exe` で配れる |
| `FindWindowByClassName`（Inno の組み込み関数） | 起動中の検出 | **窓のクラス名が分かっている**（`ExtendExprorer.MainWindow`）ので、プロセス名やミューテックスを増やさずに済む。**アプリ側の変更が 0** |
| `csproj` の `<Version>` | 版数の単一の出どころ | exe のファイルバージョンになり、CI がそれを読んでインストーラーへ渡す。**人が 2 か所に書かない** |
| GitHub Actions（`windows-latest`） | ビルド | 既にここでビルドしている。Inno Setup は `choco install innosetup` で入る |

### 選ばなかったもの

| | 理由 |
|---|---|
| **WiX / MSI** | 無人インストール（`msiexec /quiet`）と GPO 配布が利点だが、**どちらも予定が無い**。ユーザーごとインストールの記述が煩雑になる |
| **MSIX** | **署名が必須**。自己署名だと利用者に証明書を入れてもらうことになる |
| **`AppMutex`** | Inno の定石だが、**アプリ側に名前付きミューテックスを足す**必要がある。窓のクラス名で足りる |
| **`CloseApplications=yes`**（Restart Manager に閉じさせる） | 見た目は親切（勝手に閉じて入れ直す）だが、**閉じる経路が `WM_ENDSESSION`** になる。そこは **session E-13（ログオフ）として未確認のまま残っている道**で、**入れ替えのついでに配置を失う**のが最悪。**`no` にして、閉じるのは人にやってもらう** |

## アーキテクチャ

```
csproj <Version>0.1.0</Version>
   |
   |  dotnet publish
   v
publish-win32/ExtendExprorer.exe   3.55MB（ファイルバージョン 0.1.0.0）
   |                                ExtendExprorer.pdb  16.3MB ← 配らない
   |                                Assets/app.ico      91KB   ← 配らない（実行時に読まれない）
   |
   |  CI: csproj から版数を読み、exe のファイルバージョンと**突き合わせて**から渡す
   v
ISCC /DAppVersion=0.1.0 installer/ExtendExprorer.iss
   |
   v
ExtendExprorer-setup-0.1.0.exe      成果物 ExtendExprorer-setup
```

**成果物は 2 つになる。**`ExtendExprorer-win32-x64` は**名前を変えない**
（確認をお願いしている側の道具と、`tmp/確認/old/` の依頼書 20 回ぶんが指している）。
中のファイルの並びも同じ。

> **ただし exe 自体は変わる。**`<Version>` を入れたので版数の資源が付き、
> **`98a34dc` の 3,720,704 バイトから `4afa760` の 3,720,192 バイトへ 512 バイト減った**
> （資源が詰め物と入れ替わった分）。**ハッシュは一致しない。**
> 「インストーラーを足しただけ」ではない——**本体も 1 度ビルドし直っている**ので、
> 退行の確認（test-cases E-30 / E-33）は形だけの項目ではない。

### 配るものは exe 1 本だけ

| ファイル | 配る？ | 理由 |
|---|---|---|
| `ExtendExprorer.exe`（3,720,192 バイト＝3.55MB） | **配る** | これだけで動く（自己完結の Native AOT） |
| `ExtendExprorer.pdb`（17,076,224 バイト＝16.3MB） | 配らない | 利用者には不要。**入れると 5 倍以上に膨らむ** |
| `Assets/app.ico`（91KB） | 配らない | **実行時に誰も読んでいない。**アイコンは exe に埋まっている PE リソースから `LoadImageW(instance, IDI_APPLICATION, …)` で取る（`MainWindow.LoadAppIcon`） |

## データ構造

### インストール後に置かれるもの

```
%LOCALAPPDATA%\Programs\ExtendExprorer\
    ExtendExprorer.exe
    unins000.exe            ← Inno Setup が作るアンインストーラー
    unins000.dat

%APPDATA%\Microsoft\Windows\Start Menu\Programs\
    ExtendExprorer\                 ← ★ フォルダ。中に .lnk が入る
        ExtendExprorer.lnk          （DefaultGroupName={#AppName} なので「群」が作られる）

HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\
    {A11863B4-E23A-41E9-90B2-5B8DC4E7AE45}_is1\
        DisplayName / DisplayVersion / UninstallString / ...
```

### アプリが書くもの（インストーラーは触らない）

```
%LOCALAPPDATA%\ExtendExprorer\
    session.json        タブとペインの配置
    session.json.bak    壊れていたときの退避
    error.log           例外
    diag.log            --diag のときだけ
```

**インストール先と設定の置き場所は別。**
アンインストールで消すのは前者だけで、**後者は聞いてから消す**（既定は残す）。

## インターフェース

### `installer/ExtendExprorer.iss`（要点）

```ini
#define AppName    "ExtendExprorer"
; 版数は CI から /DAppVersion=... で渡す。ここに書かない（2 か所に書かないため）
#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif

[Setup]
; ★ この GUID は変えないこと。変えると「別のアプリ」になり、
;   古い版がアンインストールできないまま残る
AppId={{A11863B4-E23A-41E9-90B2-5B8DC4E7AE45}
AppName={#AppName}
AppVersion={#AppVersion}
VersionInfoVersion={#AppVersion}

; ユーザーごと。UAC を出さない。{autopf} は非管理者のとき {localappdata}\Programs になる
; ★ PrivilegesRequiredOverridesAllowed は付けない（2026-09-28 に外した）。
;   付けると「現在のユーザー用 / すべてのユーザー用」を尋ねる画面が出て、
;   **作らないと決めた枝**（PC 全体）を選べてしまう
PrivilegesRequired=lowest
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}

ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; 起動中の検出は自分でやる。Restart Manager に閉じさせない（design の「選ばなかったもの」）
CloseApplications=no

OutputBaseFilename=ExtendExprorer-setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\ExtendExprorer.exe

[Languages]
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
; 既定では作らない（チェックを外した状態で出す）
Name: "desktopicon"; Description: "デスクトップにショートカットを作る"; Flags: unchecked

[Files]
; ★ exe 1 本だけ。pdb と Assets は入れない
Source: "..\publish-win32\ExtendExprorer.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";        Filename: "{app}\ExtendExprorer.exe"
Name: "{autodesktop}\{#AppName}";  Filename: "{app}\ExtendExprorer.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\ExtendExprorer.exe"; Description: "ExtendExprorer を実行する"; \
  Flags: nowait postinstall skipifsilent

[Code]
// 起動中なら何もせずに終わる。窓のクラス名で見る（プロセス名より確か）
function AppIsRunning(): Boolean;
begin
  Result := FindWindowByClassName('ExtendExprorer.MainWindow') <> 0;
end;
```

- **`InitializeSetup` と `InitializeUninstall` の両方**で `AppIsRunning` を見る。
  片方だけだと、**起動したままアンインストールして残骸が出る**
- **アンインストール時に設定を消すか聞く**のは `CurUninstallStepChanged(usPostUninstall)`

### CI に足す手順（`build.yml` の `win32` ジョブ）

```yaml
- name: Read version
  id: ver
  shell: pwsh
  run: |
    $csproj = 'src/ExtendExprorer.Win32/ExtendExprorer.Win32.csproj'
    $v = (Select-Xml -Path $csproj -XPath '/Project/PropertyGroup/Version').Node.InnerText.Trim()
    # ★ 2 つ並べる。csproj に書いた版数と、実際に焼かれたファイルバージョンを突き合わせる。
    #   ここがずれたまま配ると、「アプリと機能」の表示と中身が食い違う
    $fv = (Get-Item publish-win32/ExtendExprorer.exe).VersionInfo.FileVersion
    Write-Host "csproj=$v  exe=$fv"
    if ($fv -ne "$v.0" -and $fv -ne $v) { throw "版数が合いません: csproj=$v exe=$fv" }
    "version=$v" >> $env:GITHUB_OUTPUT

- name: Build installer
  shell: pwsh
  run: |
    choco install innosetup -y --no-progress
    & "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe" `
        /DAppVersion=${{ steps.ver.outputs.version }} `
        /O"installer-out" installer/ExtendExprorer.iss

- uses: actions/upload-artifact@v4
  with:
    name: ExtendExprorer-setup      # ★ 2 つ目の成果物。既存の名前は変えない
    path: installer-out
    retention-days: 30
```

## 依存関係

| ライブラリ / サービス | 用途 |
|-----------------------|------|
| **Inno Setup 6**（`choco install innosetup`） | `ISCC.exe` でインストーラーを組む。CI の実行時に入れる（リポジトリには入れない） |
| `compiler:Languages\Japanese.isl` | 日本語のウィザード。Inno Setup 6 に同梱 |
| GitHub Actions `windows-latest` | ビルド環境。Chocolatey が入っている |

## 注意（触るときに読むこと）

- **`AppId` の GUID は永久に変えない。**変えると Windows から見て「別のアプリ」になり、
  **古い版がアンインストールできないまま残る**
- **`app.manifest` の `assemblyIdentity version="1.0.0.0"` は版数ではない。**
  これは side-by-side の識別子で、利用者には見えない。
  **`<Version>` と揃える必要は無い**（揃えようとして触ると、comctl32 v6 の宣言を壊しうる）
- **`tmp/bin/win32-<sha>/` から古い版が動いていても、インストールは止まる。**
  窓のクラス名で見ているので、置き場所は関係ない。
  **これは意図**——古い版が動いたままだと `session.json` を取り合う（2026-09-23 の報告）
