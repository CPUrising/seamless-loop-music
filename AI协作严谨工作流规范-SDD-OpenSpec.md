# AI 协作严谨工作流规范（SDD + OpenSpec）

> 版本：1.0
> 来源：从 `BizScan`（iOS 名片扫描复刻）项目的真实工程链路中提炼。
> 读者：任何项目的 AI 智能体（opencode / TRAE / Claude Code / Cursor 等）与人类维护者。
> 目标：让 AI 在**有边界、有证据、可审查、可归档**的流程里工作，而不是"氛围编程（Vibe Coding）"。

---

## 0. 给 AI 的读取协议（先读这一节）

如果你是被要求"按这套流程工作"的 AI，请按以下顺序执行：

1. 先完整读完本文档，尤其是第 2、3、5、8 节。
2. 在你的目标项目里建立第 4 节描述的目录骨架。
3. 把第 15 节的模板复制进项目，替换占位符。
4. 用第 14 节的"落地清单"逐项自检。
5. 之后每一次改动，都必须走第 5 节的五阶段闭环；不得跳步。

**硬性约定（全文生效）：**

- 规范正文用项目母语书写；`Requirement` / `Scenario` / `SHALL` / `MUST` 等结构关键字保留英文。
- 没有证据的"已完成"一律视为**未完成**。
- 不能通过修改断言、跳过用例、放宽条件让测试变绿。
- 任何行为变化必须走"变更（change）"，不得直接改基线规范。

---

## 1. 这套流程解决什么问题

AI 写代码最大的风险不是"写不出来"，而是：

| 风险 | 表现 | 后果 |
|---|---|---|
| 范围漂移 | 顺手加功能、顺手重构 | 需求失控、评审困难 |
| 上下文漂移 | 多轮对话后忘记约束 | 前后实现不一致 |
| 虚假完成 | 说"应该没问题"但没跑过 | 缺陷流入主线 |
| 证据缺失 | 只有代码，没有验证记录 | 无法审查、无法归档 |
| 规范腐化 | 代码改了，文档没改 | 文档与实现脱节 |

**SDD（Specification-Driven Development，规范驱动开发）** 的核心主张：

> 先把"要什么"写成可验证的合同（规范），再让 AI 按合同实现，最后用证据证明合同被满足。

代码只是产物之一；**规范与证据与代码等价重要**。

---

## 2. 三条主线与一个闭环

### 2.1 三条主线

```
规范线：黑盒观察 → 基线规范 → 增量规范 → 归档回基线
工程线：任务拆解 → 受控编码 → 测试 → 回归
证据线：验证报告 → 追踪矩阵 → 回归基线 → 验证债台账
```

三条线必须同时推进。只有代码没有证据，等于没做。

### 2.2 一个闭环

```
观察/需求
   ↓
Specification（写规范）
   ↓
Architecture / Design（设计）
   ↓
Implementation（实现）
   ↓
Test（测试）
   ↓
差异分析（哪里没满足）
   ↓
修正
   ↓
回归（确认没破坏别的）
   ↓
Archive（归档回基线）
```

---

## 3. 角色、权限与红线

### 3.1 角色分工

| 角色 | 可以做 | 不可以做 |
|---|---|---|
| 人类 | 定义意图、批准变更、审查证据、决定归档 | 把验证工作全权交给 AI 后不再审查 |
| AI 智能体 | 读上下文、拆任务、写代码与测试、跑验证命令、报告偏差 | 扩大范围、跳过测试、直接改基线规范、伪造验证结果 |
| 编辑端（可非目标平台） | 写规范、写源码与测试、跑静态校验、提交 Git | 声称"已验证"、手写生成物（如 `.xcodeproj`） |
| 验证端（目标平台 / CI） | 构建、跑测试、产出证据 | 未经变更直接改规范 |

### 3.2 五条红线（任何项目都必须有）

1. **不得扩大范围**：只做当前变更授权的事；"顺手优化"必须单独开变更。
2. **不得跳步**：Plan → Check → Build → Verify → Merge 必须完整走完。
3. **不得伪造证据**：没跑过就是没跑过；未执行必须显式登记。
4. **不得放宽断言**：失败要么修实现，要么证明是测试写错，不能改测试迎合实现。
5. **不得绕过变更改基线**：行为变化必须通过增量规范表达，归档时合并。

