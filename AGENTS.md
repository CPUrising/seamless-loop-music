# Seamless Loop Music — AI 协作规范（SDD + OpenSpec）

> 版本：2.0
> 生效日期：2026-11-06
> 依据：`AI协作严谨工作流规范-SDD-OpenSpec.md`（v1.0）
> 读者：任何参与本项目的 AI 智能体（TRAE / Claude Code / Cursor / opencode 等）与人类维护者。
> 本文件是**人机契约**，对本仓库的所有 AI 协作具有强制约束力；架构事实见**附录 A**。

**一句话原则：**

> 先把"要什么"写成可验证的合同，再按合同实现，最后用证据证明合同被满足。
> **没有证据的"已完成"一律视为未完成。**

---

## 0. 开工前读取协议（每次任务，不得跳过）

1. 完整读完本文件，重点是第 1、4、5 节。
2. 读**附录 A**，确认改动落在哪一层（Core / Services / Data / UI / Tests）。
3. 读当前任务的变更目录 `openspec/changes/<NNNN-name>/`（proposal / design / tasks / delta spec）。
4. 用一句话向人类复述：**这次改什么、不改什么、涉及哪些文件**，得到确认后再动手。
5. 如果不存在对应 change：先建 change（Plan 阶段），**禁止直接改代码**。
6. 如果 `openspec/` 骨架还不存在：这不是跳过流程的理由，而是**第一个变更（0001）的内容**，详见第 2 节。

**硬性约定（全文生效）：**

- 正文用中文书写；`Requirement` / `Scenario` / `SHALL` / `MUST` / `MUST NOT` / `GIVEN` / `WHEN` / `THEN` 保留英文。
- 不能通过修改断言、跳过用例、放宽条件让测试变绿。
- 任何行为变化必须通过"变更（change）"表达，不得直接改基线规范 `openspec/specs/`。

---

## 1. 角色与红线

### 1.1 角色分工

| 角色 | 可以做 | 不可以做 |
|---|---|---|
| 人类（cpu 大人） | 定义意图、批准变更、审查证据、决定归档 | 把验证全权交给 AI 后不再审查 |
| AI 智能体 | 读上下文、拆任务、写代码与测试、跑验证命令、报告偏差 | 扩大范围、跳过测试、直接改基线规范、伪造验证结果 |
| CI（`.github/workflows/`） | 构建、产出构建产物 | 用"CI 构建成功"替代测试证据（当前 CI 不跑测试，见第 2 节） |

### 1.2 五条红线（违反任一条，任务视为失败）

1. **不得扩大范围**：只做当前 change 授权的事；"顺手优化""顺手清理"必须单独开 change。
2. **不得跳步**：Plan → Check → Build → Verify → Merge 必须完整走完。
3. **不得伪造证据**：没跑过就是没跑过；未执行必须显式写 `NOT RUN` 并说明原因。
4. **不得放宽断言**：测试失败只能修实现，或证明测试本身写错；不得改断言迎合实现。
5. **不得绕过变更改基线**：行为变化写入增量规范，归档时由 `openspec archive` 合并。

### 1.3 本项目补充红线

6. **不得在未构建通过的情况下声称"完成"**：至少执行一次 `dotnet build`。
7. **不得忽略原生依赖**：改动 `loopfinder/` 或涉及循环点分析时，必须确认 `loopfinder.dll` 已生成并被复制到应用输出目录。

---

## 2. 规范资产现状（诚实清单，禁止脑补）

下表是**截至 2026-11-06** 的真实状态。任何 AI 不得假设缺失的资产已存在。

