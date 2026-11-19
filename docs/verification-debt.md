# 验证债台账

> 依据 `AGENTS.md` 第 5.4 节：`NOT RUN` 必须登记原因、是否阻断、何时补测。
> 状态：`未关闭` / `已关闭`；级别：`阻断` / `严重` / `一般`。

| 编号 | 来源变更 | 未执行的验证 | 级别 | 是否阻断归档 | 原因 | 计划补测 |
|---|---|---|---|---|---|---|
| VD-0002-01 | 0002-loopfinder-unicode-path | OGG 宽路径解码（`_wfopen_s` + `stb_vorbis_decode_memory`） | 一般 | 否 | ~~手头无 OGG 样本~~ 已用 `D:\FFOutput\music_09\まめ知識.ogg` 补测 | **已关闭**：宽入口日文路径 `ret=10`，与 ASCII 副本宽/窄入口结果一致（`start=384528 end=1442827 score=0.917`），见 [verify 日志](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0002-ogg-20261113-105859.log) |
| VD-0002-02 | 0002-loopfinder-unicode-path | FLAC 宽路径解码（`drflac_open_file_w`） | 一般 | 否 | 手头无 FLAC 样本 | 找到 FLAC 样本后补测中文路径 |
| VD-0002-03 | 0002-loopfinder-unicode-path | T04 旧 DLL（缺 `lf_analyze_file_w`）时的运行时提示 | 一般 | 否 | 未构造旧 DLL 运行场景，仅静态检查代码 | 用旧版 DLL 替换输出目录后启动应用确认提示文案 |
| VD-0002-04 | 0002-loopfinder-unicode-path | WPF 界面手工冒烟（打开中文路径曲目 → 自动寻环） | 严重 | 否 | ~~需人类在图形界面操作~~ 人类已于 2026-11-13 完成 | **已关闭**：人工见证通过，未留存截图/日志（证据强度弱于自动化用例） |
| VD-0003-01 | 0003-music-folder-picker-library-multiselect | 选择器人工冒烟：①设置页选中「音乐」库并添加 ②Ctrl 多选两个目录 ③添加父目录替换子目录 ④重复添加提示 ⑤选中「此电脑」提示 ⑥引导页添加并扫描 | 严重 | 否 | ~~需要图形界面交互~~ 人类已于 2026-11-14 完成 | **已关闭**：人工见证全部通过，未留存截图 |
| VD-0003-02 | 0003-music-folder-picker-library-multiselect | 全量单测中 `PlaybackStatisticsAndroidWpfInteropTests` 的 2 个 fixture 缺失失败 | 一般 | 否 | 既有问题：`Fixtures/Sync/*` 未复制到测试输出目录；已用 stash 基线复跑确认与本变更无关 | 另开变更修复测试工程 fixture 复制 |
| VD-0004-01 | 0004-audio-stream-container-fallback | 常规输出目录 `dotnet build` | — | 否 | ~~被运行中的播放器锁定~~ 关闭播放器后已重跑 | **已关闭**：0 错误，exe 于 2026-11-15 10:13:36 更新 |
| VD-0004-02 | 0004-audio-stream-container-fallback | 真实 `.m4a` / `.wma` / `.aiff` / `.flac` 样本的工厂创建 | 一般 | 否 | 手头无对应容器样本（M4A 内容已通过 `.mp3` 扩展名等价覆盖） | 找到样本后补测 |
| VD-0004-03 | 0004-audio-stream-container-fallback | WPF 内实际播放 `cet6_2024_06_2.mp3` 并设置循环点 | 严重 | 否 | ~~需要图形界面操作与重新构建~~ 人类已于 2026-11-16 完成 | **已关闭**：人工播放确认正常，未留存截图 |
| VD-0006-01 | 0006-music-classification | WPF 界面手工冒烟：侧边栏「音乐分类」→ 选文件夹 → 开始分析 → 列表展示 → 四象限统计 | 严重 | 否 | 需人类在图形界面操作；服务层与规则层已由自动化覆盖 | 人类在 WPF 中确认后关闭 |
| VD-0006-02 | 0006-music-classification | T07 故障注入：用不含 `lf_classify_file_w` 的旧版 DLL 验证报错文案 | 一般 | 否 | 未构造旧 DLL 运行场景，仅静态探针检查 | 用旧版 DLL 替换输出目录后启动应用确认提示文案 |
| VD-0006-03 | 0006-music-classification | FLAC / OGG 格式的分类特征提取实测 | 一般 | 否 | 测试曲库仅含 MP3 | 找到 FLAC/OGG 样本后补测 |
| VD-0006-04 | 0006-music-classification | 低置信阈值调参（30 首中 26 首 `IsUncertain=True`） | 一般 | 否 | 属算法特性，非缺陷；阈值集中可配置 | 另开变更调参并复验分布 |
