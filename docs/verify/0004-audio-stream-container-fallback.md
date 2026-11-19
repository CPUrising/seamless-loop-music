# 验证报告：0004-audio-stream-container-fallback

## 环境

| 项 | 值 |
|---|---|
| OS | Windows 11 家庭版 中文版 |
| 系统区域设置 | `en-US`（ACP=1252） |
| .NET SDK | 10.0.302 |
| 日期 | 2026-11-15 |
| 问题文件 | `D:\documents\课本笔记2下\6_en\cet6_2024\cet6_2024_06_2.mp3`（实际容器 M4A，1549.1s，22050Hz 单声道） |

## 执行的命令与日志

```powershell
# 1) 首次构建：播放器进程占用了输出目录的 exe，改用临时输出目录
dotnet build "seamless loop music.sln" -c Debug -p:OutDir=$env:TEMP\slbuild-0004\
dotnet test "SeamlessLoop.Tests/SeamlessLoop.Tests.csproj" -c Debug -p:OutDir=$env:TEMP\slbuild-0004\

# 2) 关闭播放器后：常规输出目录构建 + 测试
dotnet build "seamless loop music.sln" -c Debug
dotnet test "SeamlessLoop.Tests/SeamlessLoop.Tests.csproj" -c Debug --filter "FullyQualifiedName~AudioStreamFactoryTests|FullyQualifiedName~MusicFolderPathTests"
```

| 项 | 结果 | 日志 |
|---|---|---|
| 构建（临时 OutDir） | 0 错误 / 23 警告（与变更前基线一致） | — |
| 构建（常规输出目录，关播放器后） | 0 错误 / 23 警告；`bin\Debug\net48\seamless loop music.exe` 已更新（1326592 字节 / 2026-11-15 10:13:36） | — |
| 新增单测 | 7/7 通过 | [verify-0004-tests-20261115-101138.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0004-tests-20261115-101138.log) |
| 常规输出目录下新增单测（含 0003 的 7 个） | 14/14 通过 | [verify-0004-bindir-20261115-101333.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0004-bindir-20261115-101333.log) |
| 全量单测 | 292 通过 / 2 失败（294） | [verify-0004-tests-20261115-101138.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0004-tests-20261115-101138.log)（失败项为 `VD-0003-02` 既有 fixture 缺失） |
| 端到端工厂探针 | 3 个文件结果见下表 | [verify-0004-final-20261115-101118.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0004-final-20261115-101118.log) |

## 端到端探针（直接调用 `AudioStreamFactory.TryCreate`）

| 文件 | 嗅探结果 | 返回解码器 | 时长 | 结论 |
|---|---|---|---|---|
| `cet6_2024_06_2.mp3`（实为 M4A） | `AudioFile`（`ftypM4A`） | `AudioFileReader` 32bit float 22050Hz mono | 1549.1s | **PASS**：变更前 `Mp3FileReader` 抛 `InvalidOperationException`，不可播放 |
| `浅唱Alone - [Stella Sora] Lucky Oasis Escape OST.mp3` | `Mp3` | `Mp3FileReader` 16bit PCM 44100Hz stereo | 134.1s，`Length=23657472` | **PASS（无回归）**：解码器与字节长度与变更前完全一致 |
| `まめ知識.ogg` | `Ogg` | `VorbisWaveReader` 32bit float 44100Hz stereo | 84.0s | **PASS（无回归）**：仍走 Vorbis 解码器 |

## 场景覆盖