| 资产 | 期望路径 | 当前状态 | 缺失时的处理 |
|---|---|---|---|
| 规范根目录 | `openspec/` | **未建立** | 作为 0001 号变更的 tasks 落地 |
| 项目上下文 | `openspec/project.md` | **未建立** | 0001 |
| AI 契约副本 | `openspec/AGENTS.md` | **未建立**（本文件为根级契约） | 0001 中加入指向本文件的副本或链接 |
| 基线规范 | `openspec/specs/<capability>/spec.md` | **未建立** | 0001 起按能力逐份补 |
| 变更沙箱 | `openspec/changes/` | **未建立** | 0001 |
| 唯一验证入口 | `scripts/verify.ps1` | **未建立**（仓库只有 `.github/workflows/windows-build.yml`，且**只构建不跑测试**） | 先按第 5.3 节手动执行并登记验证债，0001 中建立脚本 |
| 追踪矩阵 | `docs/traceability.md` | **未建立** | 0001 建立种子表 |
| 验证报告目录 | `docs/verify/` | **未建立** | 0001 建立 `TEMPLATE.md` |
| 回归基线 | `docs/regression-log.md` | **未建立** | 首次验证通过后创建 |
| 验证债台账 | `docs/verification-debt.md` | **未建立** | 首次出现 `NOT RUN` 时创建 |
| AI 协作留痕 | `docs/ai-collaboration/` | **未建立** | 0001 起按变更记录 |
| 日志目录 | `artifacts/` | 已存在且已被 `.gitignore` 忽略 | 可直接使用 |

**说明：**

- `docs/` 下已有的 90+ 篇开发文档属于**历史记录**，可用于理解背景，但**不作为验收依据**。
- 测试工程使用 **NUnit**（`SeamlessLoop.Tests.csproj`），不是 MSTest。

---

## 3. 目标目录骨架

```
seamless loop music/
├─ openspec/
│  ├─ config.yaml
│  ├─ project.md
│  ├─ AGENTS.md                 # 指向根级本文件
│  ├─ specs/<capability>/spec.md
│  └─ changes/
│     ├─ NNNN-name/{.openspec.yaml, proposal.md, design.md, tasks.md, specs/}
│     └─ archive/
├─ docs/
│  ├─ traceability.md
│  ├─ regression-log.md
│  ├─ verification-debt.md
│  ├─ verify/{TEMPLATE.md, <change-id>.md}
│  └─ ai-collaboration/<change-id>.md
├─ scripts/verify.ps1
├─ artifacts/                   # 日志，不入库
└─ AGENTS.md                    # 本文件
```

规则：

- `openspec/specs/` 是唯一基线，实施阶段禁止直接编辑。
- `docs/verify/` 的证据必须来自**真实执行的命令输出**。
- `artifacts/` 不入库，但报告中必须写明日志路径。

---

## 4. 五阶段工作循环（本项目命令映射）

一个**可命名的行为增量** = 一个 change。每个 change 必须走完以下五阶段。

| 阶段 | 本项目做法 | 产物 | 禁止 |
|---|---|---|---|
| **Plan** | 创建 `openspec/changes/NNNN-name/`，写 proposal / design / tasks / delta spec | 四类文档 | 改任何源码 |
| **Check** | `openspec validate <id> --strict`；CLI 未安装时按第 7.2 节人工自检并在 tasks 记录"人工校验" | 校验结论 | 跳过校验直接编码 |
| **Build** | 只改 `tasks.md` 列出的文件；每完成一项立即勾选；越界前先更新 tasks 并说明原因 | 源码 + 测试 + 勾选的 tasks | 改动未列入 tasks 的文件 |
| **Verify** | 执行第 5.3 节命令；失败按第 5.5 节处理；最多自愈 3 轮 | 日志 + `docs/verify/<id>.md` | 反复盲改、放宽断言 |
| **Merge** | 人类批准后 `openspec archive <id>`，更新追踪矩阵与回归基线 | 更新后的基线 | AI 自行归档 |

### 4.1 阶段约束

- **Plan**：只允许写规范类文件。
- **Build**：每个任务 ≤ 2 小时可完成、可独立验证；一个 change 超过 **8 个任务必须拆分**。
- **Verify**：先读日志定位，再改代码；连续 3 轮仍失败 → 停止并报告堆栈、已排除的假设、待决策项。
- **Merge**：所有任务完成、所有场景有结论（PASS / FAIL / NOT RUN 且已登记）才可归档。

