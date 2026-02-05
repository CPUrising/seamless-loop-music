# ⚠️ 项目迁移公告 (Project Migration Notice)

**我决定弃坑这个 WPF 版本了！(╯°□°）╯︵ ┻━┻**

为了实现 **Windows + Android + iOS 三端制霸** 的宏伟目标，本项目将停止开发。
我们将带着这里的核心算法（无缝循环、波形智能匹配）转战 **Flutter**，开启全新的跨平台篇章。

新的传说，将在那里继续书写...

---

# Seamless Loop Music Player (无缝循环音乐播放器)

[![License: Ms-PL](https://img.shields.io/badge/License-Ms--PL-blue.svg)](https://opensource.org/licenses/MS-PL)
[![Logic](https://img.shields.io/badge/Logic-.NET%20Framework%204.8-purple.svg)](https://dotnet.microsoft.com/)

[English Version](README_EN.md) | [中文版](README.md)

专注于游戏音乐与环境音效的无缝循环播放工具。
内置 **“波形逆向回溯匹配算法” (Reverse Look-Behind Matching)**，一键实现毫秒级精度的无缝循环点自动对齐。

![Screenshot](docs/screenshot.png)
![1770044288697](image/README/1770044288697.png)

* **🎛️ 智能匹配 (Smart Match)**: 不需要手动去试那 0.01秒 的差别了。算法会自动分析波形，利用“声音指纹”技术，将循环点对齐到完美位置。
* **🧠 断点续忆**: 关掉软件也没关系，下次打开，它记得你听的是哪首歌（虽然它不会自作主张地吓你一跳自动播放）。

* **♾️ 物理无缝**: 基于底层流操控的无缝连接，欺骗声卡驱动，实现真正的 Zero-Gap Loop。
* **🔧 兼容性**: 降级至 .NET Framework 4.8，在 Windows 10/11 上无需安装额外运行库即可解压即用。
* **📂 歌单管理**: 支持文件夹导入，那是必须的。

## 🚀 快速开始

1. 前往 [Releases](https://github.com/CPUrising/seamless-loop-music/releases) 下载最新版本。
2. 解压并运行 `seamless loop music.exe`。
3. 点击“我的歌单”旁的 **“+”** 按钮导入包含 BGM 的文件夹，双击列表中的歌曲进行播放。
4. 填写帧数或秒数，调节微调按钮，按下“确认应用并试听”，跳转到循环终点前3s，粗略设置循环范围，然后点击 **“智能匹配”，**最后按下“确认应用并试听”
5. 原理是读取结尾前一秒的内容，比对start 前后2s的内容，取最相似位置改变start,end不变
6. 戴上耳机，见证无缝循环的奇迹。

## 📝 待办清单 (Roadmap)

我们致力于打造极致的无缝循环体验，以下是正在进行或计划中的改进：

### 🔴 优先修复 (High Priority)
- [ ] **稳定性增强**: 解决软件偶发崩溃问题，增加全局异常捕获机制。
- [ ] **消除爆音**: 深度优化跳转时的波形处理，彻底消除偶发的 "Click/Pop" 噪声。
- [ ] **单例模式**: 限制软件双开，防止音频输出冲突。
- [ ] **智能检测**: 增加对播放设备拔插（如拔出耳机）的检测，自动暂停播放。

### 🟡 体验升级 (UX Improvements)
- [ ] **可视化优化**: 优化时间轴与单位显示，让微调更直观。
- [ ] **列表体验**: 播放切换时自动定位滚动条到当前歌曲。
- [ ] **便捷导入**: 支持直接输入路径地址来添加歌单。
- [ ] **元数据编辑**: 支持在软件内修正歌曲显示名称。
- [ ] **算法升级**: 增强对短循环片段 (<1s) 的匹配精度，并支持正向 (Start -> End) 搜索。

### 🔵 远期规划 (Future)
- [ ] **AB Loop 支持**: 适配 Intro + Loop (AB段) 结构的循环模式。
- [ ] **高级筛选**: 支持根据游戏元数据进行列表过滤。

## 🕹️ 致敬与灵感 (Acknowledgement)

本项目灵感来源于 [**AokanaMusicPlayer**](https://github.com/melodicule/AokanaMusicPlayer)。
我们在开发初期借鉴了其部分基础架构，谨以最高的敬意感谢 @melodicule 的开源贡献！

在此基础上，我们研发了以下核心技术：

* **智能对齐算法**：引入 SAD (Sum of Absolute Differences) 互相关算法，实现了无需人工干预的自动波形匹配。
* **非破坏性预览**：全新的预览逻辑，只在内存中模拟跳转，不破坏原始播放流。

## 📜 许可证

本项目遵循 **Microsoft Public License (Ms-PL)** 协议开源。
这意味着您可以自由地使用、修改代码，但分发时必须保持开源并附带原协议，且不能以此起诉贡献者专利侵权。

---

*Created with ❤️ by cpu & Lev Zenith*
