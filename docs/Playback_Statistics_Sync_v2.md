# 播放统计与 GitHub 同步：Schema v2 维护说明

**状态**：正式协议
**适用范围**：Windows WPF 播放器与 Android 互操作

本文是 schema v2 的长期维护契约。它描述当前实现，不描述开发过程，也不承诺从旧协议迁移。

## 1. 协议边界

- schema v2 是首个发布的播放统计同步协议。输入和输出只接受、只产生 `schemaVersion: 2`，以及 canonical `playbackStatistics`。
- 不支持 legacy migration、legacy fallback 或别名字段兼容。未知版本、仅含旧字段形状或缺少 canonical 必填内容的 v2 数据应拒绝；额外未知字段由 JSON 反序列化器忽略，但不参与协议语义。
- `playbackStatistics.dateBucketBasis` 固定为来源设备本地日期；日期桶使用 `yyyy-MM-dd`。

## 2. 身份与规范化

### 文件名

`normalizedFileName` 的计算顺序固定为：

1. 将反斜杠视为路径分隔符并取 basename。
2. `Trim`。
3. Unicode NFC（`.NET` 的 `NormalizationForm.FormC`）。
4. 不变小写（`ToLowerInvariant`）。

### 曲目 wire identity

曲目的 wire 身份严格为：

```text
(normalizedFileName, exact durationMs)
```

`durationMs` 必须是原始的非负整数；身份比较不使用容差、bucket、hash 或 `sampleRate`。`totalSamples` 与 `contentHash` 仅用于诊断和收敛，不能改变 wire identity。

### 贡献

一个曲目的贡献键为：

```text
(deviceId, generation)
```

同一贡献的合并规则是：dated listen buckets 按日期逐项取最大值，`undatedListenMs` 取最大值，时间字段使用确定性的最早/最晚语义，`updatedAtUtcMs` 取最大值。日期桶属于来源设备本地日期；无法归入日期的时长只能进入 `undatedListenMs`。合并必须是幂等的，不能因重复同步而重复累加。

## 3. ACI 规范化与合并

ACI canonical reducers 对同一曲目身份执行以下规则：

- `FileName`：ordinal max。
- `contentHash`：ordinal max（忽略 null；仅作诊断）。
- `TotalSamples`：nullable max（全为 null 时仍为 null；仅作诊断）。
- 设备、曲目、tombstone、贡献及日期桶按固定 ordinal/key 顺序输出；同键集合必须得到相同 JSON 顺序。
- 同一 `(deviceId, generation)` 的贡献按上述 max/union 规则合并。

规范化器不得引入采样率、hash、采样数或启发式距离作为 wire 身份的一部分。

## 4. 本地 relink

同步行先保持 wire 身份和贡献归属不变，再按以下顺序尝试只绑定 `LocalTrackId`：

1. 同名且精确 `durationMs`。
2. 同名且双方 `totalSamples` 为正，采样数差值不超过 `10000`。
3. 同名且 `durationMs` 差值不超过 `200ms`。
4. 唯一的同名候选。

任一层出现多个候选即停止该层并保留未绑定状态；没有唯一候选时不会猜测。relink 只设置 `LocalTrackId`，不会移动、重写或合并 wire 行及贡献，因此多个 wire rows 可以绑定同一个本地曲目。

## 5. 持久化、结算与删除

- 播放结束或检查点产生 `PlaybackStatisticsSettlement`。settlement 先进入 `PlaybackStatistics.pending.json` 的 v2 outbox；写入使用临时文件、flush、replace/backup 恢复，读取失败的文件会隔离，避免丢失当前 envelope。
- 上传前的当前 snapshot envelope 通过 playback checkpoint 捕获：先结算已确认的播放进度，再导出并 canonicalize。同步应用后还要重新 checkpoint/export，确保回传的是最新本地状态。
- settlement 以 `SettlementEventId` 去重；同一 `(deviceId, generation)` 的重复数据不能重复计时。数据库 schema、outbox 和 snapshot 都只处理 v2 形状。
- 删除源设备数据不是物理忘记：对设备已知的活动 generation 写入 `deviceGeneration` tombstone。tombstone 会抑制旧贡献；设备拥有者观察到自己当前 generation 的 tombstone 后必须清理并旋转，本机清除也会直接旋转，新的播放只能进入新 generation。
- normal sync 与 force-push 都必须保留 tombstone 语义。force-push 只覆盖歌单、循环点和评分等本机域时，播放统计仍与远端合并并重新导出，不能用本机快照抹掉远端统计。

## 6. 设备与用户界面

- Windows 和 Android 设备以稳定 `deviceId` 注册，并带有 `platform`、显示名、首次/最近出现时间和当前 generation。Windows 默认显示名来自机器名；显示名变更按更新时间和确定性名称顺序收敛。
- 设置中的播放统计源设备列表展示来源设备、平台和有效时长，允许重命名并按设备选择删除来源数据。仍有活动 generation 的设备继续单独显示；所有完全 tombstone 化的非本机来源合并为一条不可操作的删除历史汇总，不再暴露旧设备名称、ID 或平台。
- 播放统计视图提供日、周、月、年和全部时段，并从已 relink 的本地曲目汇总排行。未能绑定本地曲目的 wire 行不应被错误计入本地曲目排行。