### 4.2 分支与提交（若使用 Git）

- 分支名与 change 同名：`NNNN-kebab-case`。
- 一个 change 对应一个（或一组）语义化提交；不夹带无关改动。
- 不提交 `bin/`、`obj/`、`artifacts/`、`.venv/`、`_backup/` 中的内容。

---

## 5. 验证与证据

### 5.1 唯一验证入口

目标态：`scripts/verify.ps1`，支持 `-Scope unit|integration|all` 与 `-Change <id>`，负责构建、跑测试、把日志写入 `artifacts/`。

**当前该脚本不存在**：在它建立之前，按 5.3 节手动执行，并在 `docs/verification-debt.md` 登记"未通过唯一入口执行"。

### 5.2 分层

| 层 | 内容 | 是否必须 |
|---|---|---|
| 静态 | `dotnet build` 零错误；无新增编译警告（有则记录） | 必须 |
| 单元/集成 | `SeamlessLoop.Tests`（NUnit）全绿 | 必须 |
| 原生 | `loopfinder` 构建成功 + 产物存在 | 仅涉及分析链路时 |
| 手工 | WPF 启动冒烟、播放/循环/同步等 UI 路径 | 涉及 UI 时必须，且需记录步骤 |

### 5.3 命令（在仓库根目录执行）

```powershell
# 1. 还原
dotnet restore "seamless loop music.sln"

# 2. 构建（WPF 应用 + 测试工程）
dotnet build "seamless loop music.sln" -c Debug

# 3. 测试（NUnit）
dotnet test "SeamlessLoop.Tests/SeamlessLoop.Tests.csproj" -c Debug

# 4. 原生分析库（仅当改动 loopfinder/ 时）
cmake -S loopfinder -B loopfinder/build -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build loopfinder/build --config Release --target loopfinder --parallel
```

产物检查：

- 应用输出：`seamless loop music/bin/Debug/net48/seamless loop music.exe`
- 原生库：`seamless loop music/bin/Debug/net48/loopfinder.dll`（缺失时播放器的自动寻环不可用）

> 上述命令以仓库现有工程结构与 CI 配置为依据；**首次执行结果必须如实记录**，不得假设通过。

### 5.4 证据铁律

1. **未执行 ≠ 通过**：`NOT RUN` 必须写明原因、是否阻断、何时补测。
2. **证据由执行方产出**：报告里贴的是命令与真实输出摘要，不是结论性形容词。
3. **可追溯**：每条 PASS 都能定位到测试方法名或日志行。

每个 change 必须有 `docs/verify/<change-id>.md`，至少包含：

- 环境（OS、`dotnet --version`、Date）
- 执行的命令与日志路径（`artifacts/verify-<id>-<timestamp>.log`）
- 场景覆盖表：`Requirement | Scenario | 测试方法 | PASS/FAIL/NOT RUN`
- 偏差表：`编号 | 偏差 | 等级（阻断/严重/一般） | 处理`
- 结论：`Ready for archive` / `需修复后复验` / `需人类决策`

### 5.5 失败处理决策树

```
测试失败
├─ 测试自身写错（断言/夹具/选择器）？
│  ├─ 是 → 修测试，重跑，并在报告登记"测试缺陷"
│  └─ 否 → 环境故障（SDK/依赖/loopfinder.dll 缺失）？
│         ├─ 是 → 重建环境，重跑，记入偏差
│         └─ 否 → 实现错误 → 修实现，重跑
└─ 连续 3 轮仍失败 → 停止，写报告，等人类决策
```

**禁止**：删除用例、放宽断言、跳过测试项目、改测试数据来"制造通过"。

---

## 6. 变更管理

