# GlassBoard

GlassBoard 是 Windows 桌面上的玻璃 Dock 和应用启动面板。点击九点按钮展开面板，双击图标打开应用，点击面板外侧即可收起。支持拖入快捷方式、程序、此电脑和回收站。

## 下载和运行

[下载 Windows x64 便携版](https://github.com/sansui-jiuhenshuai/GlassBoard/releases/latest/download/GlassBoard-win-x64.zip)

将 ZIP 完整解压到固定目录，双击其中的 `GlassBoard.exe`。压缩包已包含 .NET 8 桌面运行时。玻璃效果推荐使用 Windows 11 并启用系统透明效果。

在面板的设置中可以调整 Dock 显示数量、锁定位置和开启登录时启动。应用列表和设置保存在当前用户的 `%LOCALAPPDATA%\GlassBoard` 目录。

## 从源码构建

在装有 .NET 8 SDK 的 Windows 上执行：

```powershell
dotnet publish GlassBoard.csproj -c Release -r win-x64 --self-contained true
```
