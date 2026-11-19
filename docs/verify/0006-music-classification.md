# 验证报告：0006-music-classification

## 环境

| 项 | 值 |
|---|---|
| OS | Windows 11 家庭版 中文版 |
| .NET SDK | 10.0.302 |
| 日期 | 2026-11-19 |
| 验证曲库 | `D:\CloudMusic\恋愛×ロワイアル ORIGINAL SOUNDTRACK`（30 首 MP3，含日文/Unicode 文件名） |
| 原生库 | `loopfinder/build/Release/loopfinder.dll`（513,024 字节，2026-11-19 20:18:52） |

## 执行的命令

```powershell
# 1. 原生库配置与构建
cmake -S loopfinder -B loopfinder/build -A x64 -DCMAKE_BUILD_TYPE=Release
cmake --build loopfinder/build --config Release --target loopfinder --parallel

# 2. 导出符号检查（DLL 二进制字符串扫描）
#    lf_classify_file_w: True / lf_analyze_file_w: True

# 3. 托管构建
dotnet build "seamless loop music.sln" -c Debug

# 4. 分类规则单测
dotnet test "SeamlessLoop.Tests/SeamlessLoop.Tests.csproj" -c Debug --filter "FullyQualifiedName~MusicClassificationRuleTests"

# 5. 全量单测
dotnet test "SeamlessLoop.Tests/SeamlessLoop.Tests.csproj" -c Debug

# 6. 真实曲库批量分类（托管服务直调，脚本 artifacts/classify-smoke.ps1）
powershell -NoProfile -ExecutionPolicy Bypass -File artifacts/classify-smoke.ps1
```

| 项 | 结果 | 日志 |
|---|---|---|
| 原生构建 | 成功，产出 `loopfinder.dll`（513,024 字节） | 构建输出见下 |
| 导出符号 | `lf_classify_file_w` 与 `lf_analyze_file_w` 均存在 | — |
| 托管构建 | **0 错误** / 23 警告（全部为既有基线警告，无新增） | — |
| 分类规则单测 | **10/10 通过** | — |
| 全量单测 | 310 通过 / 2 失败（312） | 失败为既有 `VD-0003-02` 环境依赖（见偏差 D1） |
| 真实曲库批量 | **30/30 成功，0 失败，无 decode failed** | [verify-0006-classify-smoke.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0006-classify-smoke.log) |
| `lf_analyze_file_w` 回归 | **未退化**：`dusk.mp3` 寻环 `start=825355 end=4588607 score=0.982` | [verify-0006-loopfinder-regression.log](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0006-loopfinder-regression.log) |

## 场景覆盖

| Requirement | Scenario | 验证方法 | 结果 |
|---|---|---|---|
| Signal-Based Feature Extraction | T01 提取音频物理特征 | 真实曲库 30 首均返回 BPM/RMS/质心/onset 密度（见日志） | **PASS** |
| Signal-Based Feature Extraction | T02 Unicode 路径文件可提取 | 30 首中 29 首含日文/Unicode 文件名，全部成功；`恋愛×ロワイアル ORIGINAL SOUNDTRACK_01.mp3` 等均正常 | **PASS** |
| Rule-Based Quadrant Classification | T03 四象限规则映射 | `MusicClassificationRuleTests` 7 个用例（四象限 + 阈值边界 + 退化 + 双轴合成） | **PASS** |
| Rule-Based Quadrant Classification | T04 低置信结果显式标记 | `T04_Classify_LowConfidence_StillLabelledButMarkedUncertain`、`T04_Classify_HighConfidence_NotMarkedUncertain` | **PASS** |
| Batch Folder Classification Display | T05 批量分析并展示 | 服务层批量 30 首成功；UI 视图与导航入口已实现，**人工 UI 冒烟 NOT RUN**（见偏差 D2） | **PASS（服务层）/ NOT RUN（UI 手工）** |
| Batch Folder Classification Display | T06 测试曲库批量成功 | 30/30 成功，0 decode failed | **PASS** |
| Native Classification Export Contract | T07 导出缺失时明确报错 | `Native` 静态探针检查 `lf_classify_file_w`，缺失时 `LastError` 含 "DLL export (lf_classify_file_w) not found"；**未构造旧版 DLL 实测** | **PASS（代码路径）/ NOT RUN（故障注入）** |

## 真实曲库结果分布

| 分类 | 数量 |
|---|---|
| 激烈 Intense | 8 |
| 明亮轻快 Bright | 7 |
| 舒缓 Calm | 9 |
| 沉稳 Steady | 6 |
| 未知 Unknown | 0 |
| **合计** | **30** |

特征区间（实测）：BPM 90.8–203.8，RMS 0.133–0.336，频谱质心 728–5218 Hz，onset 密度 1.46–2.67 /s。

## 偏差表

| 编号 | 偏差 | 等级 | 处理 |
|---|---|---|---|
| D1 | 全量单测 2 个失败：`PlaybackStatisticsAndroidWpfInteropTests` 硬编码 `E:\codeproject\...` 外部 fixture 路径，本机不存在 | 一般 | 既有环境依赖（`VD-0003-02`），与本次改动无关；已确认 `Test-Path` 均为 False |
| D2 | UI 手工冒烟（选文件夹→分析→列表展示→四象限统计）未执行 | 一般 | 登记为验证债，需人类在 WPF 中确认；服务层与规则层已由自动化覆盖 |
| D3 | T07 故障注入（用旧版 DLL 验证报错文案）未执行 | 一般 | 代码路径已实现并有静态探针；登记为验证债 |
| D4 | 首次原生构建失败：C++ 源文件为无 BOM UTF-8，MSVC 按 CP936 解析导致中文注释吞掉代码（`error C2065: "f" 未声明`） | 严重 | 已在 `CMakeLists.txt` 的 MSVC 分支加 `/utf-8`，重建通过 |
| D5 | 低置信标记偏多：30 首中 26 首 `IsUncertain=True`（aubio tempo 置信度普遍低于 0.5） | 一般 | 属算法特性而非缺陷；阈值 `ConfidenceThreshold` 集中可调，后续可单独开 change 调参 |

## 结论

**需修复后复验（仅剩人工 UI 冒烟）**

- 原生层、托管层、规则层、批量服务层全部有真实执行证据。
- 剩余 D2（UI 手工冒烟）与 D3（故障注入）为验证债，不阻断代码合入，但归档前需人类确认 UI 路径可用。