- 命名：`NNNN-kebab-case`，编号单调递增、不复用，例如 `0001-openspec-bootstrap`。
- 粒度：一个 change = 一个行为增量；纯重构 / 纯格式化 / 纯文档各自单独成 change。
- 不改变行为合同的变更（例如内部重构），在 `.openspec.yaml` 写 `skip_specs: true`；**禁止为了通过校验虚构需求**。
- 文件边界：

| 文件 | 必须回答 | 不该包含 |
|---|---|---|
| `proposal.md` | Why / What Changes / Impact / 明确不做的事 | 实现细节、代码 |
| `design.md` | 关键决策与理由、备选方案、权衡、风险、迁移 | 需求复述 |
| `tasks.md` | 原子任务（≤ 2 小时/项）、按依赖排序、可勾选 | "优化一下"之类模糊项 |
| `specs/<cap>/spec.md` | ADDED / MODIFIED / REMOVED / RENAMED 增量 | 整份基线复制 |

---

## 7. 规范写法

### 7.1 结构与关键词

```markdown
# <能力名> Capability Spec

## Purpose

用 1–3 句话说明这个能力做什么、为什么存在（建议 ≥ 50 字符）。

## Requirements

### Requirement: <英文短语，如 Seamless Loop Playback>

系统 SHALL ...

#### Scenario: T01 <中文描述>

- **GIVEN** 前置条件
- **WHEN** 触发动作
- **THEN** 系统 MUST ...
- **AND** MUST NOT ...
```

- 需求名用英文短语，正文用中文；`SHALL` / `MUST` / `MUST NOT` 保留英文。
- 场景名带稳定编号 `Txx`，与追踪矩阵、测试方法一一对应。
- 一个 Scenario 只描述一条可验证路径。

### 7.2 人工校验清单（`openspec` CLI 不可用时）

- [ ] 每个 Requirement 有 `SHALL` 或 `MUST` 语句
- [ ] 每个 Scenario 有唯一 `Txx` 编号，且 GIVEN / WHEN / THEN 完整
- [ ] `MODIFIED` 携带完整新版（正文 + 仍存活的所有场景）
- [ ] 基线文件中不残留 `## ADDED/MODIFIED/REMOVED/RENAMED` 标题
- [ ] `Purpose` 足够描述能力，不是一句话占位
- 校验结论写入 `tasks.md` 的验证项，注明"人工校验（CLI 未安装）"

---

## 8. 质量门禁（Gate）

| Gate | 准入条件 | 通过标准 |
|---|---|---|
| G0 可复现构建 | 骨架落地 | `dotnet build` 成功；`loopfinder.dll` 存在；应用能启动冒烟 |
| G1 规范冻结 | 基线规范与追踪矩阵可用 | 严格校验全绿（或人工校验通过）；验收场景均有规范与测试映射 |
| G2 播放核心 | 播放/暂停/切歌/进度可用 | 单元测试通过；UI 手工清单通过 |
| G3 无缝循环 | 循环点设置与跳转可用 | 循环边界场景通过；无爆音/断音记录 |
| G4 循环分析 | `loopfinder` 链路可用 | 候选点生成与应用的场景通过 |
| G5 媒体库与扫描 | 扫描/分类/封面可用 | 对应测试通过；缺失文件清理场景通过 |
| G6 同步 v2 | 跨端快照互通 | 同步相关测试全绿；跨端 fixture 一致 |
| G7 发布 | 全量回归 | 全量测试 + 手工清单通过；全部 change 归档；规范与实现一致 |

**规则：**

- 每个 Gate 完成时更新 README 阶段看板并链接证据。
- 当前所有 Gate 状态一律为 **未验收**，除非有 `docs/verify/` 证据支撑。禁止凭主观判断填"已完成"。

---

## 9. 反模式清单（看到就要停）

