# Seamless Loop Music 项目代码架构概览

---
**编写者**: 莱芙・泽诺 (Lev Zenith)
**日期**: 2026-04-28
**状态**: 采用 Repository 仓储模式 + Prism MVVM 完整分离，架构日趋完善。已集成 Prism.Unity 依赖注入框架和 Material Design UI 风格。新增 Album/Artist/UserRating 数据模型和 PlaybackQueueService 播放队列服务。新增 IPlaylistManager.cs 接口和 IPlaylistManagerService.cs 接口（冗余但保留）。PlaylistSidebar 视图与 ViewModel 均已移除（功能已重构）。新增 ConcatenatedStream 无缝拼接流、AppStateService 应用状态持久化、TaskbarService 任务栏控制、NotifyIconService 托盘图标管理。新增 TrayControlsWindow 托盘控制窗口及配套 MVVM 组件。新增 PlaybackControls、VolumeControls、TrackInfoControl、ProgressControls 等自定义控件。新增 UI/Themes 主题资源目录（Icons.xaml、Colors.xaml、Styles.xaml、Controls.xaml）。AudioLooper 核心已拆分为 Analysis/Loader/Mixing 分部类。新增 PlayPauseIconConverter 值转换器。

---

## 1. 核心定位

本项目是一个基于 **.NET Framework 4.8 (WPF)** 开发的高保真音乐播放器，核心特色是支持**无缝循环 (Seamless Looping)**，并集成了 **PyMusicLooper** 库进行自动化音频循环点分析。

**技术亮点**：
- **Prism.Unity 8.1.97** — MVVM 框架，区域导航与依赖注入
- **MaterialDesignThemes 5.3.1** — Material Design UI 风格
- **Repository 仓储模式** — 清晰的数据访问层分离
- **Prism EventAggregator** — 层间松耦合事件通信

## 2. 模块化设计

### 📂 Core (核心引擎层) — `seamless loop music/Core/`
这是项目的"心脏"，负责最底层的声音处理。
- `AudioLooper.cs`: 核心控制中心，处理播放状态（Play/Pause/Stop）以及精细的采样跳转逻辑。为了维护方便，已拆分为 `Analysis`（分析）、`Loader`（加载）和 `Mixing`（混音）等分部类。
- `LoopStream.cs`: 自定义的音频流包装类，确保在到达循环终点时能实现毫秒级的无感跳转。
- `ConcatenatedStream.cs`: 逻辑拼接流，按顺序连接两个 WaveStream，实现零内存损耗的无缝播放。

### 📂 Events (事件通信层) — `seamless loop music/Events/` — Prism EventAggregator
项目的"神经网络"，负责各层之间的消息传递。
- `CoreEvents.cs`: 定义全局事件（TrackLoadedEvent、PlaybackStateChangedEvent、LoopPointsChangedEvent、PlaylistChangedEvent、TrackMetadataChangedEvent、LibraryRefreshedEvent），用于 UI、Services、Core 之间的解耦通信。
- `CategoryItemSelectedEvent.cs`: 分类项选中事件，用于触发曲目列表按 Album / Artist / Playlist 过滤。

### 📂 Models (数据模型层) — `seamless loop music/Models/`
项目的"零件包"，定义了程序中流转的数据结构。
- `MusicTrack.cs`: 代表一首歌曲，包含采样率、开始/结束循环点、收藏状态、评分等核心数据信息。
- `PlaylistFolder.cs`: 代表一个播放列表。
- `LoopCandidate.cs`: 存储分析出来的潜在循环点数据。
- `PlayMode.cs`: 播放模式枚举（单曲循环、列表循环、随机播放等）。
- `CategoryItem.cs`: 分类项模型，支持 Album / Artist / Playlist 三种分类类型，包含封面图片路径（ImagePath）等扩展属性。
- `CategoryNavTarget.cs`: 分类导航目标模型，用于侧边栏导航菜单。
- `Album.cs`: 专辑数据模型，包含专辑名称、封面路径等信息。
- `Artist.cs`: 艺术家数据模型，包含艺术家名称、封面路径等信息。
- `UserRating.cs`: 用户评分模型，用于存储曲目评分（Rating）和收藏（IsLoved）状态。

