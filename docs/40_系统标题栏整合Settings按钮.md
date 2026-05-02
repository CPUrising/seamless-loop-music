# 更改计划

## 需求描述
将 Settings 按钮从主界面顶部栏（第37-39行）移动到窗口右上角的系统标题栏区域，与最小化、最大化、关闭按钮放在一起，使界面更简洁。

## 需要了解的信息
- 当前项目的相关文件：
  - `seamless loop music/UI/MainWindow.xaml` — 主界面布局，当前 Settings 按钮在第38行
  - `seamless loop music/UI/MainWindow.xaml.cs` — 主窗口后台代码，包含 BtnSettings_Click 事件处理
  - `seamless loop music/UI/Views/SettingsWindow.xaml` — 设置窗口界面
  - `seamless loop music/App.xaml` — 全局样式资源
  - `seamless loop music/UI/Themes/Controls.xaml` — 自定义控件样式

## 当前项目状态
- 当前 MainWindow 使用的是默认 Window 样式（WindowStyle 未设置，使用系统标题栏）
- 顶部栏（Row 0）有一个独立的 Border 区域，仅放置 Settings 按钮右对齐
- Settings 按钮通过 `BtnSettings_Click` 事件打开 SettingsWindow，并传入必要的服务依赖
- 系统标题栏区域目前无法自定义添加按钮（Windows 默认标题栏不支持直接嵌入 WPF 控件）

## 可复用的逻辑
- `BtnSettings_Click` 中的依赖注入逻辑可以直接复用
- SettingsWindow 的创建和显示逻辑无需修改
- 可以将顶部栏移除，节省界面空间

## 实现方案

**方案A：自定义标题栏（已确认）**

描述：设置 `WindowStyle="None"`，自己实现标题栏，在右上角放置 Settings + 最小化 + 最大化 + 关闭按钮

特点：
- ✅ CPU 大人确认：只需要按钮，不需要图标和标题
- ✅ CPU 大人确认：需要鼠标悬停时显示不同颜色/动画效果
- 完全自定义，保持 Material Design 风格一致
- 精确控制按钮位置和样式

## 实现计划

1. 修改 `MainWindow.xaml`，设置 `WindowStyle="None"`，添加自定义窗口样式（阴影、圆角等）
2. 在 MainWindow 的 Grid 最上层添加自定义标题栏，只包含右侧按钮区域
3. 添加三个按钮：Settings（齿轮图标）、最小化、最大化/还原、关闭
4. 修改 `MainWindow.xaml.cs`：
   - 添加窗口拖拽逻辑（鼠标左键拖拽标题栏区域）
   - 添加双击最大化/还原逻辑
   - 添加窗口控制按钮事件处理（Minimize、Maximize、Close、Settings）
5. 在 `UI/Themes/Controls.xaml` 或 `Styles.xaml` 中添加标题栏按钮样式：
   - 默认状态：透明背景，浅色图标
   - 悬停状态：半透明背景，高亮图标，添加平滑过渡动画
   - 关闭按钮悬停：红色背景，白色图标（警示效果）
6. 删除原有顶部栏（Row 0，第37-39行）
7. 确保 Settings 按钮点击调用原有 `BtnSettings_Click` 逻辑
8. 测试窗口行为：拖拽、最大最小化、关闭、设置按钮功能

## 预期结果
- 主界面顶部不再有独立的 Settings 栏，界面更简洁
- Settings 按钮出现在窗口右上角，与最小化/最大化/关闭按钮在同一区域
- 点击 Settings 按钮依然能正常打开设置窗口
- 窗口的拖拽、最大最小化等原生行为保持不变
- 整体风格与 Material Design 暗色主题保持一致