| 反模式 | 正确做法 |
|---|---|
| "我顺手把这个也改了" | 单独开 change |
| "测试应该能过" | 真跑，贴日志 |
| 改断言让测试变绿 | 修实现，或证明测试写错 |
| 直接编辑 `openspec/specs/` | 写增量规范，归档时合并 |
| 一个 change 塞 20 个任务 | 拆分 |
| 把 `NOT RUN` 写成 `PASS` | 如实登记，进验证债 |
| 在 `IPlayerService` 上继续加播放逻辑 | 播放控制归 `IPlaybackService`（见第 10 节） |
| 绕过 Repository 直接改 `DatabaseHelper` | 走对应 Repository；确需直连时在 design.md 说明理由 |
| 在 ViewModel 里做业务计算/直接访问数据库 | 下沉到 Services / Repository |
| 从 `_backup/` 复制旧实现当参考 | `_backup/` 是历史残留，不作为依据 |

---

## 10. 已知维护性风险（改动前必读）

以下为 2026-11-06 的**只读调查结论，均未修复**。它们不代表"应该马上改"，而是**改动时必须知道的坑**；修复必须单独开 change。

1. `IPlaylistManagerService` / `PlaylistManagerService`：整类无调用方（仅被注入到 `PlayerService` 后从未使用）。
2. `PlaybackQueueService`：未注册、全项目零引用，是队列的第三份历史实现。
3. `IPlayerService` 中的歌单 CRUD（11 个方法）零调用；其中 `AddFilesToPlaylist` 是**空实现**，调用会静默无效。
4. `PlayerService.MatchWindowSize` / `MatchSearchRadius` 是自动属性，**不会**写回播放引擎；真正生效的是 `IPlaybackService` 上的同名属性。
5. `PlaylistManager` 的 `SetNowPlayingList` / `GetNextTrack` / `GetPreviousTrack` 与 `IQueueManager` 语义重复且无人调用。
6. `LoopWorkspaceViewModel` 同时注入 `IPlayerService` 与 `IPlaybackService`，两者对同一状态存在多种写法，易产生"改了没反应"。

**已知职责边界（请遵守）：**

- 播放/暂停/切歌/进度/队列/循环点 → `IPlaybackService`
- 扫描目录/媒体库元数据/数据库同步/循环分析 → `IPlayerService`
- 歌单 CRUD → `IPlaylistManager`
- 队列容器 → `IQueueManager`（仅由 `PlaybackService` 使用）

---

## 附录 A：项目代码架构概览

---
**编写者**: 莱芙・泽诺 (Lev Zenith)
**日期**: 2026-07-12
**状态**: 采用 Repository 仓储模式 + Prism MVVM，已完成正式 schema v2 GitHub 同步、播放统计和 Android 互操作。同步实现包含 playback statistics repository、snapshot adapter/canonicalizer、outbox 与 checkpoint；设置界面支持设备、generation 和 tombstone 管理，播放统计界面支持本地汇总。旧的播放统计 repository/model 已移除，当前实现以 `PlaybackStatisticsSync*` 持久化模型和 `PlaybackStatisticsSettlement` 为准。

---

### A.1 核心定位

本项目是一个基于 **.NET Framework 4.8 (WPF)** 开发的高保真音乐播放器，核心特色是支持**无缝循环 (Seamless Looping)**，并集成了 **PyMusicLooper** 库进行自动化音频循环点分析。

**技术亮点**：
- **Prism.Unity 8.1.97** — MVVM 框架，区域导航与依赖注入
- **MaterialDesignThemes 5.3.1** — Material Design UI 风格
- **Repository 仓储模式** — 清晰的数据访问层分离
- **Prism EventAggregator** — 层间松耦合事件通信

### A.2 模块化设计

#### Core (核心引擎层) — `seamless loop music/Core/`

