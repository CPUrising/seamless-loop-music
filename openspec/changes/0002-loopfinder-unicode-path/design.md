# Design: loopfinder 非 ASCII 路径支持

## 背景事实（已实测，非推断）

| 事实 | 证据 |
|---|---|
| 同一文件、同一 DLL，仅路径不同则结果不同 | 中文路径 `ret=-1 err='decode failed'`；复制到 `C:\lf_test\lucky.mp3` 后 `ret=10`（注：此处使用的是输出目录里的旧 DLL，其数值 `1448128/3540930/0.810` 与当前源码构建不同；以验证报告中的"变更前源码 A/B 基线"`1628069/4611493/0.908` 为准） |
| 本机 ACP=1252、OEMCP=437、系统区域 en-US | `[System.Text.Encoding]::Default.CodePage=1252`、`Get-WinSystemLocale=en-US`、`HKLM\...\Nls\CodePage\ACP=1252` |
| 现有实现全程窄字符 | `Native.cs` 使用 `LPStr`；`audio_decoder.cpp` 使用 `fopen` 系接口 |
| 三个 dr 库均已提供宽字符版本 | `drwav_init_file_w`、`drflac_open_file_w`、`drmp3_init_file_w` |
| `stb_vorbis` 无文件名宽字符版本 | 仅有 `stb_vorbis_decode_filename` / `stb_vorbis_decode_memory` |

## 决策

### D1：新增宽字符导出，而不是在 C# 侧改传 UTF-8

- 选择：`lf_analyze_file_w(const wchar_t*)`，C# 侧 `CharSet.Unicode`。
- 理由：Windows 原生文件 API 的宽字符版本不受 ACP 影响，从根上消除"取决于系统区域设置"的行为差异；不需要改动数据库路径与调用方。
- 否决：把 ACP 转 UTF-8 再让 CRT 按 UTF-8 解释——CRT 的窄字符 `fopen` 仍按 ACP 解释，除本机外无收益。

### D2：窄字符入口保留

- `lf_analyze_file` 保留，Android JNI（`jni_main.cpp`）继续使用；Android 路径来自 JVM 的 UTF-8 字节，`fopen` 语义正确。
- C# 侧不再使用窄入口，避免"两套语义并存"的歧义。

### D3：OGG 走内存解码

- `stb_vorbis` 无宽字符文件名接口，因此宽路径下用 `_wfopen` 读整个文件到内存，再 `stb_vorbis_decode_memory`。
- 权衡：多占用一份压缩体积的内存（测试样本 3.07 MB，可接受）；换来零修改第三方代码。

### D4：解码后处理逻辑保持等价

- 把四个格式的"交错浮点 → 单声道 + 归一化 + 去静音"提取为文件内共享函数，窄/宽入口共用，避免两套实现漂移。
- 采样换算、`trimStart` 偏移语义不变，`lf_loop_point_t` 结构体布局不变（C# 侧无需改结构）。

## 备选方案与权衡

| 方案 | 结论 |
|---|---|
| A. 宽字符导出（本变更） | 采用；改动集中在解码入口，行为可完全回归验证 |
| B. C# 读全部字节 + 新增 `lf_analyze_memory` | 可行但需新增内存 API 并让 C# 承担文件读取，内存与职责都上移，收益不明显 |
| C. `GetShortPathNameW` 转 8.3 短路径 | 依赖 8.3 短名未被禁用，属于绕过；不采用 |

## 风险

- **R1 旧的 `loopfinder.dll` 未同步**：应用探针会因找不到 `lf_analyze_file_w` 报"DLL exports not found"，而不是静默 decode fail。缓解：csproj 已配置 `PreserveNewest` 复制，验证时检查输出目录时间戳。
- **R2 OGG 内存路径**：首次引入 `_wfopen` + 内存解码，需覆盖 OGG 用例；本次若无 OGG 样本，登记为 `NOT RUN` 并进验证债。
- **R3 窄入口回归**：`audio_decoder.cpp` 内重构了共享后处理函数，需保证 ASCII 路径结果与变更前逐位一致（用同一样本对比 best 起点/终点/分值）。