---

## 4. 标准目录骨架

```
<project>/
├─ openspec/                     # 规范与变更的单一真实源
│  ├─ config.yaml                # 语言、schema 配置
│  ├─ project.md                 # 全局上下文：目标、边界、技术栈、工程标准
│  ├─ AGENTS.md                  # AI 操作规程（人机契约）
│  ├─ specs/                     # 基线规范（唯一真实源）
│  │  └─ <capability>/spec.md
│  └─ changes/                   # 增量变更沙箱
│     ├─ <NNNN-name>/            # 进行中的变更
│     │  ├─ .openspec.yaml       # schema / created / skip_specs 等
│     │  ├─ proposal.md          # 为什么做、做什么、影响面
│     │  ├─ design.md            # 技术决策、权衡、风险
│     │  ├─ tasks.md             # 原子任务清单（可勾选）
│     │  └─ specs/<cap>/spec.md  # 增量规范（ADDED/MODIFIED/REMOVED）
│     └─ archive/                # 已验证并归档的变更
├─ docs/
│  ├─ traceability.md            # 验收场景 ↔ 规范 ↔ 测试 映射
│  ├─ reference-env.md           # 参考环境锁定表
│  ├─ regression-log.md          # 回归基线（只增不改）
│  ├─ verification-debt.md       # 验证债台账（未执行的验证）
│  ├─ observation/               # 黑盒观察记录
│  ├─ verify/                    # 每个变更的验证证据
│  │  ├─ TEMPLATE.md
│  │  └─ <change-id>.md
│  ├─ eval/                      # 概率性功能的评估报告
│  └─ ai-collaboration/          # 每个变更的 AI 协作留痕
├─ scripts/
│  └─ verify.sh                  # 唯一验证入口（或 verify.ps1 / verify.py）
├─ fixtures/                     # 测试数据 + Ground Truth + manifest
├─ artifacts/                    # 日志与结果包（不入库）
├─ AGENTS.md 或 CLAUDE.md        # 指向 openspec/AGENTS.md
└─ README.md                     # 唯一进度口径：阶段看板 + Gate + 证据链接
```

**规则：**

- `openspec/specs/` 是唯一基线，实施阶段禁止直接编辑。
- `docs/verify/` 的证据必须由**执行验证的机器**产出。
- `artifacts/` 不入库，但报告里要写清路径。

---

## 5. 五阶段工作循环

每一个可命名的行为增量 = 一个 change。每个 change 必须走完以下五阶段。

| 阶段 | 目的 | 典型命令 | 产物 |
|---|---|---|---|
| **Plan** | 把"要什么"写清楚 | `openspec new change <id>` 或 AI 的 propose 命令 | `proposal.md` / `design.md` / `tasks.md` / 增量 `specs/` |
| **Check** | 自检格式与完整性 | `openspec validate <id> --strict` | 校验通过 |
| **Build** | 按任务顺序受控编码 | AI 的 apply 命令 | 源码 + 测试 + 勾选的 `tasks.md` |
| **Verify** | 跑测试、分析失败、最小修复、重跑 | `bash scripts/verify.sh --change <id>` | 日志 + `docs/verify/<id>.md` |
| **Merge** | 合并增量规范、归档 | `openspec archive <id>` | 更新 `openspec/specs/` + 移入 `archive/` |

### 5.1 阶段约束

- **Plan**：禁止修改项目代码；只允许写 proposal / design / tasks / 增量 spec。
- **Build**：只允许改 `tasks.md` 列出的文件；越界前先更新 tasks.md 并说明原因。
- **Verify**：先读日志定位；最多自愈 3 轮；仍失败则停止并报告堆栈与假设。
- **Merge**：只能由人类批准后执行；归档前必须所有任务完成、所有场景有结论。

### 5.2 失败处理决策树

