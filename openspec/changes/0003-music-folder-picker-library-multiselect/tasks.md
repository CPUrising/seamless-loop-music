# Tasks

> 共 8 项，等于 `AGENTS.md` 第 4.1 节上限。

## 1. 规范

- [x] 1.1 建立本变更目录与 `specs/library-folders/spec.md` 增量

## 2. 纯逻辑

- [x] 2.1 新增 `Services/MusicFolderPath.cs`：`Normalize` / `IsSameOrAncestor` / `BuildPlan` + `MusicFolderAddPlan`
- [x] 2.2 新增 `SeamlessLoop.Tests/MusicFolderPathTests.cs`（7 个测试方法，覆盖 T04–T06 与归一化）

## 3. 选择器与持久化

- [x] 3.1 重写 `UI/FolderPicker.cs`：多选、库解析、取消/错误可区分（`FolderPickerResult`）
- [x] 3.2 `Data/DatabaseHelper.cs`：`ApplyMusicFolderChange(toAdd, toRemove)`；`RemoveMusicFolder` 归一化匹配

## 4. 服务与 UI

- [x] 4.1 `Services/PlayerService.cs` + `IPlayerService.cs`：`AddMusicFolders` 与 `MusicFolderAddResult`
- [x] 4.2 设置页与引导页接入新 API；新增 4 条中英文文案

## 5. 验证

- [x] 5.1 `dotnet build` 0 错误；新增单测 7/7；全量 285/287（2 个失败为既有 fixture 缺失，基线复跑确认与本变更无关）
- [x] 5.2 回填 `docs/verify/0003-music-folder-picker-library-multiselect.md`；人工冒烟由人类于 2026-11-14 执行通过，`VD-0003-01` 已关闭

## 完成定义（DoD）

- 库选择、多选、嵌套替换三条主路径有自动化或人工结论（PASS / FAIL / NOT RUN 且已登记）；
- 未放宽既有测试，未改动数据库 schema；
- UI 冒烟需人类执行，未执行前不得标记 PASS。

## 规范校验（人工校验，CLI 未安装）

- [x] 每个 Requirement 含 `SHALL` / `MUST` 语句
- [x] 每个 Scenario 有唯一 `Txx` 编号且 GIVEN / WHEN / THEN 完整
- [x] 本次为新建 `library-folders` 增量，无 `MODIFIED` 需携带完整新版
- [x] `Purpose` 足以描述能力（非占位）
