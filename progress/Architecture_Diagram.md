# Dopamine 项目架构图 (2026-03-25)

本文件由 **莱芙・泽诺 (Lev Zenith)** 维护，记录 Dopamine 播放器的完整架构设计与模块依赖关系。

---

## 1. 整体架构全景图 (The Big Picture)

> **设计理念**: 分层架构 + MVVM 模式 + Prism 依赖注入

```mermaid
graph TD
    %% 展示层：WPF UI
    subgraph UI_Layer ["🎨 Presentation Layer (WPF + Prism)"]
        direction TB
        
        subgraph Shell ["Shell (Main Window)"]
            direction LR
            TopBar["顶部工具栏\nLogo / 菜单 / 窗口控制"]
            MainContent["主内容区\n(三栏布局)"]
            BottomBar["底部状态栏\n频谱 / 播放控制"]
        end
        
        subgraph Views ["Views (XAML)"]
            FV["FullPlayerView\n完整播放器"]
            MV["MiniPlayerView\n迷你播放器"]
            NP["NowPlayingView\n正在播放"]
            CV["CollectionViews\n收藏/设置/信息"]
        end
        
        subgraph ViewModels ["ViewModels"]
            MainVM["MainViewModel"]
            PlayerVM["PlayerViewModel"]
            CollectionVM["CollectionViewModels"]
        end
    end

    %% 服务层
    subgraph Service_Layer ["⚙️ Service Layer (Dopamine.Services)"]
        direction LR
        PS["IPlaybackService\n播放控制"]
        NS["INotificationService\n系统通知"]
        MS["IMetadataService\n元数据"]
        IS["IIndexingService\n音乐索引"]
        ES["IEqualizerService\n均衡器"]
        CS["ICollectionService\n收藏管理"]
        SS["ISearchService\n搜索"]
        DS["IDiscordService\nDiscord"]
    end

    %% 核心层
    subgraph Core_Layer ["🛡️ Core Layer (Dopamine.Core)"]
        direction TB
        
        subgraph Audio ["Audio Engine"]
            CSP["CSCorePlayer\n核心播放器"]
            EQ["EqualizerPreset\n均衡器预设"]
            SP["ISpectrumPlayer\n频谱分析"]
        end
        
        subgraph Api ["External APIs"]
            LFM["LastFm API"]
            LYR["Lyrics API"]
            FAN["Fanart API"]
        end
        
        subgraph IO ["IO & Utils"]
            PLD["PlaylistDecoder\n播放列表"]
            FU["FormatUtils\n格式化"]
        end
    end

    %% 数据层
    subgraph Data_Layer ["💾 Data Layer (Dopamine.Data)"]
        direction TB
        SQLite[("SQLite Database")]
        TR["TrackRepository"]
        FR["FolderRepository"]
        PR["PlaylistRepository"]
        Entities["Entities\nTrack / Folder / Blacklist"]
    end

    %% 依赖注入容器
    DI(("Prism DI\nContainer"))

    %% 连接关系
    UI_Layer -- "依赖注入" --> DI
    DI -- "解析服务" --> Service_Layer
    Service_Layer -- "调用" --> Core_Layer
    Service_Layer -- "持久化" --> Data_Layer
    
    %% 视觉美化
    style UI_Layer fill:#e3f2fd,stroke:#2196f3,stroke-width:2px
    style Service_Layer fill:#fff3e0,stroke:#ff9800,stroke-width:2px
    style Core_Layer fill:#e8f5e9,stroke:#4caf50,stroke-width:2px
    style Data_Layer fill:#fce4ec,stroke:#f06292,stroke-width:2px
    style DI fill:#f3e5f5,stroke:#9c27b0,stroke-dasharray: 5 5
```

---

## 2. 音频播放数据流 (Audio Playback Flow)

> **核心路径**: 用户操作 → 服务层 → 音频引擎 → 输出

```mermaid
graph LR
    User(("用户"))
    
    subgraph UI ["UI Layer"]
        PC["播放按钮\nPlayCommand"]
    end
    
    subgraph Service ["Service Layer"]
        PB["IPlaybackService\nPlay()"]
        QM["QueueManager\n队列管理"]
    end
    
    subgraph Core ["Core Layer"]
        CSP["CSCorePlayer\n播放引擎"]
        FF["FFmpeg\n解码器"]
        WAS["WASAPI\n音频输出"]
    end
    
    subgraph Feedback ["反馈通知"]
        NS["NotificationService\nToast通知"]
        DS["DiscordService\n状态同步"]
        TB["TaskbarService\n任务栏进度"]
    end
    
    User -- "点击" --> PC
    PC -- "调用" --> PB
    PB -- "管理" --> QM
    PB -- "驱动" --> CSP
    CSP -- "解码" --> FF
    CSP -- "输出" --> WAS
    
    PB -. "事件通知" .-> NS
    PB -. "状态同步" .-> DS
    PB -. "进度更新" .-> TB
    
    style UI fill:#e3f2fd,stroke:#2196f3
    style Service fill:#fff3e0,stroke:#ff9800
    style Core fill:#e8f5e9,stroke:#4caf50
    style Feedback fill:#f3e5f5,stroke:#9c27b0
```

---

## 3. UI 三栏布局结构 (Three-Pane Layout)

> **设计原则**: 左侧筛选 → 中间详情 → 右侧扩展