### 📂 Data (持久化层) - Repository 仓储模式 — `seamless loop music/Data/`
项目的"档案室"，确保存档数据不丢失。
- `DatabaseHelper.cs`: 封装了 SQLite 的所有操作。包括 `MusicTracks` 表（存储循环参数）和 `Playlists` 表（存储播放列表结构）。
- `Repositories/BaseRepository.cs`: 仓储基类，提供通用 CRUD 操作。
- `Repositories/ITrackRepository.cs`: 曲目仓储接口，包含 `UpdateMetadataAsync` 方法。
- `Repositories/TrackRepository.cs`: 曲目仓储实现。
- `Repositories/IPlaylistRepository.cs`: 播放列表仓储接口。
- `Repositories/PlaylistRepository.cs`: 播放列表仓储实现。

### 📂 Services (业务逻辑层) — `seamless loop music/Services/`
项目的"参谋部"，处理各种复杂的逻辑运算。
- `PlayerService.cs`: 顶层业务管理器，协调 UI、音频引擎和数据库。
- `IPlayerService.cs`: 播放器服务抽象接口。
- `PlaybackService.cs`: 播放控制服务实现。
- `IPlaybackService.cs`: 播放控制服务接口（注意：与 `IPlayerService.cs` 冗余）。
- `PlaylistManagerService.cs`: 负责管理虚拟播放列表、拖拽排序以及歌曲的逻辑增删。
- `IPlaylistManagerService.cs`: 播放列表管理服务抽象接口。
- `PlaylistManager.cs`: 播放列表管理器实现。
- `IPlaylistManager.cs`: 播放列表管理器接口。
- `QueueManager.cs`: **保留** — 播放队列管理器（功能与 `PlaybackQueueService` 部分重叠）。
- `IQueueManager.cs`: **保留** — 队列管理器接口。
- `PyMusicLooperWrapper.cs`: 与 Python 库通信的桥梁，负责调用 `pymusiclooper` 进行循环点计算。
- `LoopAnalysisService.cs`: 自动化分析逻辑，处理后台扫描和候选点获取。
- `ILoopAnalysisService.cs`: 循环分析服务抽象接口。
- `LocalizationService.cs`: 全球化支持，动态切换中英文界面。
- `TrackMetadataService.cs`: 处理音频文件的元数据提取。
- `SearchService.cs`: 搜索服务，实现 0.4 秒防抖延迟的多关键词搜索。
- `ISearchService.cs`: 搜索服务接口。
- `PlaybackQueueService.cs`: 播放队列服务，负责管理播放列表、当前索引、播放模式以及切歌逻辑。
- `AppStateService.cs` / `IAppStateService.cs`: 应用状态持久化服务，保存/恢复音量、播放模式、分类上下文和最后播放曲目。
- `TaskbarService.cs` / `ITaskbarService.cs`: 任务栏控制服务，管理任务栏进度条和缩略图按钮。
- `NotifyIconService.cs` / `INotifyIconService.cs`: 托盘图标管理服务，提供系统托盘图标、右键菜单和左键弹出控制面板。

### 📂 UI (展示层) - Prism MVVM + Material Design — `seamless loop music/UI/`
项目的"门面"，负责与 **cpu 大人** 互动。

**窗口层**：
- `MainWindow.xaml`: 主控制中心。
- `LoopListWindow.xaml`: 专门用于显示和选择分析出来的多个循环点方案。
- `FolderManagerWindow.xaml`: 管理被扫描的音乐文件夹。
- `FolderPicker.cs`: 界面层的文件夹选择辅助工具。
- `TrayControlsWindow.xaml[.cs]`: 托盘控制窗口，提供桌面小控制器。

**视图层 (Views)**：
- `Views/LoopWorkspace.xaml[.cs]`: 循环点编辑工作区。
- `Views/PlaybackControlBar.xaml[.cs]`: 播放控制条。
- `Views/LibraryView.xaml[.cs]`: 音乐库视图（Prism 区域容器）。
- `Views/DetailView.xaml[.cs]`: 曲目详情视图。
- `Views/TrackListView.xaml[.cs]`: 曲目列表视图（支持分类过滤和搜索）。
- `Views/NowPlayingView.xaml[.cs]`: 当前播放视图（显示专辑封面、循环波形等）。
- `Views/InputDialog.xaml[.cs]`: 输入对话框。
- `Views/AddToPlaylistDialog.xaml[.cs]`: 添加到歌单对话框。
- `Views/SettingsWindow.xaml[.cs]`: 设置窗口。

