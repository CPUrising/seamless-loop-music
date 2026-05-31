# loopfinder — C++ 音频循环点检测库

PyMusicLooper 对齐版的 C++ 循环点检测库，编译为 `libloopfinder.so` / `loopfinder.dll`，通过 C API 或 JNI 供 Android 调用。

当前目标是让返回的 Top 候选循环段与 PyMusicLooper 基本一致，同时保留移动端可接受的运行时和少量可调参数。

## 目录结构

```
loopfinder/
├── CMakeLists.txt                  # 顶层构建配置
├── include/loopfinder/             # 公共头文件
│   ├── common.h                    # 数据结构 (PCMData, LoopPoint, STFTResult)
│   ├── audio_decoder.h             # 音频解码接口 (dr_libs)
│   ├── stft.h                      # 短时傅里叶变换 (KissFFT)
│   ├── hpss.h                      # 谐波/打击乐分离
│   ├── chroma.h                    # Chroma 色度特征提取
│   ├── beat_detector.h             # 节拍检测 (aubio)
│   ├── loop_finder.h              # 核心循环查找算法
│   └── loopfinder_api.h           # C API (FFI / CGo / Python ctypes 友好)
├── src/
│   ...
│   └── loopfinder_api.cpp         # C API 实现
├── jni/
│   └── jni_main.cpp                # JNI 入口点
└── third_party/                    # 第三方库 (需手动放入)
    ├── dr_wav.h, dr_flac.h, dr_mp3.h
    ├── stb_vorbis.c, stb_vorbis_impl.c
    ├── kiss_fft.h/c, kiss_fftr.h/c, _kiss_fft_guts.h
    └── aubio/                      # 完整 aubio 源码
        └── src/
            ├── config.h            # ← 项目生成的最小配置
            └── ...                 # 39 个 .c/.h 手动编译
```

## 第三方库依赖

| 库 | 所需文件 | 来源 |
|---|---|---|
| **dr_libs** | `dr_wav.h`, `dr_flac.h`, `dr_mp3.h` | https://github.com/mackron/dr_libs |
| **stb** | `stb_vorbis.c` | https://github.com/nothings/stb |
| **KissFFT** | `kiss_fft.h/c`, `kiss_fftr.h/c`, `_kiss_fft_guts.h` | https://github.com/mborgerding/kissfft |
| **aubio** | 完整源码放入 `third_party/aubio/` | https://github.com/aubio/aubio |

aubio 自带 Ooura FFT 后端，零外部依赖。已生成 `config.h` 做最小配置。
CMakeLists.txt 逐文件列出 19 个 `.c` 源文件编译，不使用 waf。

> 推荐 git submodule:
> ```bash
> git submodule add https://github.com/aubio/aubio.git third_party/aubio
> ```

## 构建

### 桌面端 (测试用)

```bash
cmake -B build -DBUILD_FOR_ANDROID=OFF -DBUILD_TEST=ON
cmake --build build
```

测试可执行文件:

```bash
build/Release/loopfinder_test.exe path/to/audio.flac
```

常用测试参数:

```bash
# 关闭 HPSS，观察非谐波分离路径
build/Release/loopfinder_test.exe path/to/audio.flac --no-hpss

# 调整候选网格密度。数值越小，越允许 off-beat 候选
build/Release/loopfinder_test.exe path/to/audio.flac --grid=1
```

### Android NDK (交叉编译)

```bash
cmake -B build \
    -DCMAKE_TOOLCHAIN_FILE=$NDK/build/cmake/android.toolchain.cmake \
    -DANDROID_ABI=arm64-v8a \
    -DANDROID_PLATFORM=android-24 \
    -DBUILD_FOR_ANDROID=ON
cmake --build build
```

输出：`build/libloopfinder.so`

## JNI 接口

