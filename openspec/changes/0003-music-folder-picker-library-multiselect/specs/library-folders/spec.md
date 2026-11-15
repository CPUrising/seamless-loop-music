# library-folders Capability Spec

## Purpose

定义"音乐文件夹"的添加与管理规则：如何从系统选择器取得一个或多个真实目录、如何处理 Windows「库」、以及重复与父子嵌套时哪些路径进入音乐库。

## Requirements

### Requirement: Library Selection

系统 SHALL 允许用户在添加音乐文件夹时选择 Windows「库」，并把该库包含的所有文件系统目录加入音乐文件夹列表。

#### Scenario: T01 选中音乐库并添加

- **GIVEN** 用户在设置 → 音乐点击"添加文件夹"
- **WHEN** 用户在系统选择器中选中「音乐」库并确定
- **THEN** 系统 MUST 把该库内所有真实目录（如 `C:\Users\<用户>\Music`）加入音乐文件夹列表
- **AND** MUST NOT 出现"确定后无任何反应"或 `decode failed` 类错误

#### Scenario: T02 无法解析的虚拟位置

- **GIVEN** 用户在选择器中选中「此电脑」等非文件系统、非库的位置
- **WHEN** 用户确定
- **THEN** 系统 MUST 提示该位置无法使用
- **AND** MUST NOT 向 `MusicFolders` 写入任何记录

### Requirement: Multi Folder Selection

选择器 SHALL 支持一次选择多个文件夹，并把每个选中项独立解析后统一入库。

#### Scenario: T03 多选不同目录

- **GIVEN** 用户打开添加文件夹对话框
- **WHEN** 用户按住 Ctrl 选中两个不同目录并确定
- **THEN** 两个目录 MUST 都出现在音乐文件夹列表中
- **AND** 允许其中某一项来自库、另一项为普通目录

### Requirement: Path Normalization And Nesting

添加音乐文件夹时，系统 SHALL 先归一化路径，再按父子包含关系去重，MUST NOT 同时保留互为父子关系的两个文件夹。

#### Scenario: T04 子目录已被父目录覆盖

- **GIVEN** 音乐文件夹中已有 `C:\Music`
- **WHEN** 用户添加 `C:\Music\Album`
- **THEN** 系统 MUST 跳过该候选并提示"未新增文件夹"
- **AND** `C:\Music` MUST 保持不变

#### Scenario: T05 新增父目录替换子目录

- **GIVEN** 音乐文件夹中已有 `C:\Music\Album`
- **WHEN** 用户添加 `C:\Music`
- **THEN** 系统 MUST 删除 `C:\Music\Album` 记录并插入 `C:\Music`
- **AND** 结果列表中 MUST NOT 同时存在这两个路径（含大小写、末尾分隔符不同写法）

#### Scenario: T06 不存在或非法路径

- **GIVEN** 候选目录在磁盘上不存在
- **WHEN** 用户执行添加
- **THEN** 系统 MUST 拒绝该候选
- **AND** MUST NOT 写入 `MusicFolders` 表
