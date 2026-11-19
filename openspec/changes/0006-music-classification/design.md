# Design: 音乐节奏/情绪轻量分类

## 1. 总体思路

用**纯信号处理**提取两个物理维度的特征，再用**透明规则**做四象限分类，全程无机器学习：

- **节奏快慢** → BPM（每分钟节拍数），辅以起始密度（onset/秒）。
- **情绪能量/亮度** → RMS 能量 + 频谱质心。

特征提取放**原生层**（复用已内置的 aubio / kissfft，性能与 Unicode 路径都有保障），分类规则放**托管层**（纯函数、可单测、阈值可调）。

## 2. 分层与职责

| 层 | 组件 | 职责 | 复用/新增 |
|---|---|---|---|
| 原生 | `AudioDecoder::decode(const wchar_t*)` | Unicode 路径 → 单声道 PCM | 复用（0002 已建） |
| 原生 | `FeatureExtractor`（新） | 对 PCM 提 BPM、起始密度、RMS、频谱质心 | 新增，内部用 aubio tempo/onset + 现有 `STFT` |
| 原生 | `lf_classify_file_w`（新导出） | 宽字符入口，输出 `lf_track_features_t` | 新增，仿照 `lf_analyze_file_w` |
| 托管 | `LoopFinder.Native` | P/Invoke 新导出，宽字符调用，DLL 探针检查 | 修改 |
| 托管 | `MusicClassificationService` | 调原生→取特征→按规则映射四象限标签 | 新增 |
| 托管 | `TrackFeatures` / `MusicCategory` | 特征值与分类结果模型 | 新增 |
| UI | `MusicClassificationView(ViewModel)` | 选文件夹→批量分析→列表展示（不落库） | 新增视图，注册进 `LibraryContentRegion` |

职责边界遵循 AGENTS.md 第 10 节：分类属于"循环分析/媒体库元数据"侧，归 `IPlayerService` 一侧的语义，但本变更新建独立服务，不污染 `IPlayerService` / `IPlaybackService`。

## 3. 关键决策

### D1 特征提取放原生层而非纯 C#
- **理由**：loopfinder third_party 已内置 aubio（含 `tempo`/`onset`/`specdesc`），BPM 估算成熟；`Native.cs` 已有宽字符 P/Invoke 与 DLL 探针，能处理测试文件夹的日文/Unicode 路径；FFT 有 kissfft 现成的 `STFT`。
- **备选（已否决）**：纯 C#（NAudio 解码 + 自写 onset 自相关）。不碰原生层但 BPM 精度与性能明显弱，且要重复实现解码。
- **代价**：需改 C++/CMake 并重编 `loopfinder.dll`（已在 Impact 与 tasks 中登记）。

### D2 原生输出"特征值"，托管做"分类规则"
- **理由**：规则要可调、要单测。原生只算物理量（BPM、能量、质心），阈值判定放 C# 纯函数 `Classify(features) -> category`，规则改动不用重编 DLL。
- **结构**：`lf_track_features_t { double bpm; double bpmConfidence; double onsetDensity; double rmsEnergy; double spectralCentroid; int64_t durationSamples; int sampleRate; }`。

### D3 四象限分类规则
- 两个轴：**快慢**（BPM）、**能量/亮度**（RMS 与频谱质心合成一个"激进度"得分）。
- 映射（初始阈值，集中在 `MusicClassificationService`，可调）：
  - BPM 高 + 激进度高 → **激烈**
  - BPM 高 + 激进度低 → **明亮轻快**
  - BPM 低 + 激进度低 → **舒缓**
  - BPM 低 + 激进度高 → **沉稳**
- 阈值采用**自适应 + 兜底**：优先用当前批次的特征中位数分界；样本不足或退化时用固定默认阈值（如 BPM=120）。这样同一文件夹内是相对分类，更符合"粗糙"目标，也对 30 首 OST 更稳健。
- **置信度处理**：aubio tempo 给出 BPM 置信度，低于阈值时标签附加"不确定"，但仍有输出，不静默丢弃。

### D4 结果仅展示不落库
- **理由**：先验证算法靠谱再考虑持久化；避免 schema 变更扩大范围。展示模型 `TrackClassificationResult { 文件名/路径, Bpm, Energy, Brightness, Category, 是否不确定 }`。

### D5 UI 入口形态
- 在 `LibraryViewModel.InitializeNavigationCategories` 增加"音乐分类"导航项（参照 `IsStatistics` 的模式新增标记位），选中时 `RequestNavigate("LibraryContentRegion", "MusicClassificationView")`。
- 新视图内：选文件夹（用现有 `FolderPicker`）→ 递归枚举支持的音频扩展名 → 后台逐个分析（带进度/取消）→ `DataGrid`/`ListBox` 展示结果。
- **不改** `TrackListView` 主列表、不往 `MusicTrack` 加字段。

## 4. 数据流

```
文件夹路径
  → 枚举音频文件（mp3/flac/wav/ogg，与解码器能力一致）
  → 对每个文件: Native.ClassifyFile(path)  [P/Invoke 宽字符]
      → lf_classify_file_w → AudioDecoder::decode → FeatureExtractor
      → lf_track_features_t
  → MusicClassificationService.Classify(features) → 四象限标签
  → UI 列表行 {文件名, BPM, 能量, 亮度, 标签, 不确定标记}
```

## 5. 风险与权衡

| 风险 | 等级 | 缓解 |
|---|---|---|
| aubio tempo 对无鼓点/氛围曲 BPM 不准 | 中 | 输出置信度；低置信标"不确定"；自适应阈值降低对绝对 BPM 的依赖 |
| 原生崩溃拖垮应用 | 中 | 沿用 `Native.cs` 现有的 `HandleProcessCorruptedStateExceptions` + try/catch 模式；逐文件隔离失败 |
| 30 首 OST 多为 ACG/器乐，四象限区分度有限 | 中 | 这是"粗糙分类"的既定目标；验证报告如实记录分布，不强行追求均衡 |
| 重编 DLL 后循环点分析回归 | 高 | 构建后用既有 ASCII/Unicode 路径回归 `lf_analyze_file_w`，确认 loopfinder 主功能不退化 |

## 6. 测试策略

- **单元（NUnit）**：`MusicClassificationService.Classify` 为纯函数，覆盖四象限边界、阈值退化、低置信标记。不依赖原生 DLL。
- **集成/手工**：对测试文件夹批量跑，登记 30 首的特征值与标签分布、Unicode 路径成功率；在 `docs/verify/0006-music-classification.md` 记录。
- **回归**：`dotnet build` + 既有 NUnit 全绿；`lf_analyze_file_w` 行为不变。

## 7. Out of scope 复述

不引入 ML；不写库；不做按分类浏览；不改 Python 链路；不做 Android 入口；不改循环点算法。
