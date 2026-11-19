# Tasks

> 共 8 项，符合 `AGENTS.md` 第 4.1 节上限。

## 1. 原生层

- [x] 1.1 `loopfinder` 新增 `FeatureExtractor`（`include/loopfinder/feature_extractor.h`、`src/feature_extractor.cpp`）：对单声道 PCM 用 aubio tempo/onset 估算 BPM 与起始密度，用现有 `STFT` 计算 RMS 能量与频谱质心；输出结构体
- [x] 1.2 `loopfinder_api.h/.cpp` 新增 `lf_track_features_t` 与宽字符导出 `lf_classify_file_w`（复用 `AudioDecoder::decode(const wchar_t*)`）；`CMakeLists.txt` 登记新源文件

## 2. 托管层

- [x] 2.1 `Native.cs` 增加 `lf_classify_file_w` 的 `DllImport`（宽字符）、特征结构体与 `ClassifyFile` 包装，DLL 探针同步检查新导出
- [x] 2.2 新增 `Models/TrackFeatures.cs`、`Models/MusicCategory.cs`；新增 `Services/MusicClassification/IMusicClassificationService.cs` 与 `MusicClassificationService.cs`（含四象限规则、自适应阈值+默认兜底、低置信标记）；`App.xaml.cs` 注册服务

## 3. UI

- [x] 3.1 新增 `UI/Views/MusicClassificationView.xaml[.cs]` 与 `UI/ViewModels/MusicClassificationViewModel.cs`：选文件夹、后台批量分析（进度/取消）、结果列表展示（仅展示不落库）；`App.xaml.cs` 注册导航
- [x] 3.2 `LibraryViewModel` 增加"音乐分类"侧边导航项（参照统计入口模式），导航到 `MusicClassificationView`

## 4. 测试与验证

- [x] 4.1 `SeamlessLoop.Tests` 新增分类规则单元测试：四象限边界、阈值退化、低置信标记（纯函数，不依赖原生 DLL）
- [x] 4.2 构建与验证：重编 `loopfinder.dll` 并复制到输出目录；`dotnet build` 零错误；既有 NUnit 全绿；`lf_analyze_file_w` 回归不退化；对 `D:\CloudMusic\恋愛×ロワイアル ORIGINAL SOUNDTRACK` 批量分析，登记 Unicode 路径成功率与结果分布到 `docs/verify/0006-music-classification.md`

## 完成定义（DoD）

- 测试文件夹 30 首全部成功返回特征值与标签，无 decode failed；
- 分类规则单元测试全绿，未放宽既有断言；
- 无法覆盖的用例（如 FLAC/OGG 实测、非 ACG 曲风）显式写 `NOT RUN` 并进 `docs/verification-debt.md`；
- 未改动 `openspec/specs/` 基线（本次为新建 `music-classification` 增量）。

## 规范校验（人工校验，CLI 未安装）

- [x] 每个 Requirement 含 `SHALL` / `MUST` 语句（4 个 Requirement 均含 SHALL/MUST/MUST NOT）
- [x] 每个 Scenario 有唯一 `Txx` 编号且 GIVEN / WHEN / THEN 完整（T01–T07）
- [x] 本次为新建 `music-classification` 增量，使用 `ADDED` 语义，无 `MODIFIED` 需携带完整新版
- [x] `Purpose` 足以描述能力（非占位）
- [x] 关键技术细节复核：aubio `tempo.h` 提供 `aubio_tempo_get_bpm` / `aubio_tempo_get_confidence`，aubio 全量编译进静态库；`AudioDecoder::decode(wchar_t*)`、`BeatDetector`、`STFT` 均可复用，`bpmConfidence` 字段成立
