# LoopAnalysis 架构重构：多后端分析与原生 DLL 集成

**日期**: 2026-05-11
**版本**: v2.x

## 1. 背景与动机

原有循环分析架构存在以下局限：

*   **强耦合**：`LoopAnalysisService` 直接 `new PyMusicLooperWrapper()`，无法替换分析引擎。
*   **无原生后端**：项目 `loopfinder/` 目录已包含完整的 C++ 重写版算法（带 HPSS 增强），但未接入 WPF 端。
*   **缺少自动部署**：即使接入原生 DLL，也缺少自动构建与输出部署流程。

本次重构目标：

```
重构前:
  LoopAnalysisService ──▶ PyMusicLooperWrapper ──▶ pymusiclooper CLI

重构后:
  LoopAnalysisService (ILoopAnalysisService)
         │
    ILoopAnalysisBackend
         │                    │
  PyMusicLooperWrapper    LoopFinder.Native
         │                    │
  pymusiclooper CLI      loopfinder.dll (P/Invoke)
```

## 2. 新增文件

### 2.1 `Services/ILoopAnalysisBackend.cs`

分析后端统一契约，定义三个核心方法 + 一个状态事件：

```csharp
public interface ILoopAnalysisBackend
{
    event Action<string> OnStatusMessage;
    Task<int> CheckEnvironmentAsync();
    Task<(long Start, long End, double Score)?> FindBestLoopAsync(string filePath);
    Task<List<LoopCandidate>> FetchTopLoopCandidatesAsync(string filePath);
}
```

与 `ILoopAnalysisService` 的区别：不包含序列化、路径配置等方法，仅定义纯分析能力。

### 2.2 `Services/LoopFinder/Native.cs`

原生 `loopfinder.dll` 的 P/Invoke 封装，实现 `ILoopAnalysisBackend`。

**C API 映射**（参见 `loopfinder/include/loopfinder/loopfinder_api.h`）：

```c
typedef struct { int64_t loopStart, loopEnd; float noteDiff, loudnessDiff, score; } lf_loop_point_t;
int  lf_analyze_file(const char* filepath, int topN, lf_loop_point_t* outPoints, int capacity);
const char* lf_get_last_error();
```

**关键设计点**：

| 机制 | 说明 |
|------|------|
| 静态构造器探测 | 类型加载时仅调用轻量级 `lf_get_last_error()` 验证 DLL 可用性，不触发音频解码 |
| 同步 `CheckEnvironmentAsync` | 直接返回静态缓存结果（`DllPresent` + `DllError`），避免每调用一次都做进程探测 |
| SEH/AV 保护 | `AnalyzeNative()` 附加 `[HandleProcessCorruptedStateExceptions]`，隔离捕获 `AccessViolationException` / `SEHException` |
| 调用链隔离 | `AnalyzeAsync` → `Task.Run` → `AnalyzeNative`（独立具名方法），确保异常不会因 lambda 委托边界丢失属性 |
| ExactSpelling | `DllImport` 均设置 `ExactSpelling=true`，禁用 CharSet 自动后缀探测 |
| 返回码语义 | `>0` = 找到 N 个点 / `0` = 未找到 / `<0` = 错误（调用 `lf_get_last_error` 获取详情） |

## 3. 修改文件

### 3.1 `Services/PyMusicLooperWrapper.cs`

仅一行修改：类声明追加 `: ILoopAnalysisBackend`。现有方法签名与接口完全一致，无需任何方法体变更。

### 3.2 `Services/LoopAnalysisService.cs`

**构造函数变化**：

```diff
- public LoopAnalysisService(IEventAggregator eventAggregator)
+ public LoopAnalysisService(ILoopAnalysisBackend backend, IEventAggregator eventAggregator)
```

*   不再直接 `new PyMusicLooperWrapper()`，改为通过 DI 接收 `ILoopAnalysisBackend`。
*   `SetCustomCachePath` / `SetPyMusicLooperExecutablePath` 使用模式匹配委托到 `PyMusicLooperWrapper`。
*   序列化/反序列化方法（`SerializeLoopCandidates` / `DeserializeLoopCandidates`）保持不变。
*   事件转发逻辑（`OnStatusMessage` → `StatusMessageEvent`，含 UI 线程调度）保持不变。

### 3.3 `App.xaml.cs`

新增一行 DI 注册：

```csharp
containerRegistry.RegisterSingleton<ILoopAnalysisBackend, PyMusicLooperWrapper>();
```

