# Proposal: 以实际解码长度校正循环点，修复播放中途跳回开头

## Why

用户报告 `cet6_2024_06_2.mp3`（实为 M4A）加载后可播放，但**播到 13:47 突然停止并跳回开头**。

实测证据：

| 来源 | 采样率 | 时长 | 总采样数 |
|---|---|---|---|
| TagLib（扫描入库时使用） | 32000 | 570.18 s | 18,245,760 |
| 实际解码器（AudioFileReader） | 22050 | 1549.10 s | 34,157,544 |

数据库中 `LoopPoints` 表该曲目为 `LoopStart=0, LoopEnd=18245760`（默认"整曲循环"），播放时按 22050Hz 换算成 827.5 s，即 **13:47**，于是触发循环跳回开头——与用户观察完全吻合。

根因：扫描期的总采样数与循环点取自 TagLib，而播放期以解码器为准，两者对同一文件给出不同长度；播放器无条件信任数据库中的循环点。

## What Changes

1. 新增 `Services/LoopPointReconciler.cs`：纯函数，按"实际解码采样数"校正 `LoopStart/LoopEnd`：
   - `LoopEnd` 覆盖整首（`<=0` 或 `>= 库中总数`）→ 对齐到真实末尾；
   - `LoopEnd > 真实总数` → 收敛到真实末尾；
   - `LoopStart` 越界 → 归零；
   - 总数不一致时返回"需要写回"。
2. `PlaybackService.LoadTrackAsync` 非 A/B 分支：加载完成后调用该函数，用 `AudioLooper.TotalSamples` 校正内存中的 `MusicTrack`，并把新循环点写回 `LoopPoints`。
3. 新增 `LoopPointReconcilerTests`。

## Capabilities

### New Capabilities

无。属于既有 `playback` 能力的增量需求。

## Impact

- **修改文件**：`Services/PlaybackService.cs`
- **新增文件**：`Services/LoopPointReconciler.cs`、`SeamlessLoop.Tests/LoopPointReconcilerTests.cs`
- **不改变**：扫描流程、TagLib 使用方式、数据库 schema、循环点算法
- **Out of scope（明确不做）**：
  - 不改扫描期元数据来源（改 TagLib → 解码器会显著增加扫描耗时，另行评估）
  - 不修正 `PlaybackSyncSongs` 等同步表中的历史采样数

## 验收口径

- 该曲目可从 0 连续播放到 1549 s，不再在 13:47 跳回开头；
- 用户自定义的循环点（终点小于库中总数）不被改写；
- `dotnet build` 与新增单测通过。