## 7. 主要实现文件

- `seamless loop music/Data/PlaybackStatisticsSyncSchema.cs`：v2 SQLite 表、约束和索引。
- `seamless loop music/Data/Repositories/PlaybackStatisticsSyncRepository.cs`、`IPlaybackStatisticsSyncRepository.cs`、`PlaybackStatisticsSyncPersistence.cs`：设备、曲目、贡献、日期桶、settlement、tombstone、relink 和来源设备持久化。
- `seamless loop music/Models/PlaybackStatisticsSettlement.cs`、`PlaybackSyncPersistenceModels.cs`：本地结算与持久化模型。
- `seamless loop music/Services/PlaybackStatisticsOutbox.cs`、`PlaybackStatisticsLocalService.cs`、`PlaybackStatisticsOffsetHelper.cs`、`PlaybackStatisticsSettlementFilter.cs`：播放结算、durable outbox、来源设备操作和本地统计读取。
- `seamless loop music/Services/Sync/SyncSnapshotSerializer.cs`、`seamless loop music/Services/Sync/Models/SyncModels.cs`：v2 snapshot 验证、序列化和 wire DTO。
- `seamless loop music/Services/Sync/PlaybackStatisticsSyncCanonicalizer.cs`、`PlaybackStatisticsSyncSnapshotAdapter.cs`：ACI canonical reducers、数据库导出/应用和 relink。
- `seamless loop music/Services/Sync/GitHubSyncPreparationService.cs`、`GitHubSyncCoordinator.cs`、`GitHubSyncService.cs`、`GitHubSyncManagementService.cs`、`Backend/GitHubContentsSyncBackend.cs`：checkpoint、合并、GitHub Contents 读写、force-push 和管理操作。
- `seamless loop music/Services/Sync/PlaybackStatisticsDeviceIdentity.cs`：设备身份与 Windows 显示名。
- `seamless loop music/UI/ViewModels/PlaybackStatisticsViewModel.cs`、`Settings/SettingsDataViewModel.cs`、`Settings/PlaybackStatisticsSourceDeviceRow.cs` 及对应 `Views/PlaybackStatisticsView.xaml`、`Views/Settings/SettingsDataView.xaml`：统计和源设备 UI。

## 8. 测试与 fixture

- `SeamlessLoop.Tests/PlaybackStatisticsSyncV2Tests.cs`：版本边界、文件名规范化、身份和 canonical JSON。
- `SeamlessLoop.Tests/PlaybackStatisticsSyncSnapshotAdapterTests.cs`：导出、应用、max/union、tombstone、平台校验和 relink。
- `SeamlessLoop.Tests/PlaybackStatisticsSyncPersistenceTests.cs`：SQLite schema、贡献持久化、tombstone 和 relink。
- `SeamlessLoop.Tests/PlaybackStatisticsOutboxTests.cs`、`PlaybackServiceCheckpointTests.cs`、`PlaybackStatisticsLocalServiceTests.cs`：outbox、checkpoint、settlement 和本地统计。
- `SeamlessLoop.Tests/PlaybackStatisticsSourceDeviceRowTests.cs`、`PlaybackStatisticsAndroidWpfInteropTests.cs`：设备 UI 行为和 Android/WPF 互操作。
- `SeamlessLoop.Tests/GitHubSyncPreparationServiceTests.cs`、`GitHubSyncCoordinatorTests.cs`、`GitHubSyncBackendTests.cs`、`GitHubSyncManagementTests.cs`、`GitHubSyncServiceTests.cs`：同步准备、冲突、GitHub 后端和管理操作。
- `SeamlessLoop.Tests/Fixtures/Sync/playback_stats_v2_wpf_canonical.json`、`playback_stats_v2_android_wpf_diff.md`：v2 规范样例与跨端差异记录。

## 9. 维护不变量与验证

维护代码时必须保持：

- 只接受和发出 schema v2 与 `playbackStatistics`；不得恢复旧协议迁移或 fallback。
- basename、Trim、NFC、不变小写的规范化顺序不可变。
- wire identity 只由 normalized filename 和精确 durationMs 组成；诊断字段不得升级为身份字段。
- 合并使用 max/union 且幂等；canonical 输出顺序确定。
- relink 只能设置 `LocalTrackId`；tombstone 永久抑制对应 generation；force-push 不得抹除远端播放统计。
- settlement、checkpoint、当前 envelope 和数据库更新在崩溃或重复同步后仍可恢复且不重复计时。

提交前执行：

```powershell
dotnet test "seamless loop music.sln"
dotnet build "seamless loop music.sln" --configuration Release
git diff --check
```
