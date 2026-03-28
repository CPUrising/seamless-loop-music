# Seamless Loop Music 数据存储机制调研报告

本项目的数据存储由 **SQLite (Dapper)** 与 **TagLib#** 共同驱动，通过“指纹识别”实现跨会话的循环点自动恢复。

## 1. 存储核心：SQLite 数据库
- **数据库路径**: `%AppData%\Local\SeamlessLoopMusic\library.db`
- **连接管理**: [DatabaseHelper.cs](file:///d:/seamless%20loop%20music/seamless%20loop%20music/seamless%20loop%20music/Data/DatabaseHelper.cs)
- **表结构定义**: `DatabaseHelper.InitializeDatabase()` 方法 (L63-L111)
    - `LoopPoints`: 歌曲元数据、循环点、PyMusicLooper 分析结果。
    - `Playlists` & `PlaylistItems`: 播放列表及关联关系。
    - `PlaylistFolders`: 扫描目录配置。
- **数据模型**: [MusicTrack.cs](file:///d:/seamless%20loop%20music/seamless%20loop%20music/seamless%20loop%20music/Models/MusicTrack.cs) (L10-L77)

## 2. 歌曲信息的读取与识别 (Identification)
- **底层仓储**: [TrackRepository.cs](file:///d:/seamless%20loop%20music/seamless%20loop%20music/seamless%20loop%20music/Data/Repositories/TrackRepository.cs)
- **唯一识别逻辑**: `GetByFingerprint(fileName, totalSamples)` (L31-L42)
    - 以文件名 + 采样总数作为联合识别码。
- **元数据服务**: [TrackMetadataService.cs](file:///d:/seamless%20loop%20music/seamless%20loop%20music/seamless%20loop%20music/Services/TrackMetadataService.cs)
    - `GetOrUpdateTrackMetadata()` (L51-L101): 负责根据读取的音频流去库里“寻亲”，如果找到了就恢复之前的所有设置，找不到就创建新的。

## 3. 音乐元数据的提取 (Metadata Extraction)
- **技术实现**: `TagLib.File.Create(filePath)`
- **核心逻辑**: `TrackMetadataService.FillMetadataFromFile()` (L103-L129)
    - 这里莱芙发现它会抓取 `Artist`, `Album`, `AlbumArtist` 和 `Title`。
    - **显示名称**: 如果数据库没有记录且标签中有 `Title`，莱芙会优先把它作为 `DisplayName`。

## 4. 循环点设置的记录与恢复 (Loop Points)
- **记录恢复**: `TrackMetadataService.GetOrUpdateTrackMetadata()` 中的 L77-L90。
- **手动保存**: [TrackRepository.cs](file:///d:/seamless%20loop%20music/seamless%20loop%20music/seamless%20loop%20music/Data/Repositories/TrackRepository.cs) 中的 `Save()` (L44-L94) 会执行 SQL 的 `UPDATE` 或 `INSERT`。
- **A/B 段自动识别**: [TrackMetadataService.cs](file:///d:/seamless%20loop%20music/seamless%20loop%20music/seamless%20loop%20music/Services/TrackMetadataService.cs) 的 `FindPartB()` (L22-L46)。
    - 根据后缀名（`_A`/`_B` 等）自动探测。
- **分析结果持久化**: `LoopCandidatesJson` 字段。莱芙在 [PyMusicLooperWrapper.cs](file:///d:/seamless%20loop%20music/seamless%20loop%20music/seamless%20loop%20music/Services/PyMusicLooperWrapper.cs) 或 `TrackRepository` 中看到它会将所有候选点转成 JSON 存起来。

## 5. 典型数据流向 (Workflow)
1. **音频引擎初始化**: [AudioLooper.Loader.cs](file:///d:/seamless%20loop%20music/seamless%20loop%20music/seamless%20loop%20music/Core/AudioLooper.Loader.cs)
2. **元数据介入**: `TrackMetadataService` 探测 A/B 段或检索数据库。
3. **仓储层执行**: `TrackRepository` 发起 SQLite 查询。
4. **实时保存**: 界面（如 [DetailViewModel.cs](file:///d:/seamless%20loop%20music/seamless%20loop%20music/seamless%20loop%20music/UI/ViewModels/DetailViewModel.cs)）更新循环点时触发保存。