```mermaid
graph TB
    subgraph MainWindow ["MainWindow (Shell.xaml)"]
        direction TB
        
        subgraph TopBar ["顶部工具栏"]
            Logo["Logo / 返回按钮"]
            Menu["主菜单\nCollection / Settings / Info"]
            SysCtrl["系统设置 / 窗口控制"]
        end
        
        subgraph ThreePane ["三栏主内容区"]
            direction LR
            LeftPane["左栏\nPrimaryFilter\n(艺术家/专辑/流派)"]
            MiddlePane["中栏\nMainContent\n(曲目列表)"]
            RightPane["右栏\nExtension\n(歌词/信息)"]
        end
        
        subgraph BottomBar ["底部状态栏"]
            Spectrum["频谱分析器"]
            PlayCtrl["播放控制\n(上一曲/播放/下一曲)"]
            Progress["进度条"]
        end
        
        TopBar --> ThreePane
        ThreePane --> BottomBar
    end
    
    style TopBar fill:#e3f2fd,stroke:#2196f3,stroke-width:2px
    style ThreePane fill:#fff3e0,stroke:#ff9800,stroke-width:2px
    style BottomBar fill:#e8f5e9,stroke:#4caf50,stroke-width:2px
```

---

## 4. 依赖注入注册图 (DI Registration)

> **关键服务**: 所有服务在 `Initializer.cs` 中注册为单例

```mermaid
graph TB
    subgraph Container ["Prism DI Container"]
        direction LR
        
        subgraph CoreServices ["核心服务"]
            PS["IPlaybackService\n→ PlaybackService"]
            MS["IMetadataService\n→ MetadataService"]
            IS["IIndexingService\n→ IndexingService"]
        end
        
        subgraph UIServices ["UI 相关服务"]
            NS["INotificationService\n→ NotificationService"]
            TS["ITaskbarService\n→ TaskbarService"]
            AS["IAppearanceService\n→ AppearanceService"]
        end
        
        subgraph ExternalServices ["外部集成"]
            DS["IDiscordService\n→ DiscordService"]
            UPS["IUpdateService\n→ UpdateService"]
            LFS["ILastFmService\n→ LastFmService"]
        end
        
        subgraph DataServices ["数据服务"]
            CS["ICollectionService\n→ CollectionService"]
            SS["ISearchService\n→ SearchService"]
            PLS["IPlaylistService\n→ PlaylistService"]
        end
    end
    
    style CoreServices fill:#e3f2fd,stroke:#2196f3
    style UIServices fill:#fff3e0,stroke:#ff9800
    style ExternalServices fill:#e8f5e9,stroke:#4caf50
    style DataServices fill:#fce4ec,stroke:#f06292
```

---

## 5. 数据库实体关系 (Database ER Diagram)

```mermaid
erDiagram
    TRACK ||--o{ METADATA : has
    TRACK {
        int TrackId PK
        string Path
        string Title
        string Artist
        string Album
        int Duration
    }
    
    FOLDER ||--o{ TRACK : contains
    FOLDER {
        int FolderId PK
        string Path
        string Name
    }
    
    PLAYLIST ||--o{ TRACK : includes
    PLAYLIST {
        int PlaylistId PK
        string Name
        string Type
    }
    
    BLACKLIST_TRACK {
        int BlacklistTrackId PK
        string Path
    }
    
    METADATA {
        int MetadataId PK
        int TrackId FK
        string Key
        string Value
    }
```

---

## 6. 模块依赖关系 (Module Dependencies)

```mermaid
graph BT
    Tests["Dopamine.Tests"]
    
    subgraph Main ["Dopamine (主程序)"]
        UI["Views + ViewModels"]
    end
    
    subgraph Services ["Dopamine.Services"]
        Srv["所有服务实现"]
    end
    
    subgraph Core ["Dopamine.Core"]
        Audio["音频引擎"]
        API["外部API"]
    end
    
    subgraph Data ["Dopamine.Data"]
        Repo["Repositories"]
        DB[("SQLite")]
    end
    
    Tests -.-> Main
    Tests -.-> Services
    Tests -.-> Core
    
    Main --> Services
    Services --> Core
    Services --> Data
    Core --> Data
    
    style Main fill:#e3f2fd,stroke:#2196f3,stroke-width:2px
    style Services fill:#fff3e0,stroke:#ff9800,stroke-width:2px
    style Core fill:#e8f5e9,stroke:#4caf50,stroke-width:2px
    style Data fill:#fce4ec,stroke:#f06292,stroke-width:2px
    style Tests fill:#f3e5f5,stroke:#9c27b0,stroke-dasharray: 5 5
```

---

## 7. 关键技术栈

| 层级          | 技术                     | 用途           |
| :---------- | :--------------------- | :----------- |
| **UI 框架**   | WPF + XAML             | 界面渲染         |
| **MVVM 框架** | Prism                  | 依赖注入、导航、事件   |
| **音频引擎**    | CSCore                 | 音频解码与播放      |
| **音频解码**    | FFmpeg (CSCore.Ffmpeg) | 多格式支持        |
| **音频输出**    | WASAPI / DirectSound   | Windows 音频输出 |
| **数据库**     | SQLite                 | 数据持久化        |
| **ORM**     | SQLite-net-pcl         | 数据库操作        |
| **序列化**     | Newtonsoft.Json        | JSON 处理      |
| **外部集成**    | DiscordRPC             | Discord 状态   |

---

*本文档基于源码分析生成，最后更新: 2026-03-25*
