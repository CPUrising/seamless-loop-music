# 更改计划

## 需求描述
CPU大人希望移除界面中受到挤压几乎看不见的收藏按钮（Love Button），只保留Rating星级评分功能。同时将"我的收藏"歌单修改为"Rating歌单"，并按照星星数量（Rating）进行排列显示。

## 需要了解的信息
- 当前项目的相关文件：
  - `UI/Views/TrackListView.xaml` - 曲目列表视图，包含收藏按钮和Rating控件的XAML定义
  - `UI/ViewModels/TrackListViewModel.cs` - 曲目列表视图模型，包含 ToggleLoveCommand 和评分相关逻辑
  - `UI/ViewModels/LibraryViewModel.cs` - 库视图模型，负责创建侧边栏分类项（包括"我的收藏"）
  - `Models/MusicTrack.cs` - 音乐曲目模型，包含 IsLoved 和 Rating 属性
  - `Properties/Resources.resx` - 英文资源文件，包含 PlaylistFavorites 等本地化字符串
  - `Properties/Resources.zh-CN.resx` - 中文资源文件，包含"我的收藏"等本地化字符串
  - `Services/PlaybackService.cs` - 播放服务，包含播放列表相关的业务逻辑
  - `Data/Repositories/TrackRepository.cs` - 曲目仓储，包含数据库操作
  - `Data/Repositories/ITrackRepository.cs` - 曲目仓储接口

## 当前项目状态
- 相关功能的现有代码情况：
  - TrackListView.xaml 第45行定义了收藏按钮列（Width="35"），第60-79行实现了收藏按钮，包含 IconLove/IconLoved 图标切换
  - TrackListViewModel.cs 第137行声明了 ToggleLoveCommand，第169行初始化，第371-383行实现了 OnToggleLove 方法
  - LibraryViewModel.cs 第279行创建了"我的收藏"分类项（Id = -2, Name = loc["PlaylistFavorites"], Icon = "❤️"）
  - TrackListViewModel.cs 第323-325行的 TracksFilter 方法中，对 Id == -2 的分类项进行过滤，只显示 IsLoved = true 的曲目
  - 数据库 UserRatings 表中同时包含 IsLoved（布尔值）和 Rating（整数0-5）两个字段
  - Resources.resx 第220-222行定义 PlaylistFavorites = "My Favorites"
  - Resources.zh-CN.resx 第220-222行定义 PlaylistFavorites = "我的收藏"

- 可复用的逻辑：
  - Rating 控件已经存在于 TrackListView.xaml 第93-101行，只需移除收藏按钮即可
  - RateCommand 已经在 TrackListViewModel.cs 中实现（第138行声明，第170行初始化，第xxx行实现 OnRateTrack）
  - 可以按 Rating 排序的逻辑可以参考现有的排序实现

## 可选实现方案

- 方案A：完全移除 IsLoved 功能
  - 描述：从数据库、模型、ViewModel、View 中完全移除 IsLoved 相关代码，只保留 Rating 功能。"Rating歌单"显示所有 Rating > 0 的曲目，按 Rating 降序排列。
  - 优点：代码更简洁，逻辑更清晰，完全符合 CPU 大人"只要 rating"的要求
  - 缺点：如果用户已经使用了收藏功能，升级后会丢失收藏状态（但 Rating 数据保留）

- 方案B：保留 IsLoved 但隐藏界面按钮
  - 描述：保留数据库和模型中的 IsLoved 字段，但不在界面显示收藏按钮。"Rating歌单"改为按 Rating 排序。
  - 优点：保留数据兼容性，万一以后需要可以恢复
  - 缺点：代码冗余，IsLoved 字段无人使用，可能造成困惑

## 实现计划
1. 修改 `UI/Views/TrackListView.xaml`：
   - 移除第45行的收藏按钮列定义（`<ColumnDefinition Width="35"/>`）
   - 移除第60-79行的收藏按钮 XAML 代码
   - 调整 Play 按钮的列索引（从 Grid.Column="0" 保持不变）
   - 调整 Title 列的定义（移除收藏列后，原来 Grid.Column="2" 的 Title 改为 Grid.Column="1"）
   - 后续列的 Grid.Column 值依次减1

