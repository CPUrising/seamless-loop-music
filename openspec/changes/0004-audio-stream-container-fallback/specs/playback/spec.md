# playback Capability Spec

## Purpose

定义播放器创建音频流时的解码器选择规则：如何根据文件路径与文件内容选择解码器，以及解码失败时的回退与报告方式，确保扩展名与实际容器不一致的音频文件同样可播放。

## Requirements

### Requirement: Decoder Selection By Extension First

播放器 SHALL 优先按文件扩展名选择解码器，以使既有曲目的解码基准保持不变。

#### Scenario: T01 正常 MP3 仍走 Mp3FileReader

- **GIVEN** 一个扩展名为 `.mp3` 且内容为合法 MP3 帧的文件
- **WHEN** 创建音频流
- **THEN** 系统 MUST 返回 `Mp3FileReader` 实例
- **AND** MUST NOT 改用 Media Foundation 解码器

#### Scenario: T02 常用扩展名保持既有解码器

- **GIVEN** 扩展名分别为 `.wav`、`.flac`、`.ogg` 的文件
- **WHEN** 创建音频流
- **THEN** 系统 MUST 分别返回 `WaveFileReader`、`FlacReader`、`VorbisWaveReader` 实例

### Requirement: Container Sniffing Fallback

当按扩展名创建解码器失败时，系统 SHALL 读取文件头判定真实容器，并使用对应解码器重试；MUST NOT 直接放弃播放。

#### Scenario: T03 扩展名为 mp3 但内容为 M4A

- **GIVEN** 文件 `cet6_2024_06_2.mp3`，其文件头为 `00 00 00 1C 66 74 79 70 4D 34 41 20`（`ftypM4A`）
- **WHEN** 创建音频流
- **THEN** 系统 MUST 通过 MP4 解码路径返回可用音频流
- **AND** MUST NOT 因 `Mp3FileReader` 抛异常而使该曲目不可播放

#### Scenario: T04 扩展名与实际内容不符（非 MP4）

- **GIVEN** 一个内容为 WAV 但扩展名为 `.flac` 的文件
- **WHEN** 创建音频流
- **THEN** 系统 MUST 按嗅探结果返回 `WaveFileReader` 实例

#### Scenario: T05 无法识别的内容

- **GIVEN** 一个扩展名未知且文件头不匹配任何已知容器的文件
- **WHEN** 创建音频流
- **THEN** 系统 MUST 返回 `null` 而不是抛出未处理异常
- **AND** 调用方 MUST 沿用既有的不支持格式提示

### Requirement: Playable Container Coverage

播放器 SHALL 为扫描白名单中的 `.m4a`、`.wma`、`.aiff` 提供可用的解码路径。

#### Scenario: T06 m4a 文件可创建音频流

- **GIVEN** 扩展名为 `.m4a` 的 AAC 文件
- **WHEN** 创建音频流
- **THEN** 系统 MUST 返回可用音频流
- **AND** MUST NOT 返回 `null` 导致 `Unsupported audio format!`

### Requirement: Media Foundation Stream Thread Affinity

由 Media Foundation 支撑的音频流 SHALL 只在单一专用线程上创建与访问，MUST NOT 因跨线程访问 COM 对象而中断播放。

#### Scenario: T07 M4A 曲目跨线程读取不中断

- **GIVEN** 一个经 `AudioFileReader`（Media Foundation）打开的 M4A 曲目
- **WHEN** 播放器的填充线程读取音频、UI 线程同时读取播放位置
- **THEN** 读取 MUST 持续返回数据
- **AND** MUST NOT 抛 `InvalidCastException`（`IMFSourceReader` E_NOINTERFACE）
- **AND** MUST NOT 出现"约 1 秒后静默停止"（缓冲被排空）
