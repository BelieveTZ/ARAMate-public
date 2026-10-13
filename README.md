# ARAMate 海斗助手

ARAMate 是 Windows 上的英雄联盟海斗数据与出装助手，帮助玩家查看统计数据、挑选海克斯与装备。

## 主要功能

- 英雄总榜与英雄详情：查看英雄表现、海克斯、召唤师技能和技能加点。
- 海克斯提升榜：查看达到胜率与提升门槛的英雄与海克斯、海克斯组合。
- 出装与海克斯组合推荐：按所选方案展示核心购买顺序及后续装备评级。
- 一键导入游戏内商店套装；支持选人时自动导入。
- 选人界面浮窗：英雄表现徽标、召唤师技能与加点提示。
- 局内海克斯识别浮窗：识别选卡内容并展示统计与组合建议。游戏请使用无边框窗口模式。

## 安装

从本仓库 Releases 下载 `ARAMate-Setup-<版本>.exe` 并运行，支持 64 位 Windows 10/11。

程序界面依赖 Microsoft Edge WebView2 运行时。Windows 11 和大多数 Windows 10 已经自带；如果电脑上没有，安装程序会自动下载并安装它，这一步需要联网。

安装包不内置统计数据，首次启动时会自动下载，通常需要 1–2 分钟，进度显示在右下角。下载失败时，点击右下角的「检查数据更新」重试即可。

## 数据来源

数据来自 [aramkit.cn](https://aramkit.cn) 高分段对局统计。统计学胜率与提升均按样本场次作保守修正，供选择时参考。

## 从源码构建

需要 Windows x64、Windows PowerShell、.NET Framework 4.x 的 C# 编译器以及 Windows 系统 WinMetadata（脚本从 Windows 目录读取）；WebView2 SDK 程序集与所需资源已随源码提供。运行程序还需要 WebView2 运行时。

在源码根目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

生成 `ARAMate.exe`。构建公开安装包还需要安装 Inno Setup 6：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-public.ps1
# 编译器没有安装在标准位置时：
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-public.ps1 -ISCC '安装位置\ISCC.exe'
```

安装包输出到 `public/ARAMate-Setup-<版本>.exe`。公开构建不会同步已安装程序，也不会下载统计数据。

## 声明

ARAMate 未获 Riot Games 认可，不代表 Riot Games 或任何参与制作、管理 Riot Games 相关产品的人员的观点或意见。Riot Games 及其相关产品均为 Riot Games, Inc. 的商标或注册商标。

ARAMate isn't endorsed by Riot Games and doesn't reflect the views or opinions of Riot Games or anyone officially involved in producing or managing Riot Games properties. Riot Games, and all associated properties are trademarks or registered trademarks of Riot Games, Inc.

© BelieveTZ，保留所有权利。公开源码仅供查看，未授予任何开源许可。第三方组件遵循其各自声明。
