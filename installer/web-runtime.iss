
[Code]

const UninstallKey = 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{B036EDC1-313F-422B-A342-54F180F30D0B}_is1';
var
  OriginalDirEditTop, OriginalDirBrowseTop: Integer;
  RelaunchedAsAdmin: Boolean;

function PreviousDirectory: String;
var Value: String;
begin
  Result := '';
  if RegQueryStringValue(HKCU, UninstallKey, 'Inno Setup: App Path', Value) then begin
    Result := RemoveBackslashUnlessRoot(Value);
    Exit;
  end;
  if RegQueryStringValue(HKCU, UninstallKey, 'InstallLocation', Value) then begin
    Result := RemoveBackslashUnlessRoot(Value);
    Exit;
  end;
  if RegQueryStringValue(HKLM64, UninstallKey, 'Inno Setup: App Path', Value) then begin
    Result := RemoveBackslashUnlessRoot(Value);
    Exit;
  end;
  if RegQueryStringValue(HKLM64, UninstallKey, 'InstallLocation', Value) then begin
    Result := RemoveBackslashUnlessRoot(Value);
    Exit;
  end;
  if RegQueryStringValue(HKLM32, UninstallKey, 'Inno Setup: App Path', Value) then begin
    Result := RemoveBackslashUnlessRoot(Value);
    Exit;
  end;
  if RegQueryStringValue(HKLM32, UninstallKey, 'InstallLocation', Value) then begin
    Result := RemoveBackslashUnlessRoot(Value);
    Exit;
  end;
end;

function NormalizeDisplayVersion(Value: String): String;
var I, Count, Number: Integer; Part, Version: String; C: Char;
begin
  Result := '';
  Part := '';
  Version := '';
  Count := 0;
  for I := 1 to Length(Value) + 1 do begin
    if I > Length(Value) then C := '.' else C := Value[I];
    if C = '.' then begin
      if Part = '' then Exit;
      Number := StrToIntDef(Part, -1);
      if (Number < 0) or (Number > 65535) then Exit;
      Count := Count + 1;
      if Count > 4 then Exit;
      if Count <= 3 then begin
        if Version <> '' then Version := Version + '.';
        Version := Version + IntToStr(Number);
      end;
      Part := '';
    end else begin
      if (C < '0') or (C > '9') then Exit;
      Part := Part + C;
    end;
  end;
  if (Count = 3) or (Count = 4) then Result := Version;
end;

function ExecutableVersion(Directory: String): String;
var Major, Minor, Revision, Build: Word;
begin
  Result := '';
  if Directory <> '' then
    if GetVersionComponents(AddBackslash(Directory) + 'ARAMate.exe',
      Major, Minor, Revision, Build) then
      Result := IntToStr(Major) + '.' + IntToStr(Minor) + '.' + IntToStr(Revision);
end;

{ 先读目录框里的目录：注册表可能仍记着已改名的旧目录（BelieveTZ的海斗小助手）和旧 DisplayVersion }
function PreviousVersion(Shown: String): String;
var Value: String;
begin
  Result := ExecutableVersion(Shown);
  if Result <> '' then Exit;
  Result := ExecutableVersion(PreviousDirectory);
  if Result <> '' then Exit;
  if RegQueryStringValue(HKCU, UninstallKey, 'DisplayVersion', Value) then begin
    Result := NormalizeDisplayVersion(Value);
    Exit;
  end;
  if RegQueryStringValue(HKLM64, UninstallKey, 'DisplayVersion', Value) then begin
    Result := NormalizeDisplayVersion(Value);
    Exit;
  end;
  if RegQueryStringValue(HKLM32, UninstallKey, 'DisplayVersion', Value) then begin
    Result := NormalizeDisplayVersion(Value);
    Exit;
  end;
end;
function IsLegacyDirectory(Path: String): Boolean;
begin
  Result := (CompareText(ExtractFileName(Path), 'BelieveTZ的海斗小助手') = 0) or
    (CompareText(ExtractFileName(Path), 'BelieveTZ海斗小助手') = 0);
