; ExtendExprorer のインストーラー（Inno Setup 6）
;
; 組み立てるのは CI だけ（.github/workflows/build.yml）。手元に Windows が無いので、
; 構文の誤りは CI でしか分からない。設計は docs/installer/design.md。
;
; 配るのは ExtendExprorer.exe 1 本だけ。
;   - ExtendExprorer.pdb（15.4MB）は利用者には不要。入れると 5 倍以上に膨らむ
;   - Assets\app.ico は実行時に誰も読んでいない（アイコンは exe に埋まっている
;     PE リソースから LoadImageW(instance, IDI_APPLICATION, ...) で取る）

#define AppName     "ExtendExprorer"
#define AppExeName  "ExtendExprorer.exe"
#define AppPublisher "kairamu061-dev"

; 版数は CI から /DAppVersion=... で渡す。ここには書かない（2 か所に書かないため）。
; 手元で試す人のために、渡されなかったときだけ仮の値を入れる
#ifndef AppVersion
  #define AppVersion "0.0.0-local"
#endif

[Setup]
; ★ この GUID は永久に変えないこと。
;   変えると Windows から見て「別のアプリ」になり、古い版が
;   アンインストールできないまま「アプリと機能」に残る
AppId={{A11863B4-E23A-41E9-90B2-5B8DC4E7AE45}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}

; ユーザーごとにインストールする。UAC のプロンプトを出さないため。
; {autopf} は非管理者のとき {localappdata}\Programs になる
;   → C:\Users\<名前>\AppData\Local\Programs\ExtendExprorer
; アプリが設定を書く %LOCALAPPDATA%\ExtendExprorer とは別の場所（消す範囲が違う）
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes

; 本体は win-x64 のみ
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

; ★ 起動中の検出は自分でやる（下の [Code]）。Restart Manager に閉じさせない。
;   閉じる経路が WM_ENDSESSION になり、そこは session E-13（ログオフ）として
;   未確認のまま残っている道。入れ替えのついでに配置を失うのが最悪なので、
;   閉じるのは人にやってもらう（docs/installer/design.md「選ばなかったもの」）
CloseApplications=no
RestartApplications=no

OutputBaseFilename=ExtendExprorer-setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExeName}
UninstallDisplayName={#AppName}

[Languages]
Name: "ja"; MessagesFile: "compiler:Languages\Japanese.isl"

[Tasks]
; 既定では作らない（チェックを外した状態で出す）。既定で作ると消すのが手間
Name: "desktopicon"; Description: "デスクトップにショートカットを作る"; \
  GroupDescription: "追加のショートカット:"; Flags: unchecked

[Files]
; ★ exe 1 本だけ
Source: "..\publish-win32\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}";       Filename: "{app}\{#AppExeName}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "{#AppName} を実行する"; \
  Flags: nowait postinstall skipifsilent

[Code]
{ 起動しているかを、窓のクラス名で見る（MainWindow.ClassName と同じ文字列）。
  プロセス名やミューテックスより確かで、アプリ側の変更が要らない。

  tmp\bin\win32-<sha>\ のような別の場所から古い版が動いていても止まる。
  これは意図——古い版が動いたままだと %LOCALAPPDATA%\ExtendExprorer\session.json を
  取り合い、後に閉じた方が勝つ（2026-09-23 の動作確認で実際に起きていた） }
function AppIsRunning(): Boolean;
begin
  Result := FindWindowByClassName('ExtendExprorer.MainWindow') <> 0;
end;

function WarnIfRunning(): Boolean;
begin
  Result := True;
  if AppIsRunning() then
  begin
    MsgBox('ExtendExprorer が起動しています。' + #13#10 +
           '閉じてから、もう一度お試しください。', mbError, MB_OK);
    Result := False;
  end;
end;

{ ★ インストールとアンインストールの両方で見る。
  片方だけだと、起動したままアンインストールして残骸が出る }
function InitializeSetup(): Boolean;
begin
  Result := WarnIfRunning();
end;

function InitializeUninstall(): Boolean;
begin
  Result := WarnIfRunning();
end;

{ アンインストールのあとに、設定を消すか聞く。
  既定は「残す」——入れ直すときに消えている方が痛い }
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var
  DataDir: String;
begin
  if CurUninstallStep <> usPostUninstall then
    Exit;

  DataDir := ExpandConstant('{localappdata}\ExtendExprorer');
  if not DirExists(DataDir) then
    Exit;

  if MsgBox('設定（タブやペインの配置）も削除しますか？' + #13#10 +
            '残しておくと、次に入れ直したときに同じ状態で開きます。' + #13#10 + #13#10 +
            DataDir,
            mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES then
    DelTree(DataDir, True, True, True);
end;
