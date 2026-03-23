# PyMusicLooper

[![Downloads](https://static.pepy.tech/badge/pymusiclooper)](https://pepy.tech/project/pymusiclooper)
[![Downloads](https://static.pepy.tech/badge/pymusiclooper/month)](https://pepy.tech/project/pymusiclooper)
[![PyPI pyversions](https://img.shields.io/pypi/v/pymusiclooper.svg)](https://pypi.python.org/pypi/pymusiclooper/)
[![PyPI pyversions](https://img.shields.io/pypi/pyversions/pymusiclooper.svg)](https://pypi.python.org/pypi/pymusiclooper/)

一个基于 Python 的程序，通过自动寻找最佳循环点，来实现音乐的无缝且无限的循环播放。

特性：

- 在任何音频文件中寻找循环点（如果存在）。
- 支持加载最常见的音频格式（MP3, OGG, FLAC, WAV），并通过 ffmpeg 支持更多编解码器。
- 使用自动发现的最佳循环点，或使用音频文件中现有的循环元数据标签，无缝且无限地通过播放音频文件。
- 导出为 前奏/循环/尾奏 (intro/loop/outro) 部分，以便在任何支持 [无缝播放 (gapless playback)](https://en.wikipedia.org/wiki/Gapless_playback) 的音乐播放器中进行编辑或无缝播放。
- 直接将循环点以采样点 (samples) 的形式导出到终端或文本文件（例如，用于创建具有无缝循环音频的自定义主题）。
- 将循环点作为元数据标签导出到输入音频文件的副本中，供游戏引擎等使用。
- 通过无缝循环至所需长度，导出音频轨道的加长/扩展版本。

## 先决条件 (Pre-requisites)

必须安装以下软件才能使 `pymusiclooper` 正常工作。

- [Python (64-bit)](https://www.python.org/downloads/) >=3.10
- [ffmpeg](https://ffmpeg.org/download.html)：从 youtube（或任何 [yt-dlp](https://github.com/yt-dlp/yt-dlp) 支持的流媒体）加载音频所必需，并增加了对加载其他音频格式和编解码器的支持，如 M4A/AAC, Apple Lossless (ALAC), WMA, ATRAC (.at9) 等。完整列表可以在 [ffmpeg 的文档](https://www.ffmpeg.org/general.html#Audio-Codecs) 中找到。如果不需要上述功能，可以跳过此项。

*无需* ffmpeg 支持的音频格式包括：WAV, FLAC, Ogg/Vorbis, Ogg/Opus, MP3。
完整列表可以在 [libsndfile 的支持格式页面](https://libsndfile.github.io/libsndfile/formats.html) 找到。

此外，要在 Linux 系统上使用 `play` 命令，您可能需要安装 PortAudio 库。在 Ubuntu 上，运行 `sudo apt install libportaudio2`。

## 安装 (Installation)

### 选项 1: 使用 uv 安装 [推荐]

强烈推荐此安装方法，因为它将 PyMusicLooper 的依赖项与您环境的其余部分隔离开来，
从而避免了依赖冲突和其他包导致的损坏。

所需工具：[`uv`](https://github.com/astral-sh/uv)。

注意：不需要 python，因为如果不存在，`uv` 会自动安装此包所需的 python 版本。

```sh
# 正常安装
# (遵循 https://pypi.org/project/pymusiclooper/ 上的官方发布)
uv tool install pymusiclooper

# 替代安装
# (遵循 git 仓库；相当于每夜构建版本/nightly release channel)
uv tool install git+https://github.com/arkrow/PyMusicLooper.git

# 在任何一种情况下，更新到新版本都可以简单地使用：
uv tool upgrade pymusiclooper
```

安装说明：如果最新的 Python 版本不受支持并导致安装失败，您可能需要指定 Python 版本，例如：

```sh
uv tool install pymusiclooper --python "3.12"
```

### 选项 2: 使用 pipx 安装

像 `uv` 一样，将 PyMusicLooper 的依赖项与您环境的其余部分隔离开来，
从而避免了依赖冲突和其他包导致的损坏。
但是，与 `uv` 不同，它需要已经安装了 python 和 `pipx`。

所需 python 包：[`pipx`](https://pypa.github.io/pipx/) (可以使用 `pip install pipx` 安装)。

```sh
# 正常安装
# (遵循 https://pypi.org/project/pymusiclooper/ 上的官方发布)
pipx install pymusiclooper

# 替代安装
# (遵循 git 仓库；相当于每夜构建版本/nightly release channel)
pipx install git+https://github.com/arkrow/PyMusicLooper.git

# 在任何一种情况下，更新到新版本都可以简单地使用：
pipx upgrade pymusiclooper
```

### 选项 3: 使用 pip 安装

传统的包安装方法。

*注意：与使用 `uv` 或 `pipx` 安装相比，这种方法比较脆弱。如果 PyMusicLooper 的依赖项被另一个包覆盖，它可能会突然停止工作（例如 [issue #12](https://github.com/arkrow/PyMusicLooper/issues/12)）。*

```sh
pip install pymusiclooper
```

## 可用命令 (Available Commands)

![pymusiclooper --help](README_CN.assets/pymusiclooper.svg+xml)

注意：更多帮助和选项可以在每个子命令的帮助信息中找到（例如 `pymusiclooper export-points --help`）；
所有命令及其 `--help` 信息可以在 [CLI_README.md](https://github.com/arkrow/PyMusicLooper/blob/master/CLI_README.md) 中查看。

**注意**：强烈建议使用交互式 `-i` 选项，因为自动选择的“最佳”循环点在听感上并不一定是最好的。因此，所有示例中均显示了该选项。如果省略 `-i` 标志，则可以禁用它。批量处理时也可以使用交互模式。

## 使用示例 (Example Usage)

### 播放 (Play)

```sh
# 使用发现的最佳循环点循环播放歌曲。
pymusiclooper -i play --path "TRACK_NAME.mp3"


# 音频也可以从 yt-dlp 支持的任何流媒体加载，例如 youtube
# (`tag` 和 `split-audio` 子命令也可用)
pymusiclooper -i play --url "https://www.youtube.com/watch?v=dQw4w9WgXcQ"


# 读取音频文件的循环元数据标签，并使用文件中指定的循环开始和结束点（必须以采样点/samples存储）
# 播放并在循环处于活动状态时播放
pymusiclooper play-tagged --path "TRACK_NAME.mp3" --tag-names LOOP_START LOOP_END
```

### 导出 (Export)

*注意：所有导出子命令均可使用批量处理。只需指定一个目录而不是文件作为路径即可。*

```sh
# 将音频轨道并在 前奏(intro)、循环(loop) 和 尾奏(outro) 文件。
pymusiclooper -i split-audio --path "TRACK_NAME.ogg"

# 将轨道扩展到一小时长 (--extended-length 接受以秒为单位的数字)
pymusiclooper -i extend --path "TRACK_NAME.ogg" --extended-length 3600

# 将轨道扩展到一小时长，带有尾奏，并且格式为 OGG
pymusiclooper -i extend --path "TRACK_NAME.ogg" --extended-length 3600 --disable-fade-out --format "OGG"

# 直接将最佳/选定的循环点以采样点 (sample points) 的形式导出到终端
pymusiclooper -i export-points --path "/path/to/track.wav"

# 直接将所有发现的循环点以采样点 (sample points) 的形式导出到终端
# 输出与带有采样点循环值的交互模式相同，但没有格式化和分页
# 格式：loop_start loop_end note_difference loudness_difference score
pymusiclooper export-points --path "/path/to/track.wav" --alt-export-top -1

# 将发现的最佳循环点的元数据标签添加到输入音频文件的副本中
# (如果使用的是目录路径，则为目录中的所有音频文件)
pymusiclooper -i tag --path "TRACK_NAME.mp3" --tag-names LOOP_START LOOP_END


# 将特定目录中所有轨道的循环点（以采样点为单位）导出到 loops.txt 文件
# (兼容 https://github.com/libertyernie/LoopingAudioConverter/)
# 注意：loop.txt 中的每一行遵循以下格式：{loop-start} {loop-end} {filename}
pymusiclooper -i export-points --path "/path/to/dir/" --export-to txt
```

### 其他 (Miscellaneous)

#### 寻找更多潜在的循环

```sh
# 如果检测到的循环点不令人满意，暴力选项 `--brute-force`
# 可能会产生更好的结果。
## 注意：暴力模式检查整个音频轨道而不是检测到的节拍。
## 这会导致运行时间长得多（可能需要几分钟）。
## 在后台处理期间，程序可能会显得冻结。
pymusiclooper -i export-points --path "TRACK_NAME.wav" --brute-force


# 默认情况下，当有 >=100 对可能的循环对时，程序会根据内部标准
# 进一步过滤初始发现的循环点。
# 如果不希望这样，可以使用 `--disable-pruning` 标志禁用它，例如
pymusiclooper -i export-points --path "TRACK_NAME.wav" --disable-pruning
# 注意：如果需要，可以与 --brute-force 一起使用
```

#### 调整循环长度约束

*默认情况下，最小循环持续时间是轨道长度的 35%（不包括尾部静音），最大值无限制。
可以使用以下选项指定替代约束。*

```sh
# 如果循环非常长（或非常短），可以指定不同的最小循环持续时间。
## --min-duration-multiplier 0.85 意味着循环至少是轨道的 85%，
## 不包括尾部静音。
pymusiclooper -i split-audio --path "TRACK_NAME.flac" --min-duration-multiplier 0.85

# 或者，可以以秒为单位指定循环长度约束
pymusiclooper -i split-audio --path "TRACK_NAME.flac" --min-loop-duration 120 --max-loop-duration 150
```

#### 在所需的 开始/结束 循环点附近搜索

```sh
# 如果已经知道所需的循环点，并且您想提取最佳循环
# 位置（以采样点为单位），可以使用 `--approx-loop-position` 选项，
# 它会在指定点的 +/- 2 秒范围内搜索。
# 最好以交互方式使用。使用 `export-points` 子命令的示例：
pymusiclooper -i export-points --path "/path/to/track.mp3" --approx-loop-position 20 210
## `--approx-loop-position 20 210` 意味着所需的循环点从 20 秒左右开始
## 并在 210 秒标记左右循环回来。
```

## 致谢 (Acknowledgement)

本项目最初是 [Nolan Nicholson](https://github.com/NolanNicholson) 的项目 [Looper](https://github.com/NolanNicholson/Looper/) 的一个复刻 (fork)。虽然由于采用了完全不同的方法和实现，目前该项目中只保留了几行代码；但如果没有他们最初的贡献，这个项目是不可能实现的。

## 版本历史 (Version History)

可在 [CHANGELOG.md](CHANGELOG.md) 查看

## Star 历史 (Star History)

[![Star History Chart](https://api.star-history.com/svg?repos=arkrow/PyMusicLooper&type=Date)](https://www.star-history.com/#arkrow/PyMusicLooper&Date)