end;
function GetDefaultDirectory(Param: String): String;
var Previous: String;
begin
  Previous := PreviousDirectory;
  if Previous = '' then Result := ExpandConstant('{localappdata}\Programs\ARAMate')
  else if IsLegacyDirectory(Previous) then Result := AddBackslash(ExtractFileDir(Previous)) + 'ARAMate'
  else Result := Previous;
end;
procedure InitializeWizard;
var RightEdge: Integer;
begin
  OriginalDirEditTop := WizardForm.DirEdit.Top;
  OriginalDirBrowseTop := WizardForm.DirBrowseButton.Top;
  RightEdge := WizardForm.PageDescriptionLabel.Left + WizardForm.PageDescriptionLabel.Width;
  WizardForm.PageDescriptionLabel.Left := WizardForm.PageNameLabel.Left;
  WizardForm.PageDescriptionLabel.Width := RightEdge - WizardForm.PageDescriptionLabel.Left;
end;

procedure CurPageChanged(CurPageID: Integer);
var Previous, Version: String; Offset: Integer;
begin
  if CurPageID <> wpSelectDir then Exit;
  Previous := PreviousDirectory;
  if Previous = '' then Exit;
  Version := PreviousVersion(WizardDirValue);
  if Version <> '' then Version := ' ' + Version;
  WizardForm.SelectDirBrowseLabel.Visible := False;
  WizardForm.SelectDirLabel.AutoSize := False;
  WizardForm.SelectDirLabel.WordWrap := True;
  WizardForm.SelectDirLabel.Caption := '检测到已安装 ARAMate 海斗助手' + Version +
    '，位于下面的目录，是否覆盖安装？如果想安装到新的位置，请在下方更改安装目录。';
  WizardForm.SelectDirLabel.AdjustHeight;
  Offset := WizardForm.SelectDirLabel.Top + WizardForm.SelectDirLabel.Height +
    ScaleY(8) - OriginalDirEditTop;
  if Offset < 0 then Offset := 0;
  WizardForm.DirEdit.Top := OriginalDirEditTop + Offset;
  WizardForm.DirBrowseButton.Top := OriginalDirBrowseTop + Offset;
end;

#include "dir-access.iss"

procedure CancelButtonClick(CurPageID: Integer; var Cancel, Confirm: Boolean);
begin
  if RelaunchedAsAdmin then begin
    Cancel := True;
    Confirm := False;
  end;
end;

function NextButtonClick(CurPageID: Integer): Boolean;
var Previous, WriteError, Target, DefaultDir: String; Choice, ErrorCode: Integer;
  Started: Boolean;
