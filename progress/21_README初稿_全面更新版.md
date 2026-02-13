# Seamless Loop Music Player (无缝循环音乐播放器)

[![License: Ms-PL](https://img.shields.io/badge/License-Ms--PL-blue.svg)](https://opensource.org/licenses/MS-PL)
[![Framework](https://img.shields.io/badge/.NET%20Framework-4.8-purple.svg)](https://dotnet.microsoft.com/)
[![Database](https://img.shields.io/badge/Database-SQLite-green.svg)](https://www.sqlite.org/)

一款专为游戏 BGM，如galgame,RPG游戏 和环境音效设计，如白噪音的无缝循环播放与高效管理工具。通过自研算法与工业级引擎双驱动，实现采样级别的极致循环对齐。

目前支持win10及以上的操作系统

---

## 🛠️ 核心功能

### 1. 智能循环对齐 (Smart & Deep Match)

- **手动智能匹配**：支持“寻找起点” (Reverse) 与“寻找终点” (Forward) 两种模式，完美适配带 Intro 的 OST 音轨。
  寻找起点：以当前循环终点的前一秒为指纹，在当前循环起点的前后共10秒寻找匹配程度最高处，将此处更新为循环起点
  寻找终点：以当前循环起点的后一秒为指纹，在当前循环终点的前后共10秒寻找匹配程度最高处，将此处更新为循环终点
- **自动智能匹配**：集成业界领先的极致匹配引擎PyMusicLooper，使得可以一键批量匹配循环点（需要用户先自行安装）

### 2. A/B 拼接模式 (A/B Looping)

- **双文件合体**：支持将两个独立的音轨（如 Intro.wav + Loop.wav，01_a.ogg+01_b.ogg，02_A.mp3+02_B.mp3）逻辑拼接为单曲播放，适用于白色相簿2、流星世界演绎者的游戏原始BGM匹配
- 用户可以自己设置合并后的循环起始点，同时可以通过“恢复AB接缝”恢复AB的原始循环

3.支持列表播放与循环播放，同时可以修改loop来确定循环几次进入下一首歌

### 4. 持久化数据管理 (Safe & Portable)

- **指纹识别系统**：基于 `(文件名 + 总采样数)` 的音频指纹技术。即使移动文件位置，其别名、循环配置也能自动找回。
- **工业级后端**：基于 SQLite + Dapper 架构，开启 WAL 并发模式，支持大规模曲库的高速读写与管理。
- **别名系统**：支持在不修改物理文件名的前提下，在 UI 界面自定义显示名称。

### 5. 现代交互体验 (UX & Interaction)

- **原生拖拽重排**：支持在歌单和曲目列表中直接拖拽调整顺序，排序结果实时持久化到数据库。
- **批量管理引擎**：支持多选歌单执行一键刷新、批量极致匹配及删除操作。
- **智能边缘滚动**：长列表拖拽时自动感知边界并自动滚动，操作顺滑。

---

## 🚀 技术栈

- **音频后端**：NAudio (基于环形缓冲区的无缝流技术)
- **数据存储**：SQLite + Dapper (ORM)
- **界面框架**：WPF (支持 UI 虚拟化与动态列表重绘)
- **匹配算法**：时域互相关 (自研) + PyMusicLooper (集成)

---

## 📖 使用指南

1. **导入**：点击 `+` 号添加歌单，一种只能通过添加删除文件夹管理，另一种只能通过添加删除单个系统会自动扫描并建立指纹映射。
2. 手工匹配：
   **微调**：在主界面输入或按钮修改得到粗略采样点或时间，利用“寻找起点/终点”进行局部相位对齐，通过“确认并试听”跳转到循环终点前3秒比对是否无缝
   **极致匹配**：对复杂音轨，右键点击“极致匹配”，让引擎自动为你寻找最佳路径。
3. 自动匹配：
4. **管理**：右键左侧的歌单或歌曲进行相应管理操作，删除，重命名，添加歌单，通过拖拽排列你喜欢的播放顺序，
5. 歌单分为两类：一种只能通过添加删除文件夹管理，另一种只能通过添加删除单个（或歌单里批量选中的）歌曲管理
6. 批量操作：同windows多选文件的操作，即CTRL选个别，CTRL+A列表内全选，shift选范围

---

🕹️ 致敬

本项目最初的灵感与开发动力来源于 [melodicule/AokanaMusicPlayer: 苍彼音乐无缝播放/Play Aokana&#39;s BGM seamless](https://github.com/melodicule/AokanaMusicPlayer)，其中循环相关代码对我们后期AB段的处理有很大的启发
我们在其基础上进行了扩展与增强，使得具有更加广泛的应用

本项目的批量极致匹配由[arkrow/PyMusicLooper: A python program for repeating music endlessly and creating seamless music loops, with play/export/tagging support.](https://github.com/arkrow/PyMusicLooper)提供支持，请大家给这个仓库一个star

---

## 📜 许可证

本项目尊敬[melodicule/AokanaMusicPlayer: 苍彼音乐无缝播放/Play Aokana&#39;s BGM seamless](https://github.com/melodicule/AokanaMusicPlayer)的开源贡献，根据其协议遵循 **Microsoft Public License (Ms-PL)** 协议。
