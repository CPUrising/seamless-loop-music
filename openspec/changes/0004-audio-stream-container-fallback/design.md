# Design: 解码器选择与容器嗅探回退

## 决策

### D1：扩展名优先，失败再嗅探

- 现状：`.mp3` → `Mp3FileReader`；`.wav` → `WaveFileReader`；`.flac` → `FlacReader`；`.ogg` → `VorbisWaveReader`。
- 保持不动的原因：`Mp3FileReader` 与 `MediaFoundationReader` 对同一 MP3 报出的 `Length` 不同（实测 23657472 vs 23662064 字节），切换会平移所有已存循环点。
- 因此仅在扩展名解码器**抛异常或返回 null** 时才嗅探，正常文件路径完全不变。

### D2：容器嗅探规则

读取文件前 12 字节：

| 特征 | 判定 | 解码器 |
|---|---|---|
| 偏移 4 起为 `ftyp` | MP4 / M4A | `AudioFileReader`（内部走 Media Foundation） |
| 0 起 `RIFF` | WAV | `WaveFileReader` |
| 0 起 `fLaC` | FLAC | `FlacReader` |
| 0 起 `OggS` | Ogg Vorbis | `VorbisWaveReader` |
| 0 起 `FORM` 且 8 起 `AIFF` | AIFF | `AudioFileReader` |
| 0 起 `ID3` 或 `0xFF` + `(b1 & 0xE0) == 0xE0` | MP3 | `Mp3FileReader` |
| 其它 | Unknown | 最后兜底尝试 `AudioFileReader` |

### D3：`.m4a/.wma/.aiff/.opus` 直接走 `AudioFileReader`

- 现状是返回 `null`（无法播放）。改为 `AudioFileReader` 后 `.m4a/.wma/.aiff` 可用。
- `.opus` 仍不可用（NAudio 无 Opus 解码器），但会以统一方式返回 `null`，与现状一致，不引入新的静默行为。

### D4：三处调用点统一

`AudioLooper.CreateAudioStream`、`PlayerService.CreateNAudioStream`、`TrackMetadataService.GetSampleRate` 各自维护了一套分派逻辑，容易漂移（`TrackMetadataService` 用 `default:` 走 `AudioFileReader`，另两处用 `null`）。统一到 `AudioStreamFactory` 后，扫描期与播放期的解码器选择必然一致，避免采样数不一致导致循环点偏移。

### D5：Media Foundation 音频流必须固定到单一线程

- 实测：`AudioFileReader` 打开 M4A 后，若在另一线程读取，会抛
  `InvalidCastException`（`IMFSourceReader` QueryInterface 返回 `E_NOINTERFACE`）；
  该异常被填充线程的 `catch { break; }` 吞掉，表现为"播放约 1 秒（预填充量）后停止"。
- 决策：`AudioContainerKind.AudioFile` 返回 `ThreadAffineWaveStream` 包装，底层解码器只在专用 MTA 线程创建，
  `WaveFormat` / `Length` / `Position` / `Read` 全部经队列串行转发；缓存 `WaveFormat` 与 `Length` 供无锁读取。
- 否决：把播放器整体改为单线程解码——影响面远大于本 bug。

## 备选方案

| 方案 | 结论 |
|---|---|
| 全部改用 `AudioFileReader` | 否决：会改变 MP3 解码器，平移已存循环点 |
| 只给 `.mp3` 加 try/catch 回退 | 否决：`.m4a/.wma/.aiff` 仍不可播，且三处逻辑继续漂移 |
| 依赖 TagLib 的格式识别 | 否决：TagLib 用于元数据，采样数需与实际解码器一致，不能替代 |

## 风险

| 风险 | 缓解 |
|---|---|
| 嗅探后再失败 | 统一返回 `null`，沿用既有 `Unsupported audio format!` / `Load operation failed` 提示 |
| 损坏文件被误判为某种容器 | 仍会在创建解码器时抛异常，被兜底捕获 |
| `AudioFileReader` 的 float 输出改变播放链 | `AudioLooper` 只依赖 `WaveStream` 与 `BlockAlign`，`ISampleProvider` 由播放链自行转换；本次不改播放链 |