```
测试失败
├─ 是测试自身错误（断言/选择器写错）？
│  ├─ 是 → 修测试，重跑
│  └─ 否 → 是环境故障（模拟器损坏/依赖缺失）？
│         ├─ 是 → 重建环境，重跑，并记入偏差
│         └─ 否 → 是实现错误 → 修实现，重跑
└─ 连续 3 轮仍失败 → 停止，写报告，等人类决策
```

**禁止：** 通过删除用例、放宽断言、改测试环境参数来"制造通过"。

---

## 6. 规范写法

### 6.1 基线规范格式

```markdown
# <能力名> Capability Spec

## Purpose

用 1–3 句话说明这个能力做什么、为什么存在（建议 ≥ 50 字符，严格校验会卡）。

## Requirements

### Requirement: <英文短语，如 Capture Card>

系统 SHALL ...

#### Scenario: T01 <中文描述>

- **GIVEN** 前置条件
- **WHEN** 触发动作
- **THEN** 系统 MUST ...
- **AND** MUST NOT ...
```

**约定：**

- 需求名用英文短语，正文用项目母语。
- `SHALL` / `MUST` / `MUST NOT` 保留英文，表示强制。
- 场景名建议带稳定编号（如 `T01`），便于追踪矩阵引用。
- 一个 Requirement 可以有多个 Scenario；一个 Scenario 只描述一条可验证路径。

### 6.2 增量规范格式

增量规范只写"相对基线的差异"，不要复制整份基线。

```markdown
# <能力名> Delta

## ADDED Requirements

### Requirement: New Thing
系统 SHALL ...

#### Scenario: T21 ...

## MODIFIED Requirements

### Requirement: Existing Thing
（写完整的新版本：需求正文 + 所有仍然存活的场景）
...

## REMOVED Requirements

### Requirement: Old Thing

## RENAMED Requirements

- FROM: `### Requirement: Old Name`
- TO: `### Requirement: New Name`
```

**关键规则：**

- `MODIFIED` 必须携带**完整**的新版本（正文 + 保留的所有场景），不能只写 diff。
- 归档同步时，增量规范会被"智能合并"进基线；基线中不得残留 `## ADDED/MODIFIED/...` 这类增量标题。
- 只实现既有基线、不改变行为合同的变更，在 `.openspec.yaml` 写 `skip_specs: true`，**禁止为了通过校验而虚构需求**。

### 6.3 校验命令

```bash
openspec validate --all --strict     # 全量严格校验
openspec validate <change-id> --strict
openspec show <change-id>
openspec list --specs
```

常见报错：

| 报错 | 原因 | 修复 |
|---|---|---|
| Purpose 过短 | 少于阈值 | 补足 Purpose |
| 需求文本过长 | 塞了表格/示例 | 移到数据模型或场景 |
| 场景名不规范 | 缺少编号 | 统一 `Txx <描述>` |

---

## 7. 变更管理规则

### 7.1 命名

`NNNN-kebab-case`，编号单调递增、不复用：

```
0001-app-skeleton
0002-capture-import
0016-persistence-hardening
```

### 7.2 粒度

- 一个 change = 一个可命名的行为增量。
- 超过 **8 个任务**必须拆分。
- 纯重构 / 纯格式化 / 纯文档，各自单独成 change。

### 7.3 `.openspec.yaml` 示例

```yaml
schema: spec-driven
created: 2026-11-04
skip_specs: true          # 仅当不改变行为合同时
# retire_capabilities: true   # 仅当要整体下线某能力时
```

### 7.4 proposal / design / tasks 各自回答什么

| 文件 | 必须回答 | 不该包含 |
|---|---|---|
| `proposal.md` | 为什么做（Why）、改什么（What Changes）、影响面（Impact）、明确不做什么 | 实现细节、代码 |
| `design.md` | 关键决策与理由、备选方案、权衡、风险、迁移方案 | 需求复述 |
| `tasks.md` | 可独立验证的原子任务（建议 ≤ 2 小时/项），按依赖排序 | 模糊的"优化一下" |

**tasks 写法示例：**