**切换后端**只需改动此行为：
```csharp
// 使用原生 DLL
containerRegistry.RegisterSingleton<ILoopAnalysisBackend, LoopFinder.Native>();
```

### 3.4 `seamless loop music.csproj`

#### 3.4.1 CMake 构建集成（`CheckNativeBuild` + `BuildLoopFinder` target）

*   **条件执行**：仅当 `loopfinder.dll` 不存在且 CMake 可用时触发。
*   **平台检测**：默认 `-A x64`。
*   **双生成器兼容**：同时覆盖 VS generator（`build/$(Configuration)/`）和 Ninja（`build/`）的输出路径。
*   **跳过开关**：`msbuild /p:SkipNativeBuild=true` 可完全跳过。

#### 3.4.2 DLL 自动部署

两个 `<Content>` 项，覆盖 Ninja 和 VS 两种输出结构：
```xml
<Content Include="..." CopyToOutputDirectory="PreserveNewest" Link="loopfinder.dll" />
```

`PreserveNewest` 策略：仅当 DLL 比输出目录中的副本更新时才复制，首次构建跳过（不报错）。

### 3.5 `loopfinder/CMakeLists.txt`

*   **修复优化标志**：原代码无条件 `/O2`（所有配置），现改为 Debug `/Od /Zi`、Release `/O2`。
*   **静态 CRT 链接**：`set(CMAKE_MSVC_RUNTIME_LIBRARY "MultiThreaded$<$<CONFIG:Debug>:Debug>")`，避免部署时依赖 VC++ 可再发行组件。
*   **配置宏定义**：Debug 注入 `_DEBUG`，Release 注入 `NDEBUG`。

## 4. 异常边界设计（Native）

`LoopFinder.Native` 的异常处理覆盖完整的 P/Invoke 故障面：

| 异常类型 | 捕获位置 | 行为 |
|----------|---------|------|
| `DllNotFoundException` | 静态构造器 + `AnalyzeNative` | 缓存错误，后续调用直接返回空 |
| `BadImageFormatException` | 静态构造器 | 记录架构不匹配（x86/x64） |
| `EntryPointNotFoundException` | 静态构造器 | 记录 API 版本不匹配 |
| `AccessViolationException` | `AnalyzeNative` | 在独立线程上捕获，上报错误消息 |
| `SEHException` | `AnalyzeNative` | 上报结构化异常码 |
| `Exception`（通用） | 全部热点 | 兜底日志 |

**关键原则**：所有原生异常均在 `Task.Run` 内的独立线程上处理，不会逃逸到 DI/Prism/UI 层，确保分析失败不会拖垮播放器。

## 5. 构建与部署流程

```
┌──────────────────────────────────────────────────┐
│  msbuild /t:Build (首次或 DLL 缺失时)             │
│                                                   │
│  1. CheckNativeBuild                              │
│     ├─ 检查 loopfinder.dll 是否存在               │
│     ├─ 检测 CMake 是否可用                        │
│     └─ 输出诊断消息                               │
│                                                   │
│  2. BuildLoopFinder (条件触发)                     │
│     ├─ cmake -S loopfinder/ -B loopfinder/build/  │
│     │   -A x64                                    │
│     └─ cmake --build loopfinder/build/            │
│         --config $(Configuration)                 │
│                                                   │
│  3. C# 编译                                       │
│                                                   │
│  4. 输出部署                                       │
│     └─ Copy loopfinder.dll → bin/$(Config)/       │
└──────────────────────────────────────────────────┘
```

*   后续增量构建：DLL 已存在 → 跳过 CMake 步骤，仅 C# 编译。
*   CMake 未安装：输出提示信息，跳过原生构建，`LoopFinder.Native` 报告 `DLL not found`。

## 6. 兼容性说明

*   **`ILoopAnalysisService` 公有 API 不变**：`PlayerService`、`LoopWorkspaceViewModel` 等消费者无需任何修改。
*   **序列化格式不变**：`SerializeLoopCandidates` / `DeserializeLoopCandidates` 输出格式与原有完全兼容，数据库中已缓存的 JSON 数据无需迁移。
*   **PyMusicLooper 行为不变**：`PyMusicLooperWrapper` 仅加了接口声明，所有进程调用逻辑完全保留。
*   **原生后端为可选依赖**：默认注册 `PyMusicLooperWrapper`，应用在不部署 `loopfinder.dll` 时运行不受影响。
