# Proposal: loopfinder 支持非 ASCII（Unicode）文件路径

## Why

实测结论（2026-11-13）：在系统区域设置为 en-US（ACP=1252）的机器上，`loopfinder` 对**任何含非 ASCII 字符路径**的音频都返回 `decode failed`，而同一文件复制到纯 ASCII 路径后能正常返回 10 个候选点。

根因链：

1. `Native.cs` 以 `[MarshalAs(UnmanagedType.LPStr)]` 传路径，.NET Framework 按系统 ANSI 代码页转换，1252 下中文字符被替换为 `?`；
2. `audio_decoder.cpp` 全部使用窄字符 `fopen` 系接口（`drmp3_init_file` / `drwav_init_file` / `drflac_open_file` / `stb_vorbis_decode_filename`），同样受 ANSI 代码页约束；
3. 因此文件的真实位置根本无法打开，`lf_analyze_file` 返回 -1，UI 显示 `[loopfinder] decode failed`。

影响面：凡媒体库目录含中文（本机为 `D:\CloudMusic\电台节目\...`）的整库失败；其它机器若是 zh-CN（ACP=936）则恰好正常，导致"换台电脑就好"的假象。

## What Changes

1. `loopfinder` 原生层新增宽字符入口 `lf_analyze_file_w(const wchar_t*)`（仅 Windows 导出），内部改用 `drwav_init_file_w` / `drflac_open_file_w` / `drmp3_init_file_w`；OGG 用 `_wfopen` 读入内存后交给 `stb_vorbis_decode_memory`。
2. `AudioDecoder` 增加宽字符 `decode` 重载；窄字符入口保留不动（Android JNI 使用）。
3. `Native.cs` 改为 `CharSet.Unicode`（`LPWStr`）调用 `lf_analyze_file_w`，DLL 探针改为检查该导出。

## Capabilities

### New Capabilities

`loop-analysis`：本变更首次为"循环点分析"能力建立规范（此前 `openspec/specs/` 为空）。

### Modified Capabilities

无。

## Impact

- **修改文件**：`loopfinder/include/loopfinder/audio_decoder.h`、`loopfinder/include/loopfinder/loopfinder_api.h`、`loopfinder/src/audio_decoder.cpp`、`loopfinder/src/loopfinder_api.cpp`、`seamless loop music/Services/LoopFinder/Native.cs`
- **产物**：需重新构建 `loopfinder.dll` 并复制到应用输出目录（`bin\Debug\net48\loopfinder.dll`）
- **不改变**：分析算法、候选点排序、采样换算、数据库结构、UI 文案
- **Out of scope（明确不做）**：
  - 不改 `PyMusicLooperWrapper` / Python 环境
  - 不处理"文件本身损坏/格式不支持"的诊断文案（仍为 `decode failed`）
  - 不新增 Android 宽字符入口（Android 路径为 UTF-8，窄接口已正确）

## 验收口径

- 原始中文路径 `D:\CloudMusic\电台节目\浅唱Alone - [Stella Sora] Lucky Oasis Escape OST.mp3` 通过 `lf_analyze_file_w` 返回 10 个候选点，best `start=1628069 end=4611493 score≈0.908`（与 ASCII 路径副本、以及变更前源码构建的 ASCII 基线三者一致）。
- 纯 ASCII 路径回归不退化。
- `dotnet build "seamless loop music.sln" -c Debug` 零错误，`loopfinder.dll` 已复制到输出目录。