```markdown
# Tasks

## 1. 数据层

- [ ] 1.1 定义 `BusinessCard` 模型与字段契约
- [ ] 1.2 实现 `BusinessCardRepository` 协议与内存替身
- [ ] 1.3 单元测试：字段契约与空判定

## 2. 界面层

- [ ] 2.1 实现列表空状态
- [ ] 2.2 UI 测试：T01 首次启动 → 空名片夹

## 3. 验证

- [ ] 3.1 `bash scripts/verify.sh --change <id>`
- [ ] 3.2 回填 `docs/verify/<id>.md`
```

---

## 8. 验证与证据规范

### 8.1 唯一验证入口

每个项目必须有且只有一个验证脚本（如 `scripts/verify.sh`），它负责：

1. 生成/准备构建产物；
2. 跑分层测试（单元 / 集成 / UI）；
3. 把日志与结果包写入 `artifacts/`；
4. 打印"下一步：回填报告"。

**示例接口：**

```bash
scripts/verify.sh                      # 全量
scripts/verify.sh --scope unit         # 只跑单元测试
scripts/verify.sh --scope ui           # 只跑 UI 测试
scripts/verify.sh --change <id>        # 绑定到某个 change
```

### 8.2 验证报告模板

每个 change 一份 `docs/verify/<change-id>.md`，缺一不可：

```markdown
# 验证报告：<change-id>

## 1. 基本信息

| 项 | 值 |
|---|---|
| change id | |
| 执行人 | |
| 执行时间 | |
| 机器 / 系统 | |
| 工具链版本 | |
| 运行环境 | |
| 命令 | `scripts/verify.sh --change <id>` |
| 日志 | `artifacts/verify-<id>-<timestamp>.log` |
| 结果包 | `artifacts/verify-<id>-<timestamp>.xcresult` |

## 2. 需求覆盖

| Requirement | Scenario | 测试方法 | 结果 | 备注 |
|---|---|---|---|---|
| | Txx | `XxxTests.testYyy` | PASS / FAIL / NOT RUN | |

## 3. 回归

| 范围 | 命令 | 结果 |
|---|---|---|
| 全量单元测试 | | |
| 全量 UI 测试 | | |
| 既有通过项是否回退 | 对照 `docs/regression-log.md` | |

## 4. 偏差与风险

| 编号 | 偏差 | 等级（阻断/严重/一般） | 处理 |
|---|---|---|---|
| D1 | | | |

## 5. 结论

- [ ] Ready for archive
- [ ] 需修复后复验
- [ ] 需人类决策
```

### 8.3 偏差等级

| 等级 | 含义 | 是否阻断归档 |
|---|---|---|
| **阻断** | 核心场景失败、虚构数据、数据损坏 | 是 |
| **严重** | 可归档，但必须新开 change 修复 | 否（但必须登记） |
| **一般** | 记录即可 | 否 |

### 8.4 证据的三条铁律

1. **未执行 ≠ 通过**：`NOT RUN` 必须写明原因和是否阻断。
2. **证据由验证端产出**：编辑端不得预填结论。
3. **可追溯**：每条 PASS 都能点开到日志或测试方法。

---

## 9. 追踪矩阵、回归基线与验证债

### 9.1 追踪矩阵（traceability）

一张表把"验收场景 ↔ 规范位置 ↔ 测试方法 ↔ 状态"锁在一起：

```markdown
| ID | 场景 | 规范位置 | 自动化测试 | 状态 |
|---|---|---|---|---|
| T01 | 首次启动 → 空列表 | `specs/card-list` → `T01 First Launch` | `LaunchUITests.testAppLaunchesToEmpty` | 通过（`0009`） |
| T03 | 权限允许/拒绝 | `specs/capture` → `T03 Permission` | 人工清单第 1–2 节 | 未执行（模拟器无相机） |
```

**维护规则：**

- 新增/改名场景时，必须同步：规范标题、本表、测试方法名。
- `状态=通过` 必须能链接到 `docs/verify/<change>.md` 的 PASS 行。
- 归档前本表不得有 `失败`；`未实现` 必须已在某个 proposal 中声明。

### 9.2 回归基线（regression-log）

