# Proposal: 音乐节奏/情绪轻量分类（纯信号处理，无机器学习）

## Why

当前应用支持无缝循环与循环点分析，但媒体库没有任何"按节奏快慢/情绪"浏览或筛选的能力。用户希望对本地音乐做一个**轻量、粗糙**的分类：

- **不引入任何机器学习模型或训练数据**，完全用纯信号处理（DSP）特征 + 简单规则；
- 目标维度是**节奏快慢（BPM / 峰值密度）**与**粗糙情绪（能量、亮度）**两个物理指标；
- 需要一个真实曲库（`D:\CloudMusic\恋愛×ロワイアル ORIGINAL SOUNDTRACK`，30 首 MP3，含日文/Unicode 文件名）来验证算法是否靠谱。

关键约束：项目已内置 `loopfinder` 原生分析库，其 third_party 已包含 **aubio**（tempo/onset/FFT），且 [Native.cs](../../../seamless loop music/seamless loop music/Services/LoopFinder/Native.cs) 已有宽字符 P/Invoke，能正确处理上述 Unicode 路径。复用该链路成本最低、性能最好。

## What Changes

1. **`loopfinder` 原生层新增分类导出**：`lf_classify_file_w(const wchar_t*, lf_track_features_t*)`，内部复用 `AudioDecoder::decode(wchar_t*)` 宽字符解码得到单声道 PCM，再用 aubio tempo/onset 估算 BPM 与起始密度，用已有 STFT 计算 RMS 能量与频谱质心，输出结构化特征值。
2. **C# 新增 `MusicClassificationService`**：通过 `LoopFinder.Native` 调用新导出，把原生特征值按**双维四象限规则**（快慢 × 能量/亮度）映射为 `激烈 / 明亮轻快 / 舒缓 / 沉稳` 四分类标签，并保留原始 BPM、能量、质心数值。规则阈值集中在服务内，可调。
3. **媒体库新增"批量分析"入口**：对一个文件夹内的音频文件逐个分析，在结果列表展示每首的文件名、BPM、能量、亮度、分类标签；**仅展示，不写入数据库**。
4. **测试**：为分类规则（纯函数，给定特征值→标签）补充 NUnit 单元测试；用真实测试文件夹做手工/集成验证并在 `docs/verify/0006-*.md` 登记结果。

## Capabilities

### New Capabilities

`music-classification`：本变更首次为"音乐节奏/情绪分类"能力建立规范（此前 `openspec/specs/` 为空）。

### Modified Capabilities

无。

## Impact

- **修改/新增文件**：
  - 原生：`loopfinder/include/loopfinder/loopfinder_api.h`、`loopfinder/src/loopfinder_api.cpp`、可能新增 `loopfinder/include/loopfinder/feature_extractor.h`、`loopfinder/src/feature_extractor.cpp`
  - C#：`seamless loop music/Services/LoopFinder/Native.cs`、新增 `seamless loop music/Services/MusicClassification/IMusicClassificationService.cs`、`MusicClassificationService.cs`、`Models/TrackFeatures.cs`
  - UI：媒体库相关 View/ViewModel（新增"分析文件夹"入口与结果展示），具体文件在 design 中锁定
  - 测试：`SeamlessLoop.Tests/` 新增分类规则测试
- **产物**：需重新构建 `loopfinder.dll` 并复制到应用输出目录（`bin\Debug\net48\loopfinder.dll`）
- **不改变**：循环点分析算法与导出、数据库 schema、播放/同步链路、现有 UI 文案
- **Out of scope（明确不做）**：
  - 不引入任何机器学习/深度学习模型或外部推理引擎
  - 不把分类结果写入数据库、不做按分类浏览媒体库
  - 不改 `PyMusicLooperWrapper` / Python 环境
  - 不处理"文件本身损坏/格式不支持"的细粒度诊断文案（沿用 decode failed 语义）
  - 不做 Android 端分类入口

## 验收口径

- 对 `D:\CloudMusic\恋愛×ロワイアル ORIGINAL SOUNDTRACK` 下 30 首 MP3 批量分析，全部成功返回特征值（BPM、能量、质心）与四分类标签，无 decode failed；日文/Unicode 文件名路径全部正常。
- 分类规则单元测试全绿：给定边界与典型特征值，四象限映射与阈值行为符合 spec。
- 纯 ASCII 路径与 Unicode 路径分类结果一致（同一文件两条路径产出相同特征值与标签）。
- `dotnet build "seamless loop music.sln" -c Debug` 零错误，`loopfinder.dll` 已重建并复制到输出目录。
