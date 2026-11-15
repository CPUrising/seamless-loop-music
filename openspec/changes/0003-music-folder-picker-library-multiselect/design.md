# Design: 音乐文件夹「库」解析、多选与嵌套去重

## 决策

### D1：移除 `FOS_FORCEFILESYSTEM`，改用运行时解析

- 现状：`FOS_FORCEFILESYSTEM` 让对话框只允许选中文件系统项，库节点被禁用"确定"。
- 选择：选项改为 `FOS_PICKFOLDERS | FOS_ALLOWMULTISELECT`；对拿不到 `SIGDN_FILESYSPATH` 的项尝试按「库」解析。
- 否决：保留 `FOS_FORCEFILESYSTEM` 然后提示用户"请进入库里选子文件夹"——用户明确要求能直接加入整库。

### D2：库 → `IShellLibrary.LoadLibraryFromItem` + `GetFolders(LFF_FORCEFILESYSTEM)`

- 用 `new ShellLibrary()`（CLSID `D9B3211D-E57F-4426-AAEF-30A806ADD397`）加载选中项，取 `IShellItemArray` 后逐个 `SIGDN_FILESYSPATH`。
- VTable 顺序按本机 SDK `ShObjIdl_core.h:28561-28682` 核对，`GetFolders` 位于第 5 个自定义槽位（`LoadLibraryFromItem, LoadLibraryFromKnownFolder, AddFolder, RemoveFolder, GetFolders, ...`）。
- 失败降级：进 `Unresolved`，由 UI 提示，不抛异常、不写库。

### D3：嵌套规则采用"父目录替换子目录"

- 候选被现有目录包含（含相等，`OrdinalIgnoreCase` + 目录边界）→ 跳过；
- 候选是现有目录的上级 → 删除旧子目录、插入候选；
- 理由：扫描使用 `Directory.GetFiles(AllDirectories)`，同时保留父子会产生重复遍历；`CleanupTracksOutsideMusicFolders` 本身也按"属于任一文件夹"语义判断。

### D4：归一化只在服务层做，不改 schema

- `MusicFolderPath.Normalize`：`Path.GetFullPath` + 统一分隔符 + 去尾部 `\`（盘符根保留）；非法路径返回 `null`。
- 历史库里已存在的大小写/末尾斜杠重复行不主动清理（避免扩大范围），但比较时按归一化结果进行。

### D5：UI 反馈最小化

- 成功 → 只刷新列表（沿用现状），不弹窗；
- 全部被覆盖 → 提示"未新增文件夹"；
- 有无法解析项 → 提示"无法使用该位置"；
- 选择器异常 → 提示具体错误，不再静默。

## 风险

| 风险 | 缓解 |
|---|---|
| COM VTable 声明错误 | 已按 SDK 头文件核对；解析失败走 `Unresolved` |
| 放开虚拟项后可选"此电脑/网络" | 统一归入 `Unresolved` 并提示 |
| 添加盘符根目录后扫描越权 | 已知风险，本变更不处理，报告登记 |
| `IShellLibrary` 在部分系统不可用 | 解析失败降级提示，不影响普通文件夹选择 |