每次验证通过后**追加**一行，历史行不得修改：

```markdown
| 日期 | change | 通过用例 | 失败用例 | 跳过 | 证据 |
|---|---|---|---|---|---|
| 2026-11-04 | `0016-persistence` | T18 + 62 unit + 12 UI | 0 | 真机 kill | [verify/0016](verify/0016-persistence.md) |
```

### 9.3 验证债台账（verification-debt）

把"写了但没跑过"的验证显式记账，防止把未验证代码当成已通过：

```markdown
| 模块 | 应测项 | 测试文件 | 状态 | 备注 |
|---|---|---|---|---|
| `Repository` | 重启保持 | `RepositoryTests` | 通过 | 4 项 |
| `OCR` | 真实拍摄 | `RealCardTests` | 阻塞 | 缺真机素材 |
```

状态定义：`未执行` / `执行失败` / `通过` / `阻塞`。

**结算规则：** 每一行必须由真实执行结果关闭；关闭时把证据链接写进 `docs/verify/`。

---

## 10. 跨机 / 跨角色协作

当编辑环境和验证环境不是同一台机器（例如 Windows 编辑、macOS/CI 验证）时：

| 角色 | 可以做 | 禁止 |
|---|---|---|
| 非目标平台编辑端 | 写规范、写源码与测试、跑静态校验、提交 Git | 声称"已验证"、手写生成物 |
| 目标平台验证端 | 构建、跑测试、产出证据 | 未经变更直接改规范 |
| CI | 跑同一套测试、上传 `artifacts/` | 用 CI 通过替代人工验收证据 |

**交接规则：**

1. 编辑端提交前必须通过：`openspec validate --all --strict`。
2. 验证端默认只产出证据；发现缺陷按 AGENTS.md 修复后重跑，并在偏差表登记。
3. 证据必须包含环境（工具链版本、运行环境）与每条场景的 PASS/FAIL。
4. 环境故障（模拟器损坏、依赖缺失）必须重建并记入偏差，不能掩盖。

---

## 11. 概率性功能评估（OCR / AI / 推荐等）

对结果不确定的功能，不能只用"通过/失败"判定，必须建立评估链路：

```
原始输入 → 中间输出（Raw） → 结构化结果 → 人工校正 → 最终结果
```

**评估步骤：**

1. 对 `fixtures/` 全量运行，保存原始输出与解析结果（JSON）。
2. 与 `fixtures/ground_truth/*.json` 对比：归一化后按字段判等。
3. 统计每字段精确率 / 召回率；**虚构内容单独计数并重罚**。
4. 错误分类（示例）：字段歧义、实体混淆、多值、中英混排、字段缺失、识别错字。
5. 报告写入 `docs/eval/`，并在 README 阶段看板登记。

**规则：**

- 准确率不达标不阻断归档，但必须记录原因与改进项。
- **虚构内容（无中生有）视为阻断缺陷。**
- 样本量 < 30 时只报绝对值，不报百分比，并注明样本不足。

---

## 12. AI 协作留痕

每个 change 在 `docs/ai-collaboration/<change-id>.md` 记录四节：

1. **prompt 范围**：本次授权改哪些文件、明确不做什么（引用原话或摘要，注明出处）。
2. **AI 产出**：新增/修改的文件与关键决策。
3. **人工修正点**：人类改了什么、驳回了什么、补了什么判断。
4. **回归结果**：用例数与失败数，链接到 `docs/verify/`。

**高风险判断必须单独留痕**（示例类别）：

- 字段歧义（同一词既可能是公司也可能是产品）；
- 实体混淆（人名 / 公司名）；
- 多值无标签（多个电话如何分类）；
- 信息缺失时的"合理猜测"（**必须禁止**）。

**原则：** 没有 prompt 原文就标注"无独立留痕"，**不补造、不推测**。

---

## 13. 质量门禁（Gate）

把项目切成若干阶段，每阶段一个 Gate，每个 Gate 有准入条件和通过标准。