项目的"心脏"，负责最底层的声音处理。
- `AudioLooper.cs`: 核心控制中心，处理播放状态（Play/Pause/Stop）以及精细的采样跳转逻辑。已拆分为 `Analysis`（分析）、`Loader`（加载）和 `Mixing`（混音）等分部类。
- `AudioLooper.Analysis.cs` / `AudioLooper.Loader.cs` / `AudioLooper.Mixing.cs`: 对应分部类。
- `LoopStream.cs`: 自定义音频流包装类，确保到达循环终点时毫秒级无感跳转。
- `ConcatenatedStream.cs`: 逻辑拼接流，按顺序连接两个 WaveStream，实现零内存损耗的无缝播放。

#### Events (事件通信层) — `seamless loop music/Events/` — Prism EventAggregator

- `CoreEvents.cs`: 全局事件（TrackLoadedEvent、PlaybackStateChangedEvent、LoopPointsChangedEvent、PlaylistChangedEvent、TrackMetadataChangedEvent、LibraryRefreshedEvent）。
- `CategoryItemSelectedEvent.cs`: 分类项选中事件，触发曲目列表按 Album / Artist / Playlist 过滤。

#### Models (数据模型层) — `seamless loop music/Models/`

- `MusicTrack.cs`: 代表一首歌曲，包含采样率、开始/结束循环点、收藏状态、评分等。
- `Playlist.cs`: 播放列表（由 `PlaylistFolder.cs` 重命名而来）。
- `LoopCandidate.cs`: 潜在循环点数据。
- `PlayMode.cs`: 播放模式枚举（单曲循环、列表循环、随机播放等）。
- `CategoryItem.cs`: 分类项模型，支持 Album / Artist / Playlist，含封面图片路径。
- `CategoryNavTarget.cs`: 侧边栏导航目标。
- `Album.cs` / `Artist.cs`: 专辑与艺术家模型，含封面路径。
- `UserRating.cs`: 评分与收藏状态。
- `SubfolderItem.cs`: 文件夹浏览子项（名称、路径、是否根目录）。

#### Data (持久化层) - Repository 仓储模式 — `seamless loop music/Data/`

- `DatabaseHelper.cs`: 封装 SQLite 操作，包括 `MusicTracks` 表与 `Playlists` 表。
- `Repositories/BaseRepository.cs`: 仓储基类，通用 CRUD。
- `Repositories/ITrackRepository.cs` / `TrackRepository.cs`: 曲目仓储。
- `Repositories/IPlaylistRepository.cs` / `PlaylistRepository.cs`: 播放列表仓储。
- `PlaybackStatisticsSyncSchema.cs`: 播放统计 v2 的设备、曲目、贡献、日期桶、settlement 和 tombstone 表结构。
- `Repositories/IPlaybackStatisticsSyncRepository.cs` / `PlaybackStatisticsSyncRepository.cs`: 播放统计同步仓储。
- `Repositories/PlaybackStatisticsSyncPersistence.cs`: settlement 消费、贡献合并、generation tombstone 与本地 relink。

#### Services (业务逻辑层) — `seamless loop music/Services/`

