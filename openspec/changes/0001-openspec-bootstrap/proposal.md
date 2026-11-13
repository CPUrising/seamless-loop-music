# Proposal: 落地 SDD / OpenSpec 流程骨架

## Why

当前仓库只有 `AGENTS.md` 一份人机契约，缺少它依赖的规范资产（`openspec/` 目录、追踪矩阵、验证报告目录、唯一验证入口脚本）。结果是：

- 规则写了，但没有可执行的载体，AI 仍可能退回"氛围编程"；
- 测试工程存在，但没有任何统一入口产出证据，CI 也只构建不跑测试；
- 后续任何变更都没有可归档的位置，规范与实现无法对齐。

本变更的目标是**把流程变成可执行的基础设施**，而不是继续增加文档口号。

## What Changes

1. 建立 `openspec/` 目录骨架：`config.yaml`、`project.md`、`AGENTS.md`（指向根级契约）、`specs/`、`changes/archive/`。
2. 建立证据基础设施：`docs/traceability.md`（种子表）、`docs/verify/TEMPLATE.md`、`docs/regression-log.md`、`docs/verification-debt.md`。
3. 建立唯一验证入口 `scripts/verify.ps1`，支持 `-Scope` 与 `-Change`，日志写入 `artifacts/`。
4. 执行一次真实验证并把结果回填 `docs/verify/0001-openspec-bootstrap.md`。

## Capabilities

### New Capabilities

无。本变更不新增任何播放器行为能力。

### Modified Capabilities

无。`skip_specs: true`。

## Impact

- **新增文件**：`openspec/**`、`docs/traceability.md`、`docs/verify/TEMPLATE.md`、`docs/regression-log.md`、`docs/verification-debt.md`、`scripts/verify.ps1`
- **修改文件**：无源码改动；如需在 `AGENTS.md` 第 2 节更新"已建立"状态，随本变更一并提交
- **影响的测试**：不新增测试；仅新增执行入口
- **Out of scope（明确不做）**：
  - 不清理第 10 节列出的死代码（须单独 change）
  - 不编写各能力的基线规范正文（须按能力逐个 change 补）
  - 不修改 CI，不新增自动测试步骤
  - 不安装 `openspec` CLI（见 design 决策 D3）

## 验收口径

- 全新会话的 AI 只读 `AGENTS.md` + `openspec/project.md`，能说出本次改动该走哪五个阶段。
- 运行 `scripts/verify.ps1 -Scope all` 能产出日志，且报告能如实反映 PASS / FAIL / NOT RUN。