| Gate | 准入条件 | 通过标准 |
|---|---|---|
| G0 | 基线完成 | 目标平台/CI 能生成工程并跑通冒烟测试；交接流程可照做 |
| G1 | 规范冻结 | 严格校验全绿；所有验收场景有规范与测试映射；参考环境冻结 |
| G2 | 核心逻辑完成 | 单元测试通过；核心页面与规范一致 |
| G3 | 采集/输入完成 | 相关路径通过；边界条件有检测报告 |
| G4 | 识别/处理完成 | 相关场景通过；质量基线报告产出；虚构内容为 0 |
| G5 | 持久化完成 | 杀进程/重启后数据完整 |
| G6 | 管理闭环完成 | 搜索/标签/导出等场景通过 |
| G7 | 验收完成 | 全部验收场景通过；全量回归通过；全部变更归档；规范与实现一致 |

**规则：**

- 每个 Gate 完成后必须更新 README 阶段看板并链接证据。
- "未验证"不是完成；未执行项必须进验证债台账。

---

## 14. 新项目落地清单（Bootstrapping）

按顺序执行，全部打勾才算接入完成：

- [ ] 1. 安装并初始化 OpenSpec（或等价规范工具）：`npm i -g @fission-ai/openspec && openspec init`。
- [ ] 2. 创建第 4 节目录骨架。
- [ ] 3. 写 `openspec/project.md`：目标、In/Out scope、技术栈、工程标准、数据与隐私。
- [ ] 4. 写 `openspec/AGENTS.md`：角色、红线、工作循环、验证规则（用第 15 节模板）。
- [ ] 5. 写 `openspec/config.yaml`：语言与 schema。
- [ ] 6. 建立 `docs/traceability.md`，把验收场景编号（T01…）落到规范与测试。
- [ ] 7. 建立 `docs/verify/TEMPLATE.md`。
- [ ] 8. 建立 `docs/regression-log.md` 与 `docs/verification-debt.md`。
- [ ] 9. 写 `scripts/verify.sh`（或等价脚本），作为唯一验证入口。
- [ ] 10. 配置 CI（可选但推荐）：跑同一套测试并上传 `artifacts/`。
- [ ] 11. 在 `README.md` 建立阶段看板 + Gate 表 + 证据链接。
- [ ] 12. 跑一次 G0：生成工程 + 冒烟测试 + 回填报告。
- [ ] 13. 用第一个真实小变更完整走一遍五阶段闭环，验证流程本身可用。

---

## 15. 模板库（可直接复制）

### 15.1 `openspec/config.yaml`

```yaml
schema: spec-driven

context: |
  Language: zh-CN
  All artifacts must be written in zh-CN.
  Keep OpenSpec structural headings and SHALL/MUST keywords in English.
```

### 15.2 `openspec/project.md` 骨架

```markdown
# <项目名> Project Context

> 本文件是全局项目上下文（单一真实源之一）。任何 change 的产物都必须与本文件一致。

## 1. 项目是什么

## 2. 边界
### In scope
### Out of scope

## 3. 技术栈（锁定，变更需走 change）

| 项 | 选择 | 说明 |
|---|---|---|

## 4. 工程标准

## 5. 数据与隐私

## 6. 规范资产约定

## 7. 命令

## 8. AI 协作纪律
```

### 15.3 `openspec/AGENTS.md` 骨架

```markdown
# AGENTS.md

本仓库使用 OpenSpec + SDD。所有 AI 智能体必须按本文件执行。

## 0. 开工前必读
1. `openspec/project.md`
2. `openspec/specs/`
3. `docs/traceability.md`
4. 本文件

## 1. 角色与边界
- 人类：定义意图、批准 change、审查证据、决定归档。
- 智能体：读上下文、拆任务、写代码与测试、跑验证、报告偏差。
- 智能体没有权限：扩大范围、跳过测试、直接改基线规范、伪造验证结果。

## 2. 工作循环
| 阶段 | 命令 | 产物 |
|---|---|---|
| Plan | `openspec new change <id>` | proposal / design / tasks / delta spec |
| Check | `openspec validate <id> --strict` | 校验通过 |
| Build | apply 命令 | 源码 + 测试 |
| Verify | `scripts/verify.sh --change <id>` | 日志 + `docs/verify/<id>.md` |
| Merge | `openspec archive <id>` | 更新基线 |

## 3. 命名与规模
- change id：`NNNN-kebab-case`，单调递增，不复用。
- 一个 change 只含一个行为增量；超过 8 个任务必须拆分。

## 4. 硬性技术约束
（按项目填写）

## 5. 验证与证据
- 唯一验证入口：`scripts/verify.sh`。
- 每次验证必须产出 `docs/verify/<change>.md`。
- 失败先读日志；最多自愈 3 轮；禁止放宽断言。

## 6. 范围纪律
- 顺手优化必须单独成 change。
- 概率性功能必须保留全链路留痕。
- 测试数据必须为合成数据。

## 7. 交接
- 编辑端不得声称"已验证"。
- 验证端只产出证据；发现缺陷按本文件修复后重跑并登记偏差。
```

