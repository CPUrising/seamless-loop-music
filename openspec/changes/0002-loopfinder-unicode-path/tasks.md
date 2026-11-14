# Tasks

> 共 6 项，符合 `AGENTS.md` 第 4.1 节上限。

## 1. 原生层

- [x] 1.1 `audio_decoder.h/.cpp`：新增宽字符 `decode(const wchar_t*, PCMData&)`；四格式改走 `dr*_init_file_w`；OGG 宽路径用 `_wfopen_s` + `stb_vorbis_decode_memory`；提取共享后处理函数
- [x] 1.2 `loopfinder_api.h/.cpp`：新增 `lf_analyze_file_w`，与 `lf_analyze_file` 共享分析主体

## 2. 应用层

- [x] 2.1 `Native.cs`：`DllImport` 改为 `CharSet.Unicode` + `LPWStr`，探针检查 `lf_analyze_file_w`，移除窄入口调用

## 3. 构建与验证

- [x] 3.1 `cmake --build loopfinder/build --config Release --target loopfinder` 重建 DLL（CMake 缓存路径失效，已删除 `build` 后重新 configure），确认 `build\Release\loopfinder.dll` 时间戳更新
- [x] 3.2 `dotnet build "seamless loop music.sln" -c Debug` 零错误，输出目录 `loopfinder.dll` 已是最新（510464 字节 / 10:55:27）
- [x] 3.3 实测：原始中文路径返回 10 个候选（`1628069/4611493/0.908`）；ASCII 路径回归一致；中文名 WAV 用例 PASS；日文路径 OGG（`D:\FFOutput\music_09\まめ知識.ogg`）宽入口 PASS 且与 ASCII 副本一致
- [x] 3.4 回填 `docs/verify/0002-loopfinder-unicode-path.md`（环境 / 命令 / A/B 基线 / 场景覆盖 / 偏差 / 结论）

## 完成定义（DoD）

- 3.3 的两个路径结果一致（起点/终点/分值相同）；
- 无法覆盖的用例（OGG/FLAC、旧 DLL 提示、WPF 手工冒烟）显式写 `NOT RUN` 并进 `docs/verification-debt.md`；
- 未放宽任何既有断言，未改动 `openspec/specs/` 基线（本次为首次建立 `loop-analysis` 增量）。

## 规范校验（人工校验，CLI 未安装）

- [x] 每个 Requirement 含 `SHALL` / `MUST` 语句
- [x] 每个 Scenario 有唯一 `Txx` 编号且 GIVEN / WHEN / THEN 完整
- [x] 本次为新建 `loop-analysis` 增量，使用 `ADDED` 语义，无 `MODIFIED` 需携带完整新版
- [x] `Purpose` 足以描述能力（非占位）
