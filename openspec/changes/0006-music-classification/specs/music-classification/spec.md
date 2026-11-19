# music-classification Capability Spec

## Purpose

定义音乐节奏/情绪轻量分类能力的行为合同：把一首本地音频文件解码并提取纯信号处理特征（BPM、能量、频谱质心），按透明规则映射为四象限节奏/情绪标签，并在 UI 对文件夹批量展示结果，全程不使用任何机器学习模型、不写入数据库。

## Requirements

### Requirement: Signal-Based Feature Extraction

系统 SHALL 从本地音频文件提取纯信号处理特征，至少包括每分钟节拍数（BPM）、RMS 能量与频谱质心，且 MUST NOT 依赖任何机器学习或深度学习模型、训练数据或外部推理引擎。

#### Scenario: T01 提取音频物理特征

- **GIVEN** 存在一首可解码的本地音频文件
- **WHEN** 对该文件执行分类特征提取
- **THEN** 系统 MUST 返回 BPM、RMS 能量、频谱质心与时长等特征值
- **AND** MUST NOT 调用任何机器学习/深度学习模型或网络推理服务

#### Scenario: T02 Unicode 路径文件可提取

- **GIVEN** 存在路径含中文/日文的音频文件（如 `D:\CloudMusic\恋愛×ロワイアル ORIGINAL SOUNDTRACK\rian,朝霧はやと,山下航生 - dusk.mp3`）
- **WHEN** 通过宽字符入口提取该文件特征
- **THEN** 系统 MUST 成功解码并返回特征值
- **AND** MUST NOT 返回 `decode failed` 或受系统 ANSI 代码页影响
- **AND** 同一文件的 Unicode 路径与 ASCII 路径副本 MUST 产出相同特征值

### Requirement: Rule-Based Quadrant Classification

系统 SHALL 依据提取的特征，用透明的启发式规则将曲目映射到节奏/情绪四象限标签（激烈 / 明亮轻快 / 舒缓 / 沉稳），规则 MUST 仅依赖 BPM 与能量/亮度等物理量，且阈值集中可配置。

#### Scenario: T03 四象限规则映射

- **GIVEN** 一组已提取的特征值（BPM、能量、频谱质心）
- **WHEN** 执行分类规则
- **THEN** 系统 MUST 输出四象限之一：激烈 / 明亮轻快 / 舒缓 / 沉稳
- **AND** 映射 MUST 仅由 BPM 轴与能量/亮度轴的阈值判定决定，MUST NOT 引入模型打分
- **AND** 阈值 MUST 集中在分类服务中、可调整

#### Scenario: T04 低置信结果显式标记

- **GIVEN** 某文件的 BPM 估算置信度低于设定阈值
- **WHEN** 执行分类
- **THEN** 系统 MUST 仍输出一个分类标签
- **AND** MUST 在结果中标记该标签为"不确定"
- **AND** MUST NOT 静默丢弃该文件

### Requirement: Batch Folder Classification Display

系统 SHALL 提供对一个文件夹内音频文件的批量分类入口，逐文件分析并在结果列表中展示每首的文件名、BPM、能量、亮度与分类标签；结果 MUST NOT 写入数据库。

#### Scenario: T05 批量分析并展示

- **GIVEN** 用户选择包含多个音频文件的文件夹
- **WHEN** 触发批量分析
- **THEN** 系统 MUST 逐个分析并在列表展示每首的文件名、BPM、能量、亮度与分类标签
- **AND** 单个文件失败 MUST NOT 中断整个批次
- **AND** MUST NOT 将分类结果写入数据库或修改 `MusicTrack` 表结构

#### Scenario: T06 测试曲库批量成功

- **GIVEN** 文件夹 `D:\CloudMusic\恋愛×ロワイアル ORIGINAL SOUNDTRACK` 含 30 首 MP3（含日文/Unicode 文件名）
- **WHEN** 对该文件夹批量分类
- **THEN** 全部文件 MUST 成功返回特征值与分类标签
- **AND** MUST NOT 出现 `decode failed`

### Requirement: Native Classification Export Contract

`loopfinder.dll` SHALL 导出宽字符分类入口 `lf_classify_file_w`，应用 SHALL 在加载时校验该导出存在，缺失时 MUST 通过状态消息明确报告而不是静默失败。

#### Scenario: T07 导出缺失时明确报错

- **GIVEN** 输出目录中的 `loopfinder.dll` 为不含 `lf_classify_file_w` 的旧版本
- **WHEN** 应用首次调用分类
- **THEN** 状态消息 MUST 包含 DLL 导出缺失提示
- **AND** MUST NOT 静默返回空特征或空标签
