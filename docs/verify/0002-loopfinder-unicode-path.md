# 验证报告：0002-loopfinder-unicode-path

## 环境

| 项 | 值 |
|---|---|
| OS | Microsoft Windows 11 家庭版（中文版安装介质） |
| 系统区域设置 | `en-US`（`Get-WinSystemLocale`） |
| ANSI 代码页 | `ACP=1252`（`Encoding.Default.CodePage`） |
| .NET SDK | 10.0.302 |
| 日期 | 2026-11-13 |
| 验证对象 | `seamless loop music\bin\Debug\net48\loopfinder.dll`（510464 字节，2026-11-13 10:55:27） |

## 执行的命令

```powershell
# 原生库（缓存损坏，先重建 build 目录）
cmake -S loopfinder -B loopfinder/build -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build "loopfinder/build" --config Release --target loopfinder --parallel

# 主工程（负责把 loopfinder.dll 复制到输出目录）
dotnet build "seamless loop music.sln" -c Debug
```

- 原生库构建：成功；仅剩第三方 `dr_flac.h(6448) C4244` 既有警告，本次未新增警告（`_wfopen` 已改为 `_wfopen_s`）。
- 主工程构建：`0 个错误`，23 个警告均为测试工程既有可空性警告。
- P/Invoke 验证日志：[artifacts/verify-0002-20261113-105546.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0002-20261113-105546.log)

## 场景覆盖

| Requirement | Scenario | 验证方法 | 结果 |
|---|---|---|---|
| Loop Analysis Input Path Handling | T01 中文路径 MP3 可分析 | `lf_analyze_file_w` 调用原始路径 `D:\CloudMusic\电台节目\浅唱Alone - [Stella Sora] Lucky Oasis Escape OST.mp3` | **PASS**：`ret=10`，first `start=1628069 end=4611493 score=0.908` |
| Loop Analysis Input Path Handling | T02 非 ASCII 路径 WAV 可分析 | 生成 5s/44.1k 单声道正弦 WAV，命名 `中文测试.wav`，宽入口调用 | **PASS**：`ret=10`，first `start=6114 end=159763 score=1.000` |
| Loop Analysis Input Path Handling | T03 ASCII 路径行为不回归 | 同文件复制为 `C:\lf_test\lucky.mp3`，新旧入口对比 | **PASS**：宽入口与窄入口均 `ret=10`，`start=1628069 end=4611493 score=0.908`，与变更前源码构建的基线完全一致 |
| Native Library Export Contract | T04 导出缺失时明确报错 | 代码审查 `Native.cs` 静态探针（`GetProcAddress("lf_analyze_file_w")`，缺失则写入 `LastError` 并输出状态消息） | **PASS（静态检查）**：未构造"旧 DLL"场景做运行时验证，见偏差 D3 |
| — | 附加：日文路径 | 复制为 `日本語テスト.mp3` 后宽入口调用 | **PASS**：`ret=10`，与中文/ASCII 路径结果一致 |
| Loop Analysis Input Path Handling | 附加：日文路径 OGG（内存解码路径） | `D:\FFOutput\music_09\まめ知識.ogg` 宽入口调用；同一文件复制为 `C:\lf_test\ascii.ogg` 后宽/窄入口各调用一次 | **PASS**：三次均 `ret=10`，首候选一致 `start=384528 end=1442827 score=0.917`；窄入口直传日文路径为 `ret=-1`（预期）。日志 [artifacts/verify-0002-ogg-20261113-105859.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0002-ogg-20261113-105859.log) |
| — | 附加：窄入口对中文路径仍失败（证明根因） | `lf_analyze_file` + 中文路径 | **PASS（预期失败）**：`ret=-1`，与变更前一致 |
| — | 附加：WPF 界面手工冒烟 | 人类在 `bin\Debug\net48\seamless loop music.exe` 中打开含非 ASCII 路径曲目并执行自动寻环 | **PASS（人工见证）**：人类确认已完成（2026-11-13）；未留存截图/日志，证据强度弱于自动化用例 |

## A/B 基线对照（证明无回归）

将本次 4 个源文件 `git stash` 后，用**变更前源码**重新构建到独立目录 `loopfinder/build_old`，以窄入口调用同一文件：

| 用例 | 变更前源码构建 | 变更后构建 |
|---|---|---|
| ASCII 路径 MP3 | `ret=10 start=1628069 end=4611493 score=0.908` | `ret=10 start=1628069 end=4611493 score=0.908` |
| 中文路径 MP3 | `ret=-1`（decode failed） | `ret=10 start=1628069 end=4611493 score=0.908` |

随后 `git stash pop` 恢复改动，删除 `build_old`。

## 偏差表

| 编号 | 偏差 | 等级 | 处理 |
|---|---|---|---|
| D1 | 输出目录原有 `loopfinder.dll`（2026-05-26）对同一 ASCII 文件给出 `start=1448128 end=3540930 score=0.810`，与当前源码构建结果不同 | 一般 | 该 DLL 非当前 HEAD 源码产物（仓库目录曾被移动，CMake 缓存路径失效）。已用"变更前源码 A/B 重建"替代它作为基线，结论不变 |
| D2 | FLAC 宽路径（`drflac_open_file_w`）未实测 | 一般 | `NOT RUN`，已登记 `docs/verification-debt.md`（OGG 已用 `まめ知識.ogg` 补测 PASS） |
| D3 | T04 "旧 DLL 缺导出"场景未做运行时验证 | 一般 | `NOT RUN`，仅静态检查；已登记验证债 |
| D4 | WPF 界面手工冒烟（真实点击"自动寻环"） | — | **已由人类完成（2026-11-13，人工见证，无截图留存）**，见 `docs/verification-debt.md` VD-0002-04 |
| D5 | `loopfinder/build` CMake 缓存路径失效，已删除重建 | 一般 | 目录属 `.gitignore` 排除的构建产物，无源码影响 |

## 结论

**Ready for archive（附 2 项已登记的非阻断 NOT RUN）**

- 根因与修复均以真实执行为证据：同一文件在中文/日文路径下由 `ret=-1` 变为 `ret=10`，且与 ASCII 路径结果逐位一致（MP3 与 OGG 均已覆盖）。
- 窄字符入口行为保持不变（A/B 对照一致），Android JNI 不受影响。
- WPF 手工冒烟已由人类完成（人工见证）；剩余 FLAC 宽路径与旧 DLL 提示两项为一般级验证债，不阻断归档。