2. 修改 `UI/ViewModels/TrackListViewModel.cs`：
   - 移除 ToggleLoveCommand 的声明（第137行）
   - 移除构造函数中的初始化（第169行）
   - 移除 OnToggleLove 方法（第371-383行）
   - 修改 TracksFilter 方法中针对 _selectedCategoryItem.Id == -2 的过滤逻辑（第323-325行），改为：
     - 过滤出 Rating > 0 的曲目
     - 按 Rating 降序排列（需要实现排序逻辑）

3. 修改 `UI/ViewModels/LibraryViewModel.cs`：
   - 第279行将"我的收藏"改为"Rating歌单"
   - 修改图标（可选，从 "❤️" 改为 "⭐" 或其他代表Rating的图标）

4. 修改资源文件：
   - `Properties/Resources.resx`：将 PlaylistFavorites 的值从 "My Favorites" 改为 "Rating Playlist" 或 "Top Rated"
   - `Properties/Resources.zh-CN.resx`：将 PlaylistFavorites 的值从 "我的收藏" 改为 "Rating歌单" 或 "星级歌单"

5. 清理相关代码（可选，按方案A）：
   - 从 MusicTrack.cs 中移除 IsLoved 属性和相关字段
   - 从 TrackRepository.cs 中移除 GetLovedTracksAsync 方法和相关 IsLoved 的数据库操作
   - 从 ITrackRepository.cs 中移除相关接口定义
   - 从 DatabaseHelper.cs 中移除 UserRatings 表的 IsLoved 字段（或保留但不使用）
   - 移除 Resources.resx 和 Resources.zh-CN.resx 中的 MenuLove、MenuUnlove 字符串

6. 实现按 Rating 排序：
   - 在 TrackListViewModel 中添加排序逻辑，当显示"Rating歌单"时按 Rating 降序排列
   - 可以考虑在 TracksView 的 SortDescriptions 中添加排序规则

## 确认结果（CPU大人已确认）
- ✅ 采用方案A：完全移除 IsLoved 功能（从数据库、模型、ViewModel、View 全部移除）
- ✅ "Rating歌单"只显示 Rating > 0 的曲目
- ✅ 右键菜单中的"❤️ Favorite"/"💔 Unfavorite"选项也需要移除
- ✅ 新增需求：在主界面左下角的专辑图标旁边增加 Rating 按钮

## 需要额外了解的信息
- 主界面文件：`UI/Views/MainWindow.xaml` - 需要找到左下角专辑图标位置并添加Rating按钮
- NowPlaying视图：`UI/Views/NowPlayingView.xaml` - 可能包含左下角的专辑封面显示

## 实现计划（更新版）

### 第一阶段：移除 IsLoved 相关功能

1. 修改 `UI/Views/TrackListView.xaml`：
   - 移除第45行的收藏按钮列定义（`<ColumnDefinition Width="35"/>`）
   - 移除第60-79行的收藏按钮 XAML 代码
   - 调整后续列的 Grid.Column 值依次减1
   - 移除右键菜单中的收藏选项（第251-258行附近的 MenuLove/MenuUnlove）

2. 修改 `UI/ViewModels/TrackListViewModel.cs`：
   - 移除 ToggleLoveCommand 的声明（第137行）
   - 移除构造函数中的初始化（第169行）
   - 移除 OnToggleLove 方法（第371-383行）
   - 修改 TracksFilter 方法中针对 _selectedCategoryItem.Id == -2 的过滤逻辑（第323-325行）：
     - 改为过滤出 Rating > 0 的曲目
     - 添加按 Rating 降序排列的逻辑

3. 修改 `Models/MusicTrack.cs`：
   - 移除第20行的 `_isLoved` 字段
   - 移除第76-79行的 `IsLoved` 属性

