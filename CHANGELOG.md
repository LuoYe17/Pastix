# Changelog

本项目所有的显著变更都会记录在这里。

格式参考 [Keep a Changelog](https://keepachangelog.com/zh-CN/1.1.0/)，
版本遵循 [Semantic Versioning](https://semver.org/lang/zh-CN/)。

## [1.0.0] - 2026-05

第一个稳定版本。

### Added
- 剪贴板图片历史：缩略图预览、DPAPI 加密存储、按条数和总大小双重 LRU 淘汰
- v3 存储格式（文本 + 图片）
- 置顶条目（Pin）到顶部，且不计入历史上限
- 单条历史删除
- 自定义细滚动条，与磨砂深色 UI 一致
- 第二次启动时弹出"开机自启"引导提示，仅提示一次
- README 配套视觉资源：hero 图、icon、social preview

### Changed
- Pin 图标合并为状态指示与切换按钮：未置顶时灰色描线、已置顶时蓝色实心，悬停时左侧追加删除按钮
- 存储格式从 v1 演进到 v3（v2 增加 Pin 字段，v3 增加图片支持），加载时向后兼容

## [0.2.0] - 2026-05

持久化与设置就位。

### Added
- 历史本地持久化（history.dat），DPAPI 加密，仅当前 Windows 账户可解密
- 设置面板：自定义全局热键、最大历史条数、开机自启、一键清空历史

### Fixed
- 修饰键组合（如 Ctrl+Shift+V）在热键捕获输入框中无法正确录入的问题

## [0.1.0] - 2026-05

第一个发布版本。

### Added
- 全局热键唤起浮窗（默认 Ctrl+Shift+V）
- 系统托盘 + 单实例
- 文本剪贴板历史，重复条目自动上移
- 浮窗顶部模糊搜索，即时过滤
- 全键盘流：↑↓ Enter Esc 选中确认
- 选定后自动写回剪贴板 + 模拟 Ctrl+V 粘贴到原焦点窗口
- 磨砂深色 UI：自绘圆角控件、矢量图标、托盘图标
- 自绘 Toast 反馈
- GitHub Actions CI 自动构建 + tag 触发自动发版
