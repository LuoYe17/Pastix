# Pastix

一个轻量的 Windows 剪贴板历史工具。按下 Ctrl+Shift+V，挑一条，粘贴。

[![Build](https://github.com/LuoYe17/Pastix/actions/workflows/build.yml/badge.svg)](https://github.com/LuoYe17/Pastix/actions/workflows/build.yml)
[![License](https://img.shields.io/github/license/LuoYe17/Pastix)](LICENSE)

## 起因

Windows 自带的 Win+V 只记 25 条，重启就丢，没法搜索。Ditto 强大但 UI 像 Win98。所以写了一个：100 条历史、本地 DPAPI 加密、零登录、单文件。

跟 [Snapix](https://github.com/LuoYe17/Snapix) 是姊妹篇——一个负责截取，一个负责留痕。

## 特性

- 单文件 ~42 KB，无需安装，双击即用
- 默认热键 Ctrl+Shift+V（设置可改）
- 100 条文本历史，重复条目自动上移并刷新时间
- 模糊搜索：在浮窗顶部输入即时过滤
- 键盘 ↑↓ Enter Esc 全键盘流，鼠标也能用
- 选定后自动写回剪贴板 + 模拟 Ctrl+V 粘贴到原焦点窗口
- 本地 DPAPI 加密：history.dat 只有当前 Windows 账户能解密
- 零网络、零登录、零注册表写入

## 系统要求

Windows 10 1903+ 或 Windows 11。系统已自带 .NET Framework 4.8，无需另装运行时。

## 自行构建

需要 .NET SDK 8 或更高（用来编译 net48 项目）。

```bash
git clone https://github.com/LuoYe17/Pastix.git
cd Pastix
dotnet build src/Pastix/Pastix.csproj -c Release
```

产物在 `src/Pastix/bin/Release/net48/Pastix.exe`。

## 状态

v0.2 持久化已就位，朝 v1.0 打磨中。

## 协议

[MIT](LICENSE)