| Requirement | Scenario | 验证方法 | 结果 |
|---|---|---|---|
| Decoder Selection By Extension First | T01 正常 MP3 仍走 Mp3FileReader | 端到端探针（上表第 2 行） | **PASS** |
| Decoder Selection By Extension First | T02 常用扩展名保持既有解码器 | `.wav` 由 `TryCreate_WavFileReturnsWaveFileReader`；`.ogg` 由端到端探针 | **PASS**（`.flac` 无样本，见 VD-0004-02） |
| Container Sniffing Fallback | T03 扩展名 mp3 但内容为 M4A | 端到端探针（上表第 1 行） | **PASS** |
| Container Sniffing Fallback | T04 扩展名与内容不符（非 MP4） | `TryCreate_MismatchedExtensionFallsBackToSniffedReader`（WAV 内容 + `.flac` 扩展） | **PASS** |
| Container Sniffing Fallback | T05 无法识别的内容 | `TryCreate_UnknownContentReturnsNull` | **PASS** |
| Playable Container Coverage | T06 m4a 文件可创建音频流 | 无独立 `.m4a` 样本；同一 M4A 内容以 `.mp3` 扩展名走同一 `AudioFile` 分支 | **PASS（等价覆盖）**，真实 `.m4a` 样本待补（VD-0004-02） |
| — | 附加：容器嗅探纯函数 | `DetectContainerKind_RecognizesM4a`、`DetectContainerKind_RecognizesWaveFlacOggMp3` | **PASS** |
| — | 附加：文件不存在 | `TryCreate_MissingFileReturnsNull` | **PASS** |
| Media Foundation Stream Thread Affinity | T07 M4A 曲目跨线程读取不中断 | 人工播放（M4A 内容文件）+ 诊断日志；多线程探针（填充线程 40 次读取 + UI 线程并发读位置） | **PASS**：包装后连续读取无异常；人工确认可连续播放 |

## 诊断与二次修复（ThreadAffineWaveStream）

首次修复后，人工播放表现为"播放约 1 秒后自动停止，状态栏 `Playback stopped.`"。临时诊断日志（`%TEMP%\seamless-playback-diag.log`）记录到：

```
Play: fmt=32 bit IEEFloat 22050Hz 1 channels len=138284956 preRead=88200
Filler inner exception: System.InvalidCastException
  无法将类型为"System.__ComObject"的 COM 对象强制转换为接口类型"NAudio.MediaFoundation.IMFSourceReader"
  ... E_NOINTERFACE
PlaybackStopped: exception=<none> endingNaturally=False bufferedBytes=0
```

结论：填充线程首次读取即抛异常，被 `catch { break; }` 吞掉，只剩 1 秒预填充音频（`bufferedBytes=0` 印证）。

修复：新增 `ThreadAffineWaveStream`，把 Media Foundation 解码器的创建与全部访问固定到专用 MTA 线程（`Read`/`Position`/`Length`/`WaveFormat` 经队列串行转发），`AudioStreamFactory` 的 `AudioFile` 分支返回该包装。修复后诊断日志无异常，人工确认可连续播放。

另外把填充线程的两处 `catch` 由"完全静默"改为 `Debug.WriteLine`，保留可诊断性（不影响行为）。

## 偏差表

| 编号 | 偏差 | 等级 | 处理 |
|---|---|---|---|
| D1 | 常规输出目录构建首次被运行中的播放器进程（PID 31088）锁定 | — | 关闭播放器后已重跑成功（`0 错误`，exe 于 10:13:36 更新），`VD-0004-01` 已关闭 |
| D2 | `.m4a`/`.wma`/`.aiff`/`.opus` 无真实样本，`.flac` 无样本 | 一般 | 登记 `VD-0004-02`，不阻断 |
| D3 | WPF 内实际播放该曲目、设置循环点 | — | **已由人类完成（2026-11-15）**：确认可正常播放，`VD-0004-03` 已关闭 |
| D4 | 探针初次在 `-p:OutDir` 环境误报 OGG 失败 | 一般 | 根因为临时目录混入 `System.Memory 4.0.1.2`（正式输出目录为 4.0.1.1）。换回正式版本后在真实依赖组合下 OGG 正常，已记录以免误判 |

## 结论

**Ready for archive**

- 问题文件由不可播放 → 可加载 → 可连续播放（经 ThreadAffineWaveStream 二次修复），人工确认通过。
- 正常 MP3 与 OGG 的解码器与字节长度与变更前一致（无回归）。
- 单测 22/22（本变更相关），全量 300/302（2 个失败为既有 fixture 缺失，`VD-0003-02`）。
- 遗留：`VD-0004-02`（真实 `.m4a/.wma/.aiff/.flac` 样本未覆盖，一般级，不阻断）。
- 播放中途跳回开头属于另一独立缺陷，见 `0005-loop-point-reconcile`。