```java
package com.cpu.seamlessloopmobile.jni;

data class LoopPoint(
    val loopStart: Long,    // 采样点 (相对原始文件)
    val loopEnd: Long,      // 采样点 (相对原始文件)
    val noteDiff: Float,    // 音符距离
    val loudnessDiff: Float,// 响度差异
    val score: Float        // 余弦相似度评分
)

class NativeAudio {
    external fun analyzeLoopPoints(filePath: String, topN: Int): Array<LoopPoint>
}
```

JNI 函数签名:
```
Java_com_cpu_seamlessloopmobile_jni_NativeAudio_analyzeLoopPoints(
    JNIEnv* env, jclass, jstring filePath, jint topN
) -> jobjectArray (LoopPoint[])
```

## 算法流水线

```
音频文件
    ↓ dr_libs 解码
PCM float32 单声道
    ↓ 归一化 + librosa-like trim (top_db=40, frame=2048, hop=512)
    ↓ centered STFT (KissFFT, periodic Hann, n_fft=2048, hop=512)
功率谱 (|S|²)
    ↓ 可选 HPSS 谐波分离 (默认开启)
谐波谱/原始功率谱 → librosa-like Chroma filterbank
功率谱 → perceptual_weighting-like dB → power_to_db(ref=median)
原始信号 → aubio 节拍检测 (Ooura FFT, 无外部依赖)
    ↓ beat + 稀疏 fallback grid (默认 4 帧)
候选对枚举
    ↓ 动态音符阈值 + 响度差阈值
余弦相似度打分
    ↓
帧索引 → 采样点 → nearest zero crossing → +trim 偏移
    ↓
Top N 返回
```

## PyMusicLooper 对齐策略

已对齐或近似对齐:

- trim 使用 librosa.effects.trim 的默认尺度。
- STFT 使用 centered frame 坐标，frame 到 sample 的映射与 librosa 接近。
- Chroma 使用接近 `librosa.filters.chroma` 的高斯 filterbank、octave weighting 和 frame-wise infinity norm。
- 响度门限使用接近 `librosa.perceptual_weighting` + `power_to_db(ref=median)` 的流程。
- 候选 note threshold 使用 PyMusicLooper 的动态阈值: `0.0875 * norm(chroma[:, loop_end])`。
- score 使用前后窗口的 chroma cosine similarity 和几何权重。

有意保留的差异:

- PyMusicLooper 使用 librosa beat/PLP；loopfinder 使用 aubio beat，再叠加 fallback grid。默认 `candidateFrameStep=4`，用于让 Top5 候选段更稳定地接近 PyMusicLooper。`--grid=1`/`2` 会允许更多 off-beat 候选。
- HPSS 默认开启。批量测试中 HPSS on/off 的分数都接近，但 HPSS on 的 Top5 更稳；可通过 `LoopFinder::Config::useHPSS=false` 或测试参数 `--no-hpss` 关闭。
- PyMusicLooper 当前的 duration priority 基本不会实际重排；loopfinder 为兼容默认关闭 `prioritizeDuration`。

## 批量对比

仓库根目录提供对比脚本:

```bash
python tools/compare_loopfinder.py music --top 20
```

脚本会同时运行 PyMusicLooper、loopfinder 默认 HPSS、loopfinder `--no-hpss`，并打印候选 sample/frame、C++ Top1 与 PyMusicLooper Top 候选中最近点的距离和 score 差异。

PyMusicLooper 结果会缓存在仓库根目录的 `.loop_compare_cache/`。缓存键包含音频路径、文件大小、mtime 和 `--top`，音频不变时不会重复跑 Python 分析。

刷新缓存:

```bash
python tools/compare_loopfinder.py music --top 20 --refresh-cache
```

可传递 C++ 测试参数:

```bash
python tools/compare_loopfinder.py music/5.flac --top 20 --cpp-arg=--grid=1
```

## 未实现 / 不承担

- 实时播放 (由 App/Oboe 处理)
- 音频导出 (split-audio/extend)
- 元数据标签写入
- 网络流下载
- 交互式 CLI
- M4A/AAC 解码 (dr_libs 不支持)
- FFmpeg 集成