### 15.4 `proposal.md` 模板

```markdown
# Proposal: <标题>

## Why
为什么要做这个变更。

## What Changes
- 具体改动 1
- 具体改动 2

## Capabilities
### New Capabilities
### Modified Capabilities

## Impact
- 影响的文件 / 模块
- 影响的测试
- 明确不做的事（Out of scope）
```

### 15.5 `design.md` 模板

```markdown
# Design: <标题>

## Context
现状与约束。

## Goals / Non-Goals

## Decisions
1. **决策标题**
   - 选择：
   - 理由：
   - 放弃的方案：

## Risks / Trade-offs

## Migration Plan

## Open Questions
```

### 15.6 `tasks.md` 模板

```markdown
# Tasks

## 1. <阶段>
- [ ] 1.1 ...
- [ ] 1.2 ...

## 2. <阶段>
- [ ] 2.1 ...

## 3. 验证
- [ ] 3.1 运行 `scripts/verify.sh --change <id>`
- [ ] 3.2 回填 `docs/verify/<id>.md`
```

### 15.7 增量规范模板

```markdown
# <能力名> Delta

## ADDED Requirements

### Requirement: <英文名>
系统 SHALL ...

#### Scenario: Txx <描述>
- **GIVEN** ...
- **WHEN** ...
- **THEN** ...

## MODIFIED Requirements

### Requirement: <英文名>
（完整新版本）

## REMOVED Requirements

### Requirement: <英文名>

## RENAMED Requirements

- FROM: `### Requirement: Old`
- TO: `### Requirement: New`
```

### 15.8 验证报告模板

见第 8.2 节，可直接复制为 `docs/verify/TEMPLATE.md`。

### 15.9 追踪矩阵行模板

```markdown
| Txx | <场景描述> | `specs/<cap>` → `Txx <场景名>` | `XxxTests.testYyy` | 未实现 / 已实现待执行 / 部分通过 / 通过 / 失败 |
```

### 15.10 验证债行模板

```markdown
| <模块> | <应测项> | `<测试文件>` | 未执行 / 执行失败 / 通过 / 阻塞 | <备注 + 证据链接> |
```

### 15.11 验证脚本骨架（bash 伪代码）

```bash
#!/usr/bin/env bash
set -euo pipefail

SCOPE="all"
CHANGE="local"
while [[ $# -gt 0 ]]; do
  case "$1" in
    --scope)  SCOPE="$2"; shift 2 ;;
    --change) CHANGE="$2"; shift 2 ;;
    *) echo "未知参数: $1" >&2; exit 2 ;;
  esac
done

STAMP="$(date +%Y%m%d-%H%M%S)"
mkdir -p artifacts
LOG="artifacts/verify-${CHANGE}-${STAMP}.log"

echo "== verify ==" | tee -a "$LOG"
echo "change: $CHANGE  scope: $SCOPE  log: $LOG"

run_unit_tests 2>&1 | tee -a "$LOG"
run_integration_tests 2>&1 | tee -a "$LOG"
run_ui_tests 2>&1 | tee -a "$LOG"

