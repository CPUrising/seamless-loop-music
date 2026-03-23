# 技术方案：SQLite 数据迁移与别名系统 (From CSV to SQLite)

**日期**：2026-02-10  
**提案人**：Lev Zenith  
**目标**：将存储方案升级为 SQLite，实现“别名系统”与“跨平台配置便携性”。

## 1. 核心变更 (Core Changes)

### 1.1 存储介质升级
- **现状 (Old)**: `loop_config.csv` (文本文件，全量读写，绝对路径)。
- **目标 (New)**: `LoopData.db` (SQLite 数据库，增量读写，相对路径)。

### 1.2 数据模型变更
引入 **别名 (Alias)** 概念，不再直接修改物理文件名。

| 字段 | 旧 CSV | 新 SQLite | 说明 |
| :--- | :--- | :--- | :--- |
| **标识符** | 绝对路径 (Absolute Path) | **相对路径 (Relative Path)** | 关键！支持跨设备/移动文件夹。 |
| **显示名** | 无 (文件名即显示名) | **DisplayName (别名)** | 允许用户自定义歌曲名，不影响文件。 |
| **循环点** | Start, End | Start, End | 保持不变 (采样数)。 |
| **音量** | 无 | Volume (Default 1.0) | 单曲独立音量记忆。 |

## 2. 实施步骤 (Implementation Steps)

### 2.1 引入依赖 (NuGet)
- `System.Data.SQLite.Core`: SQLite 核心库。
- `Dapper`: 轻量级 ORM，简化数据库操作代码。

### 2.2 数据库设计 (Schema)
创建 `LoopPoints` 表：
```sql
CREATE TABLE IF NOT EXISTS LoopPoints (
    Id INTEGER PRIMARY KEY AUTOINCREMENT,
    RelativePath TEXT NOT NULL UNIQUE,  -- 相对路径 (如 "BGM/Battle01.mp3")
    DisplayName TEXT,                   -- 别名系统核心 (存 "最终决战")
    LoopStart INTEGER NOT NULL,
    LoopEnd INTEGER NOT NULL,
    Volume REAL DEFAULT 1.0,
    LastModified DATETIME DEFAULT CURRENT_TIMESTAMP
);
```

### 2.3 数据迁移逻辑 (Migration)
程序启动时执行一次性检查：
1. 检查是否存在 `loop_config.csv`。
2. 读取 CSV 内容。
3. 将绝对路径转换为 **基于程序运行目录的相对路径**。
4. 开启事务 (Transaction)，批量插入 SQLite。
5. 重命名 CSV 为 `.bak` 备份。

### 2.4 代码重构 (Refactoring)
- **数据类**: 创建 `MusicTrack` 类，替代原本的 `string` 路径传递。
- **UI绑定**: 列表数据源改为 `ObservableCollection<MusicTrack>`。
- **重命名交互**: 右键菜单 -> 修改 `DisplayName` -> 更新 DB -> 界面刷新。

## 3. 预期收益 (Benefits)

1.  **无痛改名**: 即使正在播放的歌曲也能随意改名 (只改数据库 DisplayName)，完全规避文件占用问题。
2.  **便携性 (Portable)**: 整个文件夹 (含音乐 + `.db`) 拷贝到手机/其他电脑，只要目录结构相对一致，配置自动生效。
3.  **性能**: 几千首歌的加载速度从几百毫秒降至几毫秒，内存占用更低。

---
*Created by Lev Zenith for cpu*
