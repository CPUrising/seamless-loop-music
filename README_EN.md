# 🎵 Seamless Loop Music Player

> **Born for Game Music.**
> A lightweight local music player for Windows, designed to solve the "gapless loop" issue inherent in standard media players when playing game BGMs or ambient tracks.

[中文文档](README.md) | **English**

---

## ✨ Key Features

*   **♾️ True Gapless Loop**: Implements sample-accurate seamless looping based on low-level `IWaveProvider` stream processing. No more clicks or gaps when the track loops.
*   **🎯 Precision Control**: Supports custom **Loop Start Sample**, accurate to the single frame, perfectly recreating the in-game looping experience.
*   **📂 Multi-Format Support**: Supports common game audio formats including **WAV**, **OGG**, and **MP3**.
*   **🚀 Lightweight & portable**: Built on .NET 8. No installation required (via self-contained build). Just download and play.

---

## 🎮 Use Cases

*   **Game Development / QA**: Quickly verify if audio loop points are set correctly.
*   **OST Appreciation**: Immersively listen to game soundtracks (e.g., RPG town themes, battle music) specifically designed with loop structures, without breaking the flow.
*   **Ambience & Focus**: Infinite looping of rain sounds, white noise, or focus music for work and meditation.

---

## 🛠️ User Guide

1.  **Launch**: Run `seamless loop music.exe`.
2.  **Import**: Click the **"Select File"** (选择音频文件) button to load your audio track.
3.  **Set Loop Point**:
    *   Enter a value in the **"Loop Start Sample"** (循环起始采样数) box.
    *   *Tip: Use `0` to loop from the very beginning.*
    *   *Note: Many game tracks contain loop metadata, or you can manually input the sample number (e.g., 1024000).*
4.  **Play**: Click **"Play"** (播放) and enjoy the infinite loop!
5.  **Volume**: Adjust the slider to control the volume.

---

## 🏗️ Tech Stack

*   **Platform**: Windows (WinForms)
*   **Framework**: .NET 8.0
*   **Core Library**: [NAudio](https://github.com/naudio/NAudio)

---

## 📅 Roadmap

We are continuously improving the experience. Planned features include:

### 🔹 Experience Improvements
- [ ] Optimize UI layout.
- [ ] Add Pause/Resume functionality.
- [ ] Display total samples and current progress.

### 🔹 Core Features
- [ ] **Loop Memory**: Auto-save and load the last used loop points for tracks.
- [ ] **Loop End Point**: Support setting a specific "Loop End" sample.
- [ ] **Visual Progress Bar**: Drag to seek.
- [ ] **Fine-tuning**: +/- 1 frame buttons for precise loop point adjustment.

### 🔹 Advanced
- [ ] **Playlist Support**: Manage multiple tracks.
- [ ] **Waveform Visualization**: Visual aid for finding loop points.
- [ ] **Metadata Reading**: Auto-detect loop tags in wav/ogg headers.

---

## 🤝 Acknowledgements

This project was inspired by [AokanaMusicPlayer](https://github.com/melodicule/AokanaMusicPlayer), specifically regarding the logic for seamless loop streams.

## 📄 License

**Microsoft Public License (Ms-PL)**.
This project contains code logic derived from AokanaMusicPlayer and adheres to its original Ms-PL license.
