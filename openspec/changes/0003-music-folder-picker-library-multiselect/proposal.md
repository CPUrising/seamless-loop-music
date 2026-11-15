# Proposal: 音乐文件夹支持「库」选择、多选与嵌套去重

## Why

issue #30 记录：添加音乐文件夹时，选中 Windows「库」（如 此电脑 → 音乐）无法确定；同时不支持多选，多次添加产生的父子目录也没有任何去重规则。

根因（已核实代码）：

1. `UI/FolderPicker.cs` 使用 `FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM`，库是 Shell 虚拟项，选择后系统禁用"确定"；即使取到结果，`GetDisplayName(SIGDN_FILESYSPATH)` 对库也会失败，而异常被 `catch (Exception)` 静默吞掉，表现与"用户取消"相同。
2. `FolderPicker` 只取 `GetResult()` 单值，没有 `FOS_ALLOWMULTISELECT`。
3. `PlayerService.AddMusicFolder` 不做归一化、不校验存在性、不处理父子嵌套；`DatabaseHelper` 依赖字符串 `UNIQUE`，`C:\Music` 与 `C:\Music\` 会被当成两个文件夹。
4. 添加失败/被忽略时没有任何提示（设置页无 try/catch、无反馈）。

## What Changes

1. `FolderPicker` 放开虚拟项选择（移除 `FOS_FORCEFILESYSTEM`），支持 `FOS_ALLOWMULTISELECT`；对库通过 `IShellLibrary` 展开为库内真实文件夹；解析失败进 `Unresolved` 并返回给调用方提示。
2. 新增 `Services/MusicFolderPath.cs`：路径归一化、父子判断、添加计划（`ToAdd` / `ToRemove` / `SkippedCovered` / `Rejected`）。
3. `DatabaseHelper` 新增 `ApplyMusicFolderChange(toAdd, toRemove)` 单事务；`RemoveMusicFolder` 改为归一化匹配。
4. `IPlayerService.AddMusicFolder(string)` 替换为 `AddMusicFolders(IEnumerable<string>) : MusicFolderAddResult`。
5. 设置页与首次引导页接入新 API；新增 4 条中英文提示文案。
6. 新增 `MusicFolderPathTests`（6 个纯逻辑用例）。

## Capabilities

### New Capabilities

`library-folders`：音乐文件夹的添加、去重与嵌套规则。

### Modified Capabilities

无。

## Impact

- **修改文件**：`UI/FolderPicker.cs`、`Data/DatabaseHelper.cs`、`Services/PlayerService.cs`、`Services/IPlayerService.cs`、`UI/ViewModels/Settings/SettingsMusicViewModel.cs`、`UI/Views/OnboardingWindow.xaml.cs`、`Statics/Resources.resx`、`Statics/Resources.zh-CN.resx`
- **新增文件**：`Services/MusicFolderPath.cs`、`SeamlessLoop.Tests/MusicFolderPathTests.cs`
- **不改变**：数据库 schema、扫描算法、播放链路、`loopfinder`
- **Out of scope（明确不做）**：
  - 扫描完成提示的亮色主题问题（issue #30 其它子项）
  - 30 秒快进/后退按钮、歌单批量操作
  - 扫描的权限异常隔离、根目录添加防护
  - 设置页添加后自动扫描（保持现状）

## 验收口径

- 选中「音乐」库并确定后，设置页列表出现库内全部真实文件夹。
- Ctrl 多选多个文件夹可一次添加。
- 添加父目录后，原有冗余子目录被替换；重复添加给出"未新增"提示。
- 选中"此电脑"等无法解析的位置时给出提示且不写库。
- `dotnet build` 与 `dotnet test` 全绿。
