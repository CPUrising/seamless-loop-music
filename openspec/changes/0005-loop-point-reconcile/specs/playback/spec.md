# playback Capability Spec

## Purpose

定义播放器在加载曲目时的循环点对齐规则，确保数据库中的循环点与当前解码器实际产出的音频长度一致，避免播放中途被误判为到达循环终点。

## Requirements

### Requirement: Loop Point Reconciliation On Load

加载曲目时，系统 SHALL 以解码器给出的实际采样数校正数据库中的总采样数与循环点，MUST NOT 让基于错误元数据的循环终点提前触发跳转。

#### Scenario: T01 默认整曲循环对齐真实末尾

- **GIVEN** 数据库中某曲目 `TotalSamples=18245760`、`LoopStart=0`、`LoopEnd=18245760`（扫描期由 TagLib 写入）
- **AND** 实际解码器给出 34,157,544 个采样
- **WHEN** 用户播放该曲目
- **THEN** 系统 MUST 把循环终点校正为 34,157,544
- **AND** MUST 把校正结果写回循环点存储
- **AND** MUST NOT 在 18,245,760 采样（约 13:47）处跳回开头

#### Scenario: T02 用户自定义循环点保持不变

- **GIVEN** 数据库中某曲目 `TotalSamples=18245760`、`LoopStart=1000`、`LoopEnd=500000`
- **AND** 实际解码器给出 34,157,544 个采样
- **WHEN** 用户播放该曲目
- **THEN** 系统 MUST 保持 `LoopStart=1000`、`LoopEnd=500000` 不变
- **AND** 仅更新总采样数

#### Scenario: T03 越界循环起点归零

- **GIVEN** 数据库中某曲目 `LoopStart` 大于或等于实际采样数
- **WHEN** 用户播放该曲目
- **THEN** 系统 MUST 将循环起点校正为 0
- **AND** 循环终点 MUST 大于循环起点

#### Scenario: T04 元数据一致时不改动

- **GIVEN** 数据库中总采样数与实际解码采样数差异不超过 1000
- **WHEN** 用户播放该曲目
- **THEN** 系统 MUST NOT 修改循环点
- **AND** MUST NOT 触发写回
