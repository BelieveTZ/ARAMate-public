#ifndef PublicRelease
  #ifndef Snapshot
    #error Snapshot must be supplied by release.ps1 using /DSnapshot
  #endif
#endif
#ifndef AppBinary
  #define AppBinary "..\..\..\work\installer-payload\ARAMate.exe"
#endif
[Setup]
SetupIconFile=..\AppIcon.ico
AppId={{B036EDC1-313F-422B-A342-54F180F30D0B}
AppName=ARAMate 海斗助手
AppVersion={#GetStringFileInfo(AppBinary, "ProductVersion")}
AppPublisher=ARAMate
DefaultDirName={code:GetDefaultDirectory}
DefaultGroupName=ARAMate 海斗助手
UsePreviousAppDir=no
UsePreviousGroup=no
DisableDirPage=no
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=commandline
UsedUserAreasWarning=no
#ifdef PublicRelease
OutputDir=..\public
OutputBaseFilename=ARAMate-Setup-{#GetStringFileInfo(AppBinary, "ProductVersion")}
#else
OutputDir=..
OutputBaseFilename=ARAMate-安装程序-{#GetStringFileInfo(AppBinary, "ProductVersion")}
#endif
Compression=lzma2/max
SolidCompression=yes
LZMANumBlockThreads=2
WizardStyle=modern
UninstallDisplayIcon={app}\ARAMate.exe
CloseApplications=yes
RestartApplications=no
AppMutex=Local\ARAMate-SingleInstance,Local\BelieveTZ-ARAM-Companion-SingleInstance
MinVersion=10.0
ArchitecturesAllowed=x64compatible
[Languages]
#ifdef PublicRelease
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
#else
Name: "chinesesimplified"; MessagesFile: "..\..\..\work\ChineseSimplified.isl"
#endif
[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; Flags: checkedonce
[Dirs]
Name: "{app}"; Permissions: users-modify; Check: IsAdminInstallMode
[Files]
#ifndef PublicRelease
Source: "..\网站数据\assets\*"; DestDir: "{app}\网站数据\assets"; Flags: onlyifdoesntexist recursesubdirs createallsubdirs uninsneveruninstall
#endif
Source: "RuntimeCheck.exe"; Flags: dontcopy
Source: "MicrosoftEdgeWebview2Setup.exe"; Flags: dontcopy
Source: "..\lib\*.dll"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\lib\WebView2-LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#AppBinary}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\gain-board.json"; DestDir: "{app}"; Flags: ignoreversion
#ifdef PublicRelease
Source: "payload\设置游戏目录.exe"; DestDir: "{app}"; Flags: ignoreversion
#else
Source: "..\..\..\work\installer-payload\设置游戏目录.exe"; DestDir: "{app}"; Flags: ignoreversion
#endif
#ifdef PublicRelease
Source: "payload\英雄别名.json"; DestDir: "{app}"; Flags: onlyifdoesntexist
#else
Source: "..\..\..\work\installer-payload\英雄别名.json"; DestDir: "{app}"; Flags: onlyifdoesntexist
#endif
Source: "商店套装设置.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall
#ifdef PublicRelease
Source: "payload\使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion
#else
Source: "..\..\..\work\installer-payload\使用说明.txt"; DestDir: "{app}"; Flags: ignoreversion
#endif
#ifndef PublicRelease
Source: "..\网站数据\website-state.json"; DestDir: "{app}\网站数据"; Flags: onlyifdoesntexist uninsneveruninstall
#endif
#ifndef PublicRelease
Source: "..\网站数据\snapshots\{#Snapshot}\*"; DestDir: "{app}\网站数据\snapshots\{#Snapshot}"; Flags: ignoreversion recursesubdirs createallsubdirs
#endif
[InstallDelete]
Type: files; Name: "{app}\BelieveTZ的海斗小助手.exe"
Type: files; Name: "{autoprograms}\BelieveTZ海斗小助手\*.lnk"
Type: files; Name: "{autoprograms}\BelieveTZ的海斗小助手\*.lnk"
Type: dirifempty; Name: "{autoprograms}\BelieveTZ的海斗小助手"
Type: files; Name: "{autodesktop}\BelieveTZ的海斗小助手.lnk"
Type: files; Name: "{autoprograms}\BelieveTZ海斗小助手\BelieveTZ海斗小助手.lnk"
Type: files; Name: "{autoprograms}\BelieveTZ海斗小助手\设置游戏目录.lnk"
Type: dirifempty; Name: "{autoprograms}\BelieveTZ海斗小助手"
Type: files; Name: "{autodesktop}\BelieveTZ海斗小助手.lnk"
Type: files; Name: "{app}\诊断说明.txt"
Type: files; Name: "{app}\海克斯识别耗时.txt"
Type: filesandordirs; Name: "{localappdata}\BelieveTZ-ARAM-Companion\诊断记录"
[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\ARAMate\诊断记录"
Type: filesandordirs; Name: "{localappdata}\BelieveTZ-ARAM-Companion\诊断记录"
[Icons]
Name: "{group}\ARAMate 海斗助手"; Filename: "{app}\ARAMate.exe"; WorkingDir: "{app}"
Name: "{group}\设置游戏目录"; Filename: "{app}\设置游戏目录.exe"; WorkingDir: "{app}"
Name: "{autodesktop}\ARAMate 海斗助手"; Filename: "{app}\ARAMate.exe"; WorkingDir: "{app}"; Tasks: desktopicon
[Run]
#ifndef PublicRelease
Filename: "{sys}\compact.exe"; Parameters: "/C /S:""{app}\网站数据"" /I /Q"; Flags: runhidden waituntilterminated
#endif
Filename: "{app}\设置游戏目录.exe"; Parameters: "--auto"; Flags: runhidden waituntilterminated runasoriginaluser
Filename: "{app}\ARAMate.exe"; Description: "启动 ARAMate 海斗助手"; Flags: nowait postinstall skipifsilent runasoriginaluser


#include "web-runtime.iss"