4. 修改 `Data/Repositories/TrackRepository.cs`：
   - 移除 GetLovedTracksAsync 方法（第284-290行）
   - 移除所有 SQL 查询中的 IsLoved 字段
   - 移除 UpdateMetadataAsync(int id, bool isLoved, int rating) 方法中的 isLoved 参数
   - 清理 DatabaseHelper.cs 中相关的 IsLoved 操作

5. 修改 `Data/Repositories/ITrackRepository.cs`：
   - 移除 `Task<List<MusicTrack>> GetLovedTracksAsync();` 接口定义（第19行）
   - 更新 `Task UpdateMetadataAsync(int id, bool isLoved, int rating);` 签名，移除 isLoved 参数

6. 修改 `Services/PlaybackService.cs`：
   - 移除第283-285行中对 GetLovedTracksAsync 的调用
   - 更新为使用 Rating > 0 的过滤逻辑

7. 修改资源文件：
   - `Properties/Resources.resx`：
     - 将 PlaylistFavorites 的值从 "My Favorites" 改为 "Rating Playlist"
     - 移除 MenuLove、MenuUnlove 字符串（第277-282行）
   - `Properties/Resources.zh-CN.resx`：
     - 将 PlaylistFavorites 的值从 "我的收藏" 改为 "Rating歌单"
     - 移除 MenuLove、MenuUnlove 字符串（第277-282行）

8. 修改 `UI/ViewModels/LibraryViewModel.cs`：
   - 第279行：将 Name = loc["PlaylistFavorites"] 保持不变（资源文件已改）
   - 修改图标：从 "❤️" 改为 "⭐"

### 第二阶段：添加主界面 Rating 按钮

9. 修改 `UI/Views/PlaybackControlBar.xaml`：
   - 找到第71行的 TrackInfoControl（左下角专辑封面区域）
   - 在 TrackInfoControl 旁边（右侧）添加 Rating 按钮
   - 可以使用星级图标 ⭐ 或 Material Design 的 Star图标
   - XAML示例：
     ```xml
     <StackPanel Orientation="Horizontal" HorizontalAlignment="Left">
         <controls:TrackInfoControl .../>
         <Button Command="{Binding ShowRatingPlaylistCommand}"
                 Style="{StaticResource ControlButtonStyle}"
                 Margin="10,0,0,0"
                 ToolTip="Rating 歌单">
             <md:PackIcon Kind="Star" Width="22" Height="22"/>
         </Button>
     </StackPanel>
     ```
   - 注意：需要修改第71行的布局，将 TrackInfoControl 和新的 Rating 按钮放在同一个容器中

10. 修改 `UI/ViewModels/PlaybackControlBarViewModel.cs`：
    - 添加 ShowRatingPlaylistCommand 命令
    - 命令实现：通过 EventAggregator 发布事件或直接使用 RegionManager 导航到 LibraryView 并选中 Rating 歌单
    - 或者发布一个自定义的 CategoryItemSelectedEvent，Id = -2 (Rating歌单的ID)

### 第三阶段：按 Rating 排序

11. 在 TrackListViewModel 中实现排序：
    - 当显示"Rating歌单"（Id == -2）时，按 Rating 降序排列
    - 可以在 TracksView 的 SortDescriptions 中添加：`TracksView.SortDescriptions.Add(new SortDescription("Rating", ListSortDirection.Descending));`

## 预期结果
- TrackListView 中的收藏按钮完全移除，界面更简洁，Rating 控件更加突出
- 侧边栏的"我的收藏"改为"Rating歌单"，图标改为 ⭐
- 点击"Rating歌单"后，只显示 Rating > 0 的曲目，按 Rating 星级从高到低排列
- 用户只能通过点击 Rating 控件（1-5星）来标记喜欢的曲目，不再有单独的收藏按钮
- 界面布局更加合理，不会再出现按钮被挤压看不见的问题
- 主界面左下角专辑图标旁边新增 Rating 按钮，方便快速访问评分功能
- IsLoved 相关代码完全清理，项目代码更简洁易懂
