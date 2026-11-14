# 验证债台账

> 依据 `AGENTS.md` 第 5.4 节：`NOT RUN` 必须登记原因、是否阻断、何时补测。
> 状态：`未关闭` / `已关闭`；级别：`阻断` / `严重` / `一般`。

| 编号 | 来源变更 | 未执行的验证 | 级别 | 是否阻断归档 | 原因 | 计划补测 |
|---|---|---|---|---|---|---|
| VD-0002-01 | 0002-loopfinder-unicode-path | OGG 宽路径解码（`_wfopen_s` + `stb_vorbis_decode_memory`） | 一般 | 否 | ~~手头无 OGG 样本~~ 已用 `D:\FFOutput\music_09\まめ知識.ogg` 补测 | **已关闭**：宽入口日文路径 `ret=10`，与 ASCII 副本宽/窄入口结果一致（`start=384528 end=1442827 score=0.917`），见 [verify 日志](file:///d:/seamless-loop-music/seamless%20loop%20music/artifacts/verify-0002-ogg-20261113-105859.log) |
| VD-0002-02 | 0002-loopfinder-unicode-path | FLAC 宽路径解码（`drflac_open_file_w`） | 一般 | 否 | 手头无 FLAC 样本 | 找到 FLAC 样本后补测中文路径 |
| VD-0002-03 | 0002-loopfinder-unicode-path | T04 旧 DLL（缺 `lf_analyze_file_w`）时的运行时提示 | 一般 | 否 | 未构造旧 DLL 运行场景，仅静态检查代码 | 用旧版 DLL 替换输出目录后启动应用确认提示文案 |
| VD-0002-04 | 0002-loopfinder-unicode-path | WPF 界面手工冒烟（打开中文路径曲目 → 自动寻环） | 严重 | 否 | ~~需人类在图形界面操作~~ 人类已于 2026-11-13 完成 | **已关闭**：人工见证通过，未留存截图/日志（证据强度弱于自动化用例） |
