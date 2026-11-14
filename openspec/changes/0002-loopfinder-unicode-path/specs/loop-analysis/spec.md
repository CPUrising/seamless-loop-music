# loop-analysis Capability Spec

## Purpose

定义循环点分析能力的行为合同：把一首本地音频文件解码为单声道 PCM，并产出若干带评分的循环候选点，供播放器设置无缝循环边界。

## Requirements

### Requirement: Loop Analysis Input Path Handling

循环点分析 SHALL 能打开路径中包含任意 Unicode 字符（含中文、日文、emoji）的本地音频文件，其结果 MUST NOT 依赖操作系统 ANSI 代码页或系统区域设置。

#### Scenario: T01 中文路径 MP3 可分析

- **GIVEN** Windows 系统区域设置为 en-US（ACP=1252）
- **AND** 存在文件 `D:\CloudMusic\电台节目\浅唱Alone - [Stella Sora] Lucky Oasis Escape OST.mp3`
- **WHEN** 调用 `lf_analyze_file_w(L"D:\CloudMusic\电台节目\浅唱Alone - [Stella Sora] Lucky Oasis Escape OST.mp3", 10, ...)`
- **THEN** 返回值 MUST 大于 0（实测为 10）
- **AND** MUST NOT 返回 -1 或令 `lf_get_last_error()` 返回 `decode failed`
- **AND** 首个候选点 MUST 与同文件 ASCII 路径副本的结果一致（实测 `loopStart=1628069`、`loopEnd=4611493`、`score=0.908`）

#### Scenario: T02 非 ASCII 路径 WAV 可分析

- **GIVEN** 存在路径含中文的 WAV 文件
- **WHEN** 通过宽字符入口分析该文件
- **THEN** 系统 MUST 成功解码并返回候选点或明确以 `no loop points found` 结束（返回值 0）
- **AND** MUST NOT 以 `decode failed` 结束

#### Scenario: T03 ASCII 路径行为不回归

- **GIVEN** 存在纯 ASCII 路径的音频文件
- **WHEN** 通过宽字符入口分析该文件
- **THEN** 返回的候选点数量、首个候选的 `loopStart` / `loopEnd` / `score` MUST 与变更前窄字符入口的结果一致

### Requirement: Native Library Export Contract

`loopfinder.dll` SHALL 导出宽字符入口 `lf_analyze_file_w`，应用 SHALL 在加载时校验该导出存在，缺失时 MUST 通过状态消息报告而不是静默失败。

#### Scenario: T04 导出缺失时明确报错

- **GIVEN** 输出目录中的 `loopfinder.dll` 为不含 `lf_analyze_file_w` 的旧版本
- **WHEN** 应用首次调用分析
- **THEN** 状态消息 MUST 包含 DLL 导出缺失提示
- **AND** MUST NOT 静默返回空候选列表
