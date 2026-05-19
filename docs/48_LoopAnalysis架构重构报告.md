# LoopAnalysis 架构重构：移除 PyMusicLooper，全面切换原生 loopfinder.dll

**日期**: 2026-05-11

## 1. 重构动机

彻底移除 Python / PyMusicLooper / uvx 外部进程依赖，将所有循环点分析统一更换为原生 C++ loopfinder.dll（P/Invoke 调用），消除跨语言进程通信开销与环境安装复杂性。

## 2. 删除的文件

| 文件 | 原因 |
|------|------|
| `Services/PyMusicLooperWrapper.cs` | 移除 Python CLI 调用封装 |
| `Services/ILoopAnalysisBackend.cs` | 单一后端无需抽象接口 |

## 3. 变更文件清单

### 3.1 `Services/ILoopAnalysisService.cs`

移除 `SetCustomCachePath(string)` 和 `SetPyMusicLooperExecutablePath(string)`——这些仅用于 Python 后端路径配置，原生 DLL 不需要。

### 3.2 `Services/LoopAnalysisService.cs`

- 移除 `ILoopAnalysisBackend` 构造函数注入，改为直接实例化 `LoopFinder.Native`
- 移除 `SetCustomCachePath` / `SetPyMusicLooperExecutablePath`
- 类注释更新为「封装原生 loopfinder.dll 调用」

### 3.3 `Services/LoopFinder/Native.cs`

- 移除 `: ILoopAnalysisBackend` 接口实现
- 补充中文注释
- SEH/AV 异常边界保持不变

### 3.4 `App.xaml.cs`

移除 `RegisteSingleton<ILoopAnalysisBackend, PyMusicLooperWrapper>()`，`LoopAnalysisService` 恢复为单一注册。

### 3.5 `Services/IPlayerService.cs`

`CheckPyMusicLooperStatusAsync()` → `CheckAnalyzerStatusAsync()`

### 3.6 `Services/PlayerService.cs`

同步重命名方法实现。

### 3.7 `UI/ViewModels/LoopWorkspaceViewModel.cs`

- `EnsurePyMusicLooperReadyAsync` → `EnsureAnalyzerReadyAsync`
- 错误提示从「安装 uv / PyMusicLooper」改为「编译 loopfinder.dll」
- Debug 日志从 `[PyRanking失败]` 改为 `[分析失败]`
- `CheckPyMusicLooperStatusAsync` 调用改为 `CheckAnalyzerStatusAsync`

### 3.8 `Models/MusicTrack.cs`

注释从「PyMusicLooper 计算」改为「循环分析引擎计算」。

## 4. 最终架构

```
LoopAnalysisService (ILoopAnalysisService)
    │
    │  直接实例化
    ▼
LoopFinder.Native
    │
    │  P/Invoke [DllImport("loopfinder.dll")]
    ▼
loopfinder.dll  (C++, loopfinder 项目)
```

DI 注册仅一行：
```csharp
containerRegistry.RegisterSingleton<ILoopAnalysisService, LoopAnalysisService>();
```

`LoopAnalysisService` 构造时直接 `new LoopFinder.Native()`，无需外部注入。

## 5. CheckAnalyzerStatusAsync 返回值

| 值 | 含义 |
|----|------|
| 0 | `loopfinder.dll` 已加载，可直接分析 |
| 1 | DLL 缺失，提示用户编译原生库 |

不再有「需要下载 uv」的中间状态（原生 DLL 为预编译产物）。

## 6. 受影响的本地化资源

以下资源键仍存在于 `Resources.resx` 中，不影响编译和运行：

- `PyRanking` — 按钮文本
- `PyRankingToolTip` — 按钮提示

建议后续手动修改 `.resx` 中的中文值以移除「Py」前缀（保留键名不变可避免破坏 XAML 绑定）。

使用方法：loopfinder目录下

cmake -B build -A x64
cmake --build build --config Release
将生成的dll移入seamless loop music.exe同级目录下即可