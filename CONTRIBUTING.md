# 贡献指南

欢迎为 Pastix 做出贡献。

## 报告 Bug

请通过 [Issues](https://github.com/LuoYe17/Pastix/issues) 提交，使用 Bug 报告模板，提供：

- 复现步骤
- 期望行为 vs 实际行为
- 环境信息（Windows 版本、显示器分辨率、DPI 缩放）
- 截图或录屏（如适用）

## 提交功能建议

通过 Issue 使用 Feature Request 模板。请在描述中说明：

- 你想解决什么问题
- 现有方案为什么不行
- 你期望的交互流程

## 提交 Pull Request

1. Fork 本仓库
2. 创建特性分支：`git checkout -b feat/xxx` 或 `fix/xxx`
3. 提交改动：`git commit -m "feat(scope): brief description"`
4. 推送：`git push origin feat/xxx`
5. 在 GitHub 上发起 PR

### 提交规范

提交信息遵循 [Conventional Commits](https://www.conventionalcommits.org/zh-hans/)：

```
<type>(<scope>): <subject>
```

type：
- `feat` 新功能
- `fix` 修复 Bug
- `perf` 性能优化
- `refactor` 重构（不改功能不修 Bug）
- `docs` 文档
- `chore` 构建/工具链

## 开发约定

项目遵循 [.kiro/steering/rules.md](.kiro/steering/rules.md) 中的代码风格指引：

- **简洁优先**：用最少的代码解决问题，不做投机性设计
- **手术式改动**：只动你必须动的地方，不顺手清理无关代码
- **目标驱动**：每个 PR 都该有可验证的成功标准
- 与现有代码风格保持一致

## 本地开发

```bash
git clone https://github.com/LuoYe17/Pastix.git
cd Pastix
dotnet build src/Pastix/Pastix.csproj -c Release
src\Pastix\bin\Release\net48\Pastix.exe
```

修改后建议测试以下场景：
- 单屏 / 多屏 / 高 DPI
- 框选 / 窗口吸附 / 8 手柄调整
- 5 种标注工具 + 撤销/重做
- 复制到剪贴板 / 另存为

## 行为准则

请保持友善与尊重。无理或带有攻击性的言论会被关闭。
