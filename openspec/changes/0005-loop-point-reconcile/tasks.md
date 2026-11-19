# Tasks

> 共 4 项，低于 `AGENTS.md` 第 4.1 节上限。

## 1. 实现

- [ ] 1.1 新增 `Services/LoopPointReconciler.cs`（纯函数，含 1000 采样容差）
- [ ] 1.2 `Services/PlaybackService.cs` 非 A/B 分支接入校正并写回 `LoopPoints`
- [ ] 1.3 新增 `SeamlessLoop.Tests/LoopPointReconcilerTests.cs`

## 2. 验证

- [ ] 2.1 `dotnet build` + 单测；用问题文件人工确认可连续播放到结尾；回填 `docs/verify/0005-*.md`

## 完成定义（DoD）

- 问题曲目不再在 13:47 跳回开头；
- 用户自定义循环点不被改写（单测覆盖）；
- 人工播放验证未完成前不得标记 PASS。