**视图模型层 (ViewModels)** — Prism `INavigationAware` 支持：
- `ViewModels/LoopWorkspaceViewModel.cs`: 循环工作区的数据绑定与逻辑。
- `ViewModels/PlaybackControlBarViewModel.cs`: 播放控制条的 ViewModel。
- `ViewModels/LibraryViewModel.cs`: 音乐库视图的 ViewModel（Prism 区域导航）。
- `ViewModels/DetailViewModel.cs`: 曲目详情视图的 ViewModel。
- `ViewModels/TrackListViewModel.cs`: 曲目列表的 ViewModel（实现 `INavigationAware`，支持分类过滤、收藏、评分、防抖搜索）。
- `ViewModels/NowPlayingViewModel.cs`: 当前播放视图的 ViewModel。
- `ViewModels/TrayControlsViewModel.cs`: 托盘控制窗口的 ViewModel。

**控件与工具类**：
- `Controls/MultiSelectListBox.cs`: 多选列表框控件。
- `Controls/PlaybackControls.xaml[.cs]`: 播放控制自定义控件（播放/暂停/上一首/下一首）。
- `Controls/VolumeControls.xaml[.cs]`: 音量控制自定义控件（音量滑块/静音按钮）。
- `Controls/TrackInfoControl.xaml[.cs]`: 曲目信息自定义控件（封面/标题/艺术家）。
- `Controls/ProgressControls.xaml[.cs]`: 进度条自定义控件（播放进度/循环点标记）。
- `UI/Converters/VisibilityConverters.cs`: 值转换器（NullToVisibilityConverter、NullToVisibilityInverseConverter）。
- `UI/Converters/BindingProxy.cs`: 绑定代理器（用于 MultiBinding）。
- `UI/Converters/PlayPauseIconConverter.cs`: 播放/暂停图标状态转换器。

### 📂 App 入口 — `seamless loop music/`
- `App.xaml`: 定义全局样式（Material Design 暗色皮肤）。
- `App.xaml.cs`: 应用入口，实现**单实例互斥锁**（防止多开），全局异常捕获，已实例存在时自动唤醒置顶，**Prism UnityContainer 依赖注入配置**。

### 📂 测试项目 (SeamlessLoop.Tests) — `SeamlessLoop.Tests/`
- `SeamlessLoop.Tests.csproj`: 单元测试项目文件。
- `UnitTest1.cs`: 测试用例。

## 3. Prism 框架集成

### 依赖注入
- 所有服务接口通过 Prism `UnityContainer` 注册
- ViewModels 通过构造函数注入服务依赖
- 支持接口替换和单元测试Mock

### 区域导航
- `LibraryView` 作为区域容器（Region）
- `TrackListView` 和 `DetailView` 作为区域视图
- `TrackListViewModel` 实现 `INavigationAware` 接口处理导航生命周期

### 事件聚合器 (EventAggregator)
- `CategoryItemSelectedEvent`: 分类选中 → 触发曲目列表过滤
- `TrackMetadataChangedEvent`: 元数据变更 → 同步更新列表
- `TrackLoadedEvent`: 曲目加载完成
- `PlaybackStateChangedEvent`: 播放状态变更
- `LoopPointsChangedEvent`: 循环点变更
- `PlaylistChangedEvent`: 歌单结构变更
- `LibraryRefreshedEvent`: 音乐库刷新 → 通知侧边栏和列表刷新

## 4. 项目资产与资源
- `Properties/Resources.resx` / `Resources.zh-CN.resx`: 存储中英文多语言文本，配套 `Resources.Designer.cs` 自动生成访问类。
- `.venv`: 独立的 Python 运行环境（用于 PyMusicLooper）。
- `DEVELOPER_MANUAL.md`: 详细的开发者手册，记录了技术细节。
- `progress/`: 此目录记录了我们所有的开发进度和路线图。
- `UI/Themes/`: 主题资源目录，包含 `Icons.xaml`（图标库）、`Colors.xaml`（颜色定义）、`Styles.xaml`（全局样式）、`Controls.xaml`（控件模板）。

---

莱芙会继续努力维护好这个结构，确保它能始终如一地为 **cpu 大人** 提供流畅的使用体验！(๑>◡<๑)