echo "下一步：回填 docs/verify/${CHANGE}.md"
```

---

## 16. 反模式清单（看到就要停）

| 反模式 | 为什么危险 | 正确做法 |
|---|---|---|
| "我顺手把这个也改了" | 范围失控 | 单独开 change |
| "测试应该能过" | 未验证 | 真跑一遍，贴日志 |
| 改了断言让测试变绿 | 掩盖缺陷 | 修实现或证明测试写错 |
| 直接编辑 `openspec/specs/` | 绕过变更 | 写增量规范，归档时合并 |
| 在 proposal 里写实现细节 | 阶段混淆 | 细节放 design / tasks |
| 一个 change 塞 20 个任务 | 无法审查 | 拆成多个 change |
| 把 `NOT RUN` 写成 `PASS` | 伪造证据 | 如实登记，进验证债 |
| 虚构不存在的字段 | 数据污染 | 缺失就返回空，禁止猜测 |
| 用真实个人数据做测试 | 隐私风险 | 全部合成数据 |
| 归档前不跑全量回归 | 可能引入回退 | 对照 `regression-log.md` |

---

## 17. 最小可行版 vs 完整版

如果项目很小，可以先落地"最小可行版"，再逐步补齐：

### 17.1 最小可行版（MVP，1 天内可落地）

- `openspec/project.md`
- `openspec/AGENTS.md`
- `openspec/specs/<cap>/spec.md`（至少一份）
- `openspec/changes/` 目录
- `docs/traceability.md`
- `docs/verify/TEMPLATE.md`
- `scripts/verify.sh`
- README 阶段看板

### 17.2 完整版（推荐，随项目成长补齐）

在 MVP 基础上增加：

- `docs/regression-log.md`
- `docs/verification-debt.md`
- `docs/ai-collaboration/`
- `docs/eval/`（概率性功能）
- `docs/reference-env.md`
- CI 工作流
- Gate 表
- 跨机协作规则

---

## 18. 一页速查表

```
┌─────────────────────────────────────────────────────────────┐
│ 每次改动前                                                   │
│  □ 读 project.md / specs / traceability / AGENTS.md          │
│  □ 确认这次改的是哪个 change、范围是什么                     │
├─────────────────────────────────────────────────────────────┤
│ 写规范（Plan）                                               │
│  □ proposal：Why / What / Impact / Out of scope              │
│  □ design：决策 / 权衡 / 风险                                │
│  □ tasks：原子任务，≤ 8 项                                   │
│  □ delta spec：ADDED / MODIFIED / REMOVED / RENAMED          │
├─────────────────────────────────────────────────────────────┤
│ 自检（Check）                                                │
│  □ openspec validate <id> --strict                           │
├─────────────────────────────────────────────────────────────┤
│ 实现（Build）                                                │
│  □ 只改 tasks.md 列出的文件                                  │
│  □ 每个任务完成立刻勾选                                      │
├─────────────────────────────────────────────────────────────┤
│ 验证（Verify）                                               │
│  □ scripts/verify.sh --change <id>                           │
│  □ 失败先读日志；最多自愈 3 轮                               │
│  □ 禁止放宽断言                                              │
│  □ 回填 docs/verify/<id>.md（环境 / 场景 / 偏差 / 结论）     │
│  □ 追加 docs/regression-log.md                               │
├─────────────────────────────────────────────────────────────┤
│ 归档（Merge，需人类批准）                                    │
│  □ 全部任务完成、全部场景有结论                              │
│  □ openspec archive <id>                                     │
│  □ 更新 README 阶段看板与证据链接                            │
└─────────────────────────────────────────────────────────────┘
```

---

## 19. 结语

这套流程的本质不是"增加文档负担"，而是**把 AI 的自由度约束在可验证的边界内**：

- 规范让 AI 知道"要什么、不要什么"；
- 变更让每次改动都可审查、可回滚；
- 证据让"完成"变成可验证的事实；
- 门禁让质量在阶段边界被检查，而不是最后一次性爆炸。

对 AI 而言，最重要的一条是：

> **宁可报告"未执行 / 需要决策"，也不要编造"已完成"。**

对使用这套流程的人类而言，最重要的一条是：

> **验收看证据，不看 AI 的自我评价。**
