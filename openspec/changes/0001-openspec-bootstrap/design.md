# Design: 落地 SDD / OpenSpec 流程骨架

## Context

- 项目为 **Windows-only**（.NET Framework 4.8 + WPF），CI runner 为 `windows-2022`。
- 已存在：`AGENTS.md`（v2.0 契约）、NUnit 测试工程 `SeamlessLoop.Tests`、`.github/workflows/windows-build.yml`（只构建）、`artifacts/`（已被 `.gitignore` 忽略）。
- 不存在：`openspec/`、`scripts/`、`docs/traceability.md`、`docs/verify/`、`docs/regression-log.md`、`docs/verification-debt.md`。
- 本机已确认：`dotnet --version` = `10.0.302`；`msbuild` 不在 PATH（CI 通过 `setup-msbuild` 提供）。
- 现有 `docs/` 有 90+ 篇历史文档，属于过程记录，不作为规范来源。

## Goals / Non-Goals

**Goals**

1. 任何新会话的 AI 能在不追问的情况下找到：规范在哪、变更在哪、证据写在哪、怎么跑验证。
2. 存在一个且只一个验证入口，输出可追溯日志。
3. 缺失的验证必须显式记账（`NOT RUN` 可查）。

**Non-Goals**

- 不追求覆盖率数字，不新增测试用例。
- 不重构任何业务代码。
- 不引入 Node/npm 依赖。

## Decisions

### D1. 骨架一次性落地，而不是随需求渐进建目录

- 选择：本 change 一次建全所有目录与种子文件。
- 理由：没有骨架时，"先建 change"这条规则本身无法执行，会持续产生例外。
- 放弃的方案：按需创建目录 —— 会导致每次都要临时决定路径，规范不可预测。

### D2. 验证入口用 PowerShell（`scripts/verify.ps1`），不用 bash

- 理由：项目 Windows-only，CI runner 是 `windows-2022`，PowerShell 零额外依赖；`.sh` 只能靠 Git Bash/WSL，反而不稳定。
- 放弃的方案：`verify.sh`（规范模板默认值）—— 与项目实际平台不符。

### D3. 暂不安装 `openspec` CLI，先走人工校验清单

- 理由：CLI 依赖全局 npm 包，会给一个纯 .NET 项目引入无关工具链；当前只有一份变更，人工校验成本更低。
- 代价：无法使用 `openspec archive` 自动合并增量规范。
- 退出条件：当同时进行的 change ≥ 3 或出现增量合并冲突时，再评估引入 CLI。届时单独开 change。
- 放弃的方案：立即安装 CLI —— 收益不足，且会把环境要求在文档中变成硬阻塞。

### D4. 追踪矩阵先建"种子表"，场景编号预留

- 理由：基线规范尚未编写，不能凭空登记场景；先固定能力分区（`playback` / `loop` / `library` / `sync` / `statistics`）与编号段（T01–T99 按能力分段），后续 change 只追加行。
- 风险：编号段划分可能随需求调整；调整时须在本文件记录，不许静默改号。

### D5. `docs/` 复用现有目录，不新建根级 `specs/`

- 理由：规范明确 `openspec/specs/` 是唯一基线，其余证据放 `docs/`；现有 `docs/` 已承载历史文档，新增文件只是子目录与单文件，不迁移旧文档（迁移须单独 change）。

### D6. 验证脚本的失败语义

- `verify.ps1` 自身**只负责执行并记录**，不做"是否合格"的结论。
- 退出码：`0` = 所有执行项成功；非 `0` = 至少一项失败。`NOT RUN` 不影响退出码，但必须写入日志与报告。
- 理由：避免脚本替代人做判断，防止"脚本绿了就当验收通过"。

## Risks / Trade-offs

| 风险 | 影响 | 应对 |
|---|---|---|
| 首次 `dotnet build` 可能因缺少 .NET Framework 4.8 targeting pack 失败 | G0 无法通过 | 如实记录为环境故障，按第 5.5 节重建环境后重跑；不得改脚本绕开 |
| 现有测试可能存在失败用例（历史未在 CI 运行） | 首个 change 就无法"全绿" | 不阻断骨架落地，但阻断归档；失败项逐条进 `verification-debt.md` |
| 人工校验替代 CLI | 规范格式错误可能漏检 | 按 `AGENTS.md` 第 7.2 节清单逐项勾选，勾选结果留在 `tasks.md` |
| 新增流程文件被误当作"功能完成" | 认知偏差 | 在 `docs/verify/0001` 结论中明确：本变更只交付基础设施，不代表任何 Gate 通过 |

## Migration Plan

无数据迁移。回滚方式：删除本变更新增的文件即可，不影响运行时行为。

## Open Questions

1. 是否需要在 `README.md` 增加"阶段看板"区块？（规范要求，但本次未列入 tasks；倾向下一个 change 处理）
2. 旧 `docs/` 文档是否需要归档到 `docs/archive/`？（本次不动）
