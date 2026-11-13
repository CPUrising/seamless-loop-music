# Tasks

> 共 8 项，符合 `AGENTS.md` 第 4.1 节上限（>8 必须拆分）。

## 1. 规范骨架

- [ ] 1.1 创建 `openspec/` 结构：`config.yaml`（`schema: spec-driven`，语言 zh-CN，结构关键字保留英文）、`AGENTS.md`（指向根级契约）、`specs/.gitkeep`、`changes/archive/.gitkeep`
- [ ] 1.2 编写 `openspec/project.md`：项目定位、In/Out scope、锁定技术栈、工程标准、数据与隐私、命令、AI 协作纪律

## 2. 证据基础设施

- [ ] 2.1 创建 `docs/traceability.md`：能力分区（`playback` / `loop` / `library` / `sync` / `statistics`）与编号段（T01–T99）种子表，状态列留空
- [ ] 2.2 创建 `docs/verify/TEMPLATE.md`、`docs/regression-log.md`、`docs/verification-debt.md`：只写表头与填写说明，**不预填任何"通过"**

## 3. 验证入口

- [ ] 3.1 编写 `scripts/verify.ps1`：参数 `-Scope all|build|unit`、`-Change <id>`；日志写入 `artifacts/verify-<change>-<timestamp>.log`；失败返回非 0；脚本只执行不写结论
- [ ] 3.2 更新 `AGENTS.md` 第 2 节状态列：`openspec/`、`scripts/verify.ps1`、`docs/verify/`、`docs/traceability.md` 改为"已建立"（仅改状态，不改规则）

## 4. 验证

- [ ] 4.1 执行 `powershell -File scripts/verify.ps1 -Scope all -Change 0001-openspec-bootstrap`，记录退出码、耗时与日志路径
- [ ] 4.2 回填 `docs/verify/0001-openspec-bootstrap.md`（环境 / 命令 / 场景覆盖 / 偏差 / 结论），并按 `AGENTS.md` 第 7.2 节人工校验规范格式，注明"人工校验（CLI 未安装）"

## 完成定义（DoD）

- 4.1 真实执行过，日志路径可在报告中点开；
- 4.2 中每条结果均为 PASS / FAIL / NOT RUN 之一，`NOT RUN` 已进 `docs/verification-debt.md`；
- `AGENTS.md` 第 2 节与本变更实际落地情况一致（无超前声明）。
