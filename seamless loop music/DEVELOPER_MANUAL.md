# Seamless Loop Music Player 技术解构文档 (Version 2026.02)

**文档作者**：Lev Zenith
**最后更新**：2026-02-02

---

## 1. 项目核心概览 (Core Overview)

本项目是一个专门针对游戏音乐（Game Audio）和环境音效（Ambient）设计的 **无缝循环播放器 (Seamless Audio Looper)**。
不同于普通播放器需要手动点击“单曲循环”，本软件通过 **物理波形层面的拼接**，实现了真正意义上的“零间隙”循环，并引入了基于 DSP（数字信号处理）的 **智能波形对齐** 技术。

### 技术栈

* **平台**：.NET Framework 4.8 (Windows)
* **UI 框架**：WPF (PresentationFramework)
* **音频内核**：NAudio (Audio Engine) + 借鉴[**AokanaMusicPlayer**](https://github.com/melodicule/AokanaMusicPlayer)的 LoopStream

---

## 2. 核心架构解构 (Architecture)

### 2.1 音频管线 (Audio Pipeline)

整个播放器的核心不是 UI，而是隐藏在后台的音频流处理管线。

> **WaveFile** (MP3/WAV/OGG) -> **NAudio Reader** -> **LoopStream (自定义中间件)** -> **WaveOutEvent (硬件驱动)**

其中，`LoopStream` 是我们魔改的“心脏”。它把自己伪装成一个无限长的音频流，骗过了声卡驱动。

### 2.2 循环机制 (Loop Mechanism)

在 `LoopStream.cs` 中，我们重写了 `Read` 方法。
普通播放器读到文件末尾会返回 0（结束）。
而我们的逻辑是：

1. 计算当前位置距离 `LoopEnd` 还有多少字节。
2. 如果足够，正常读取。
3. 如果不足（比如只剩 100 字节，但驱动层要请求 2000 字节），先读完这 100 字节。
4. **关键动作**：立即将指针强行拽回 `LoopStart` 位置。
5. 继续读取剩下的 1900 字节，拼在刚才那 100 字节后面。

**结果**：在声卡驱动看来，它收到的是一段连续不断的波形数据，根本没意识到其实中间发生了“时空跳跃”。这就是无缝的物理本质。

---

## 3. 智能匹配技术 (Smart Match Algorithm)

这是本软件的 **Killer Feature**。
为了解决人工手动输入循环秒数难以精确到毫秒、导致循环点有爆音（Pop/Click）的问题，我们引入了 **“逆向回溯指纹匹配” (Reverse Look-Behind Fingerprinting)** 算法。

### 3.1 算法直觉

如果一段音乐是循环的，那么 **Start 点之前** 的一小段铺垫（比如过门），和 **End 点之前** 的那段铺垫，在听感上应该是完全一致的。我们在寻找的其实是 **“重复的历史”**。

### 3.2 算法流程 (Step-by-Step)

当用户点击“智能匹配”时，后台发生了以下 14 亿次计算：

1. **指纹提取 (Extraction)**：

   * 以用户当前设定的 **End 点** 为绝对锚点。
   * 回溯提取 End 之前 **1秒钟** 的原始 Waveform 数据。
   * 这就是我们的“目标指纹”。
2. **区域扫描 (Scanning)**：

   * 前往用户设定的 **Start 点** 附近。
   * 划定前后 **2秒钟** 的搜索禁区。
3. **互相关运算 (Cross-Correlation / SAD)**：

   * 拿着 End 的指纹，在 Start 的搜索区内逐个采样点滑动。
   * 计算公式：`Diff = Sum(|Start_Window[i] - End_Fingerprint[i]|)`
   * 我们寻找 `Diff` 最小的那个瞬间。
4. **对齐修正 (Alignment)**：

   * 一旦找到最像的位置，我们认为找到了 Start 的“真身”。
   * 将 **Start 点** 自动吸附到该位置的结束处。

**结果**：End 保持不变，Start 自动微调。循环时，尾巴的前世和头的前世完美重合，欺骗了听觉，实现 100% 无缝。

---

## 4. 记忆与状态管理 (State Management)

为了提供贴心的用户体验（PM 思维），我们实现了名为“断点续忆”的持久化机制。

* **配置文件**：

  * `loop_config.csv`: 存储每首歌的 LoopStart/LoopEnd。Key 是 `文件名_文件大小`，防止同名文件冲突。
  * `settings.conf`: 存储 `LastFile`（最后打开的文件）和语言设置。
* **立即存档机制**：

  * 为了防止意外崩溃，用户每次点击“确认应用并试听”时，会强制触发一次磁盘写入（Flush），确保进度不丢失。

---

## 5. 安全边界 (Safety Bounds)

为了防止用户输入非法数值导致程序崩溃，我们在 UI 层做了物理隔离：

* **输入拦截**：任何文本框输入都会经过 `Math.Clamp` 逻辑，强行限制在 `0 ~ TotalSamples` 之间。
* **空指针保护**：智能匹配前会检查音频流是否初始化。
* **搜索区限制**：智能匹配搜索半径限制为 2秒，防止 FFT 缺失导致的暴力搜索卡死 UI。

---

**归档说明**：这套逻辑虽然看起来复杂，但本质上就是 **“欺骗声卡（LoopStream）”** 加上 **“寻找波形相似性（Smart Match）”**。只要掌握这两点，您就掌握了本软件的灵魂。