- `PlayerService.cs` / `IPlayerService.cs`: 顶层业务管理器（媒体库扫描、目录、数据库同步、循环分析），含部分播放转发。
- `PlaybackService.cs` / `IPlaybackService.cs`: 播放控制主门面（播放、队列、循环点、统计生命周期）。
- `PlaylistManager.cs` / `IPlaylistManager.cs`: 歌单 CRUD（UI 实际使用路径）。
- `PlaylistManagerService.cs` / `IPlaylistManagerService.cs`: 历史歌单服务（当前无调用方，见第 10 节）。
- `QueueManager.cs` / `IQueueManager.cs`: 播放队列管理器（队列的唯一真实持有者）。
- `PlaybackQueueService.cs`: 历史队列服务（当前零引用）。
- `PyMusicLooperWrapper.cs`: 与 Python 库通信，调用 `pymusiclooper` 计算循环点。
- `LoopAnalysisService.cs` / `ILoopAnalysisService.cs`: 后台扫描与候选点获取。
- `LocalizationService.cs`: 中英文动态切换。
- `TrackMetadataService.cs`: 音频元数据提取。
- `SearchService.cs` / `ISearchService.cs`: 0.4 秒防抖多关键词搜索。
- `AppStateService.cs`: 应用状态持久化（音量、播放模式、分类上下文、最后播放曲目）。
- `TaskbarService.cs` / `ITaskbarService.cs`: 任务栏进度与缩略图按钮。
- `NotifyIconService.cs` / `INotifyIconService.cs`: 托盘图标与右键菜单。
- `FoldersService.cs` / `IFoldersService.cs`: 文件夹浏览与面包屑导航。
- `PlaybackStatisticsOutbox.cs`: 播放结算 outbox，负责 v2 envelope 持久化恢复。
- `PlaybackStatisticsLocalService.cs` / `IPlaybackStatisticsLocalService.cs`: 本地统计查询、结算与来源设备操作。
- `Sync/SyncSnapshotSerializer.cs`: v2 snapshot 校验与规范 JSON 序列化。
- `Sync/PlaybackStatisticsSyncCanonicalizer.cs`: canonical reducer，max/union 合并与确定性排序。
- `Sync/PlaybackStatisticsSyncSnapshotAdapter.cs`: snapshot 与 SQLite 的导出、应用和 relink。
- `Sync/GitHubSyncPreparationService.cs`: checkpoint、应用远端统计、generation 旋转与最终 snapshot 准备。
- `Sync/GitHubSyncCoordinator.cs` / `Sync/GitHubSyncManagementService.cs`: 同步协调、force-push、云端删除与本地同步数据管理。
- `Sync/PlaybackStatisticsDeviceIdentity.cs`: Windows/Android 设备身份与显示名处理。

#### UI (展示层) - Prism MVVM + Material Design — `seamless loop music/UI/`

**窗口层**：
- `MainWindow.xaml`: 主控制中心。
- `LoopListWindow.xaml`: 循环点方案选择。
- `FolderPicker.cs`: 文件夹选择辅助工具。
- `Views/TrayControlsWindow.xaml[.cs]`: 托盘控制窗口。

**视图层 (Views)**：
- `Views/LoopWorkspace.xaml[.cs]`: 循环点编辑工作区。
- `Views/PlaybackControlBar.xaml[.cs]`: 播放控制条。
- `Views/LibraryView.xaml[.cs]`: 音乐库视图（Prism 区域容器）。
- `Views/DetailView.xaml[.cs]`: 曲目详情视图。
- `Views/TrackListView.xaml[.cs]`: 曲目列表（分类过滤与搜索）。
- `Views/NowPlayingView.xaml[.cs]`: 当前播放视图（封面、循环波形）。
- `Views/InputDialog.xaml[.cs]` / `Views/AddToPlaylistDialog.xaml[.cs]`: 输入与添加歌单对话框。
- `Views/SettingsView.xaml[.cs]` 与 `Views/Settings/*`: 设置窗口。
- `Views/PlaybackStatisticsView.xaml[.cs]`: 播放统计视图。

**视图模型层 (ViewModels)** — Prism `INavigationAware` 支持：
- `LoopWorkspaceViewModel.cs`、`PlaybackControlBarViewModel.cs`、`LibraryViewModel.cs`、`DetailViewModel.cs`、`TrackListViewModel.cs`、`NowPlayingViewModel.cs`、`TrayControlsViewModel.cs`、`PlaybackStatisticsViewModel.cs`。
- `Settings/SettingsDataViewModel.cs`（GitHub 配置、同步管理、来源设备）、`Settings/PlaybackStatisticsSourceDeviceRow.cs`。

**控件与工具类**：
- `Controls/MultiSelectListBox.cs`、`PlaybackControls`、`VolumeControls`、`TrackInfoControl`、`ProgressControls`。
- `Converters/VisibilityConverters.cs`、`BindingProxy.cs`、`PlayPauseIconConverter.cs`、`ProgressMarkerPositionConverter.cs`。

