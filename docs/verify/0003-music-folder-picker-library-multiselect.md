# 验证报告：0003-music-folder-picker-library-multiselect

## 环境

| 项 | 值 |
|---|---|
| OS | Microsoft Windows 11 家庭版 |
| 系统区域设置 | `en-US`（ACP=1252） |
| .NET SDK | 10.0.302 |
| 日期 | 2026-11-14 |
| 本机音乐库成员 | `D:\music`、`D:\documents\生活\music`、`D:\CloudMusic` |

## 执行的命令与日志

```powershell
dotnet build "seamless loop music.sln" -c Debug
dotnet test "SeamlessLoop.Tests/SeamlessLoop.Tests.csproj" -c Debug
```

| 项 | 结果 | 日志 |
|---|---|---|
| 构建 | 0 错误 / 23 警告（全部为既有可空性与未使用事件警告，无新增） | — |
| 新增单测 | 7/7 通过 | 控制台输出 |
| 全量单测 | 285 通过 / 2 失败（287） | [verify-0003-20261114-225202.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0003-20261114-225202.log) |
| 库解析探针 | 音乐库解析出 3 个成员目录 | [verify-0003-library-20261114-225443.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0003-library-20261114-225443.log) |

## 场景覆盖

| Requirement | Scenario | 验证方法 | 结果 |
|---|---|---|---|
| Library Selection | T01 选中音乐库并添加 | ①等价 COM 声明展开库（自动化探针）②人类在设置页选中「音乐」库并确定 | **PASS**：探针解析出 3 个成员目录；人工冒烟确认全部加入列表 |
| Library Selection | T02 无法解析的虚拟位置 | 人类选中「此电脑」类虚拟项 | **PASS（人工）**：提示"无法使用该位置"，`MusicFolders` 无新增 |
| Multi Folder Selection | T03 多选不同目录 | 人类 Ctrl 多选两个目录 | **PASS（人工）**：一次添加成功 |
| Path Normalization And Nesting | T04 子目录已被父目录覆盖 | `MusicFolderPathTests.BuildPlan_SkipsFolderCoveredByExisting` | **PASS** |
| Path Normalization And Nesting | T05 新增父目录替换子目录 | `MusicFolderPathTests.BuildPlan_ParentReplacesChild` | **PASS** |
| Path Normalization And Nesting | T06 不存在或非法路径 | `MusicFolderPathTests.BuildPlan_RejectsMissingFolder` | **PASS** |
| — | 附加：归一化与父子判定 | `Normalize_TrimsTrailingSeparator`、`Normalize_KeepsDriveRoot`、`IsSameOrAncestor_IsCaseInsensitive`、`IsSameOrAncestor_RejectsSiblingPrefix` | **PASS** |

## 偏差表

| 编号 | 偏差 | 等级 | 处理 |
|---|---|---|---|
| D1 | 选择器交互（库节点可确定、Ctrl 多选、设置页/引导页提示） | — | **已由人类完成（2026-11-14，人工见证，无截图）**，见 `VD-0003-01` |
| D2 | 全量单测 2 个失败：`PlaybackStatisticsAndroidWpfInteropTests` 找不到 `Fixtures/Sync/*.json`（fixture 未复制到输出目录） | 一般 | 已复跑基线（stash 本次改动后单独运行该 fixture）：同样 2 个失败 → **既有问题，与本变更无关**；登记 `VD-0003-02`（不阻断） |
| D3 | 本机音乐库成员分布（`D:\music` 等）与用户实际库一致，但未在另一台机器验证 | 一般 | 实现走 Shell 官方接口，不做机器特化；随人工冒烟一并确认 |

## 结论

**Ready for archive**

- 逻辑层（归一化、嵌套替换、缺失拒绝）由 7 个单测覆盖并通过。
- 库解析核心 COM 调用已用等价探针实测（3 个成员目录）。
- 选择器交互（库选择、多选、提示文案）已由人类手工冒烟确认通过（2026-11-14，人工见证）。
- 唯一遗留为非阻断项 `VD-0003-02`（既有 fixture 缺失导致的 2 个单测失败，与本变更无关）。
