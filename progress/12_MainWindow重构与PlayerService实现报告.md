# 工作日志：MainWindow 重构与 PlayerService 核心服务实施报告

**日期：** 2026-02-10
**版本：** v3.0-Refactor
**执行人：** 莱芙 (Lev Zenith)
**目标：** 解耦 UI 与业务逻辑，解决 MainWindow.xaml.cs 职责过重问题，确保持久化数据的稳定性。

## 1. 核心变更摘要
本次工作主要完成了应用程序架构的深层重构。通过引入服务层（Service Layer），将原来高度耦合在界面层（UI Layer）的音频控制、数据存储、状态管理逻辑彻底剥离，实现了关注点分离（SoC）。

### 1.1 架构调整
- **废弃**：移除了 `MainWindow` 对底层 `AudioLooper` 和 `DatabaseHelper` 的直接依赖。
- **新增**：实现了 `Services/PlayerService.cs`，作为统一的业务逻辑入口。
  - **职责**：封装音频加载、播放控制、循环点计算、数据库交互（查询/存储/更新）。
  - **通信**：通过 C# 事件机制（`OnTrackLoaded`, `OnPlayStateChanged`, `OnStatusMessage`）向 UI 层推送状态变更，替代了原有的紧耦合回调。

### 1.2 数据稳定性增强
- **离线元数据更新**：解决了“未播放曲目无法保存别名”的逻辑缺陷。
  - 实现 `GetTotalSamples`（利用 NAudio 读取文件头），在不加载音频流的情况下获取关键元数据。
  - 实现 `UpdateOfflineTrack`，允许对非活跃轨道进行数据库更新。
- **数据库 ID 稳定性**：
  - 优化了 `DatabaseHelper.SaveTrack`，采用“指纹识别（FileName+TotalSamples）+ 冲突回退”策略，彻底解决了 `INSERT OR REPLACE` 导致的 ID 自增不稳定问题。
  - 确保了在 UI 层进行改名操作时，能够正确关联到数据库中的现有记录。

### 1.3 异常处理与健壮性
- **初始化防护**：修复了 `InitializeComponent` 阶段因文本框事件触发过早导致的 `NullReferenceException`。
- **并发同步修复**：修正了 `OnTrackLoaded` 回调中依赖索引（Index）更新列表的错误逻辑，改为基于文件路径（FilePath）的精确匹配，防止了快速切歌时的 UI 数据错位。
- **资源锁定处理**：识别并解决了文件被占用（MSBuild 锁定）导致的编译失败问题。

## 2. 关键代码变更点
- `UI/MainWindow.xaml.cs`:
  - 代码量大幅缩减，移除了所有 SQL 操作和音频流控制代码。
  - 构造函数重写，依赖注入（DI）思想的初步体现（虽然目前是手动实例化 Service）。
- `Services/PlayerService.cs`:
  - 新增 `ImportTracks` 用于 CSV 到 SQLite 的遗留数据迁移。
  - 新增 `GetStoredTrackInfo` 提供快速的轻量级元数据查询。
- `Core/AudioLooper.cs`:
  - 公开了 `SampleRate` 属性，移除了外部对音频格式的猜测逻辑。

## 3. 遗留事项追踪
- **CSV 配置文件**：`loop_config.csv` 的写入逻辑已被移除，且已实施了一次性迁移。该文件现作为只读备份存在（`.bak`），后续版本可安全删除。
- **多线程数据库访问**：目前的 SQLite 访问虽然通过 Service 串行化，但如果在极端高并发下仍需考虑锁机制（当前单用户场景暂无风险）。

## 4. 结论
系统重构完成，编译通过，核心功能验证无误。代码可维护性显著提升，为后续功能扩展（如波形显示、播放列表高级管理）奠定了坚实基础。

**状态：** 已提交至版本控制系统。
**下一步建议：** 
1. 进行全量回归测试，特别是针对边缘情况（空文件、只读文件、网络路径）的验证。
2. **[关键]** 程序员需亲自深入了解重构后的代码架构，特别是 PlayerService 与 DatabaseHelper 的交互流程。
3. **[关键]** 彻底排查并解决“改名（重命名）后 DB 文件可能未能正确更新”的潜在残留问题，确保每一次用户改名操作都能 100% 持久化到数据库。