#### App 入口 — `seamless loop music/seamless loop music/`

- `App.xaml`: 全局样式（Material Design 暗色皮肤）。
- `App.xaml.cs`: 应用入口，单实例互斥锁、全局异常捕获、Prism UnityContainer 依赖注入配置。

#### 测试项目 (SeamlessLoop.Tests) — `SeamlessLoop.Tests/`

- `SeamlessLoop.Tests.csproj`: NUnit 测试工程（net48，引用主工程）。
- `TestDatabaseSeed.cs`: 测试数据库种子数据（黄金数据集 / 大规模数据）。
- `SyncTests.cs`、`DatabaseTests.cs`、`ArtistCoverTests.cs`: 数据库同步、3NF 架构与封面。
- `PlaybackStatisticsSyncV2Tests.cs`、`PlaybackStatisticsSyncSnapshotAdapterTests.cs`、`PlaybackStatisticsSyncPersistenceTests.cs`: schema v2、snapshot、max/union、tombstone、relink。
- `PlaybackStatisticsOutboxTests.cs`、`PlaybackServiceCheckpointTests.cs`、`PlaybackStatisticsLocalServiceTests.cs`: outbox、checkpoint、settlement。
- `PlaybackStatisticsSourceDeviceRowTests.cs`、`PlaybackStatisticsAndroidWpfInteropTests.cs`: 来源设备 UI 与跨端互操作。
- `GitHubSyncPreparationServiceTests.cs`、`GitHubSyncCoordinatorTests.cs`、`GitHubSyncBackendTests.cs`、`GitHubSyncManagementTests.cs`: GitHub v2 同步。
- `PlaybackServiceDependencyInjectionTests.cs`、`PlaybackServiceTestDouble.cs`: 依赖注入与测试替身。
- `Fixtures/Sync/`: v2 跨端规范 fixture。

### A.3 Prism 框架集成

**依赖注入**：所有服务接口通过 Prism `UnityContainer` 注册（见 `App.xaml.cs` 的 `RegisterTypes`），ViewModel 通过构造函数注入。

**区域导航**：`LibraryView` 作为区域容器，`TrackListView` / `DetailView` 为区域视图，`TrackListViewModel` 实现 `INavigationAware`。

**事件聚合器**：`CategoryItemSelectedEvent`、`TrackMetadataChangedEvent`、`TrackLoadedEvent`、`PlaybackStateChangedEvent`、`LoopPointsChangedEvent`、`PlaylistChangedEvent`、`LibraryRefreshedEvent`。

### A.4 项目资产与资源

- `Statics/Resources.resx` / `Resources.zh-CN.resx`: 中英文多语言文本（配套 `Resources.Designer.cs`）。
- `.venv`: 独立 Python 环境（用于 PyMusicLooper）。
- `docs/`: 开发历史文档与设计说明（非验收依据）。
- `UI/Themes/`: `Icons.xaml`、`Colors.xaml`、`Styles.xaml`、`Controls.xaml`。
- `loopfinder/`: 自研循环点分析原生库（CMake，产物 `loopfinder.dll`）。
- `installer/`: Inno Setup 打包脚本。

---

## 附录 B：命令速查

```powershell
# 构建
dotnet restore "seamless loop music.sln"
dotnet build "seamless loop music.sln" -c Debug

# 测试（NUnit）
dotnet test "SeamlessLoop.Tests/SeamlessLoop.Tests.csproj" -c Debug

# 原生分析库
cmake -S loopfinder -B loopfinder/build -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build loopfinder/build --config Release --target loopfinder --parallel

# 规范校验（CLI 可用时）
openspec validate <change-id> --strict
openspec show <change-id>
openspec archive <change-id>
```

---

## 结语

对 AI 而言，最重要的一条：

> **宁可报告"未执行 / 需要决策"，也不要编造"已完成"。**

对人类而言，最重要的一条：

> **验收看证据，不看 AI 的自我评价。**
