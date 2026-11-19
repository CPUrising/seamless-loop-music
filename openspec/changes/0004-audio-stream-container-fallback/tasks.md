# Tasks

> 共 6 项，低于 `AGENTS.md` 第 4.1 节上限。

## 1. 规范

- [x] 1.1 建立本变更目录与 `specs/playback/spec.md` 增量

## 2. 实现

- [x] 2.1 新增 `Core/AudioStreamFactory.cs`：扩展名优先、容器嗅探回退、`TryCreate` / `DetectContainerKind`
- [x] 2.2 `Core/AudioLooper.Loader.cs` 的 `CreateAudioStream` 改用工厂
- [x] 2.3 `Services/PlayerService.cs` 的 `CreateNAudioStream` 改用工厂
- [x] 2.4 `Services/TrackMetadataService.cs` 的 `GetSampleRate` 改用工厂

## 3. 验证

- [x] 3.1 新增 `AudioStreamFactoryTests`（7 个用例，全部通过）
- [x] 3.2 `dotnet build` 0 错误；全量单测 292/294（2 个失败为既有 fixture 缺失）；端到端探针：问题文件 → `AudioFileReader` 1549.1s，正常 MP3 仍为 `Mp3FileReader` 且长度不变，OGG 仍为 `VorbisWaveReader`。回填 `docs/verify/0004-audio-stream-container-fallback.md`

## 完成定义（DoD）

- 问题文件可加载（`TryCreate` 返回非 null）——已由端到端探针证明；
- 正常 MP3 仍走 `Mp3FileReader`（单测 + 探针断言类型与字节长度）；
- 无法覆盖的用例（真实 `.m4a/.wma/.aiff/.flac` 样本、WPF 播放冒烟）显式 `NOT RUN` 并登记验证债（`VD-0004-02`、`VD-0004-03`）。

## 规范校验（人工校验，CLI 未安装）

- [x] 每个 Requirement 含 `SHALL` / `MUST` 语句
- [x] 每个 Scenario 有唯一 `Txx` 编号且 GIVEN / WHEN / THEN 完整
- [x] 本次为新建 `playback` 增量，无 `MODIFIED` 需携带完整新版
- [x] `Purpose` 足以描述能力（非占位）