begin
  Result := True;
  if (CurPageID <> wpSelectDir) or WizardSilent then Exit;
  WriteError := DirectoryWriteError(WizardDirValue);
  if WriteError <> '' then begin
    Result := False;
    if IsAdminInstallMode then
      MsgBox(WriteError, mbError, MB_OK)
    else begin
      Target := WizardDirValue;
      DefaultDir := ExpandConstant('{localappdata}\Programs\ARAMate');
      Choice := TaskDialogMsgBox('无法写入“' + Target + '”',
        '当前账户没有这个目录的写入权限，磁盘根目录下的新文件夹（如 E:\ARAMate）在部分电脑上需要管理员权限。' + #13#10#13#10 +
        '可以批准管理员权限安装到这里：安装程序会让本机用户都能修改这个目录，以后运行和更新数据都不需要管理员权限。也可以改用默认目录：' + #13#10 + DefaultDir,
        mbError, MB_YESNOCANCEL, ['以管理员身份安装到此目录', '改用默认目录', '返回修改'], IDYES);
      if Choice = IDYES then begin
        WizardForm.Hide;
        { Inno 在安装开始前禁止 Exec/ShellExec 安装程序自身，经 cmd 的 start /wait 启动；
          cmd /c 不传 start 的退出码，须延迟展开 exit !errorlevel! }
        Started := Exec(ExpandConstant('{cmd}'), '/v:on /c start "" /wait "' + ExpandConstant('{srcexe}') +
          '" /ALLUSERS /DIR="' + Target + '" & exit !errorlevel!', '', SW_HIDE, ewWaitUntilTerminated, ErrorCode);
        if Started and (ErrorCode = 0) then begin
          RelaunchedAsAdmin := True;
          WizardForm.Close;
        end else begin
          WizardForm.Show;
          if not Started and (ErrorCode <> 1223) then
            MsgBox(SysErrorMessage(ErrorCode), mbError, MB_OK);
        end;
      end else if Choice = IDNO then
        WizardForm.DirEdit.Text := DefaultDir;
    end;
    Exit;
  end;
  Previous := PreviousDirectory;
  if Previous = '' then Exit;
  if CompareText(RemoveBackslashUnlessRoot(WizardDirValue),
    RemoveBackslashUnlessRoot(GetDefaultDirectory(''))) <> 0 then
    Result := MsgBox('你选择了与当前安装不同的目录。原目录（' + Previous +
      '）中的旧版本不会被删除，电脑上会同时存在两份程序。确定安装到新目录吗？',
      mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
end;

function MigrateInstallDirectory: String;
var Previous, Target: String;
begin
  Result := '';
  Previous := PreviousDirectory;
  Target := RemoveBackslashUnlessRoot(WizardDirValue);
  if (Previous <> '') and IsLegacyDirectory(Previous) and DirExists(Previous) and
     not DirExists(Target) and not FileExists(Target) and
     (CompareText(ExtractFileDir(Previous), ExtractFileDir(Target)) = 0) then
    if not RenameFile(Previous, Target) then
      Result := '无法改名旧安装目录，请关闭旧程序并检查目录权限后重试。安装已中止，原目录与数据保持不变。';
end;

function HasWebRuntime: Boolean;
var Version: String;
begin
  Result := (RegQueryStringValue(HKLM32, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0')) or
    (RegQueryStringValue(HKCU, 'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}', 'pv', Version) and (Version <> '') and (Version <> '0.0.0.0'));
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ResultCode: Integer; NetRelease: Cardinal;
begin
  Result := DirectoryWriteError(WizardDirValue);
  if Result <> '' then Exit;
  if not RegQueryDWordValue(HKLM32, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', NetRelease) then NetRelease := 0;
  if NetRelease < 394802 then begin
    Result := '缺少 .NET Framework 4.6.2 或更高版本。请通过 Windows 更新或微软官网安装 .NET Framework 4.8 后重试：https://dotnet.microsoft.com/download/dotnet-framework 。旧版本及数据保持不变。';
    Exit;
  end;
  ExtractTemporaryFile('RuntimeCheck.exe');
  if not Exec(ExpandConstant('{tmp}\RuntimeCheck.exe'), '', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) then begin
    Result := '无法检查 Windows 文字识别组件。请重启电脑后重试。旧版本及数据保持不变。';
    Exit;
  end;
  if ResultCode <> 0 then begin
    if ResultCode = 2 then
      Result := '缺少简体中文 OCR 文字识别组件。请在 Windows 设置 → 时间和语言 → 语言和区域中添加中文（简体，中国），在语言选项中安装基本输入/文字识别组件，然后重试。旧版本及数据保持不变。'
    else
      Result := 'Windows 文字识别组件不可用。请完成 Windows 更新并检查简体中文语言组件，然后重试。旧版本及数据保持不变。';
    Exit;
  end;
  if not HasWebRuntime then begin
    ExtractTemporaryFile('MicrosoftEdgeWebview2Setup.exe');
    if not Exec(ExpandConstant('{tmp}\MicrosoftEdgeWebview2Setup.exe'), '/silent /install', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) or not HasWebRuntime then
      Result := '未能安装 Microsoft Edge WebView2 运行环境。请连接网络后重试；旧版本及数据保持不变。';
  end;
  if Result = '' then Result := MigrateInstallDirectory;
end;
