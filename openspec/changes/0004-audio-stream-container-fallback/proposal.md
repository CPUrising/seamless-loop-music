# Proposal: 音频流创建支持容器嗅探回退（修复扩展名与实际格式不符的文件无法播放）

## Why

用户报告 `D:\documents\课本笔记2下\6_en\cet6_2024\cet6_2024_06_2.mp3` 无法播放。

实测结论（`artifacts/probe-20261115-100156.log`）：

- 该文件真实容器是 **MP4/M4A**（文件头 `00 00 00 1C 66 74 79 70 4D 34 41 20` = `ftypM4A `），只是后缀写成了 `.mp3`；
- `Mp3FileReader` 直接解析该文件抛 `InvalidOperationException: Got a frame at sample rate 12000, in an MP3 with sample rate 22050. Mp3FileReader does not support sample rate changes.`；
- 同一文件用 `MediaFoundationReader` / `AudioFileReader` 均能正常打开（22050Hz 单声道，1549.1s）；
- 同一套 NAudio 对正常 MP3（`浅唱Alone ... .mp3`）`Mp3FileReader` 正常，排除解码器缺失。

根因：`AudioLooper.CreateAudioStream` 与 `PlayerService.CreateNAudioStream` **仅按扩展名分派解码器**，没有任何内容探测或失败回退，异常被 `catch` 后只输出 `Load operation failed: ...`。

同时发现第二个缺陷：扫描白名单包含 `.m4a .wma .aiff .opus`，但 `CreateAudioStream` 没有对应分支 → 返回 `null` → `Unsupported audio format!`，即这类文件能入库但永远无法播放。

## What Changes

1. 新增 `Core/AudioStreamFactory.cs`：
   - 扩展名优先（`.mp3/.wav/.flac/.ogg` 保持原有解码器，**不改变已存循环点的采样基准**）；
   - `.m4a/.wma/.aiff/.opus` 走 `AudioFileReader`；
   - 扩展名解码失败时按文件头嗅探（`ftyp` / `RIFF` / `fLaC` / `OggS` / `ID3` / MP3 帧同步 / `FORM..AIFF`）并改用对应解码器；
   - 全部失败返回 `null`，由调用方沿用既有提示。
2. 三处调用点统一改用该工厂：`AudioLooper.CreateAudioStream`、`PlayerService.CreateNAudioStream`、`TrackMetadataService.GetSampleRate`。
3. 新增 `AudioStreamFactoryTests`（容器嗅探纯函数 + 扩展名与内容不符时的回退）。

## Capabilities

### New Capabilities

`playback`：音频流的创建与解码器选择规则（此前 `openspec/specs/` 无该能力基线）。

## Impact

- **修改文件**：`Core/AudioLooper.Loader.cs`、`Services/PlayerService.cs`、`Services/TrackMetadataService.cs`
- **新增文件**：`Core/AudioStreamFactory.cs`、`SeamlessLoop.Tests/AudioStreamFactoryTests.cs`
- **不改变**：扫描白名单、数据库 schema、循环点计算方式、UI
- **Out of scope（明确不做）**：
  - 不支持真正的 Opus 解码（NAudio 无解码器），`.opus` 仍会失败但会明确返回 `null`
  - 不做扩展名自动纠正/改名
  - 不切换已有 MP3 到 Media Foundation（会让已存循环点偏移）

## 验收口径

- 该 `.mp3` 文件（实为 M4A）能加载并播放，`Mp3FileReader` 的失败不再导致整曲不可播；
- 正常 MP3 仍由 `Mp3FileReader` 打开，采样数与变更前一致；
- `dotnet build` 与新增单测通过。
