# 验证报告：0005-loop-point-reconcile

## 环境

| 项 | 值 |
|---|---|
| OS | Windows 11 家庭版 |
| .NET SDK | 10.0.302 |
| 日期 | 2026-11-15 |
| 验证曲目 | `D:\documents\课本笔记2下\6_en\cet6_2024\cet6_2024_06_2.mp3`（实为 M4A） |

## 数据对照（问题根因）

| 来源 | 采样率 | 时长 | 总采样数 |
|---|---|---|---|
| TagLib（扫描入库） | 32000 | 570.18 s | 18,245,760 |
| 实际解码器 | 22050 | 1549.10 s | 34,157,544 |

数据库 `LoopPoints`：`LoopStart=0, LoopEnd=18245760` → 按 22050Hz 换算为 827.5 s ≈ **13:47**，与用户观察的"13:47 跳回开头"一致。

## 执行的命令

```powershell
dotnet build "seamless loop music.sln" -c Debug
dotnet test "SeamlessLoop.Tests/SeamlessLoop.Tests.csproj" -c Debug
dotnet test "SeamlessLoop.Tests/SeamlessLoop.Tests.csproj" -c Debug --filter "FullyQualifiedName~LoopPointReconcilerTests|FullyQualifiedName~AudioStreamFactoryTests|FullyQualifiedName~MusicFolderPathTests"
```

| 项 | 结果 | 日志 |
|---|---|---|
| 构建 | 0 错误 / 23 警告（既有基线） | — |
| 本变更相关单测 | 22/22 通过（校正 6 + 工厂/包装 9 + 文件夹 7） | [verify-0005-20261115-103822.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0005-20261115-103822.log) |
| 全量单测 | 300 通过 / 2 失败（302） | [verify-full-20261115-104518.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-full-20261115-104518.log)（失败为 `VD-0003-02` 既有 fixture 缺失） |
| 人工播放 | 人类确认可正常播放（越过 13:47 继续播放） | 诊断日志显示 `loopEnd=136630176`（= 真实字节长度），无异常 |

## 场景覆盖

| Requirement | Scenario | 验证方法 | 结果 |
|---|---|---|---|
| Loop Point Reconciliation On Load | T01 默认整曲循环对齐真实末尾 | `Reconcile_DefaultWholeFileLoopAlignsToRealEnd` + 人工播放 + 诊断日志 `loopEnd=136630176` | **PASS** |
| Loop Point Reconciliation On Load | T02 用户自定义循环点保持不变 | `Reconcile_PreservesUserDefinedLoop` | **PASS（单测）**，真实用户循环点未做人工构造 |
| Loop Point Reconciliation On Load | T03 越界循环起点归零 | `Reconcile_ResetsOutOfRangeLoopStart` | **PASS（单测）** |
| Loop Point Reconciliation On Load | T04 元数据一致时不改动 | `Reconcile_NoChangeWhenValuesConsistent`、`Reconcile_IgnoresUnknownRealTotal` | **PASS（单测）** |
| — | 附加：终点超出真实总数收敛 | `Reconcile_ClampsLoopEndBeyondRealTotal` | **PASS（单测）** |

## 偏差表

| 编号 | 偏差 | 等级 | 处理 |
|---|---|---|---|
| D1 | 首次实现未考虑"起点超过记录总数"的情形，单测暴露后修正为 `min(记录总数, 真实总数)` 作为上限 | 一般 | 已修复，单测覆盖 |
| D2 | 扫描期仍使用 TagLib，数据库中的总采样数不会因本变更而更新（每次加载时自愈） | 一般 | 记录为设计决策 D4，不阻断 |

## 结论

**Ready for archive**

- 人工确认该曲目可正常播放，不再于 13:47 跳回开头。
- 校正逻辑由 6 个单测覆盖，用户自定义循环点不被改写。
