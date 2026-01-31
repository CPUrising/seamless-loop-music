# 🎵 Seamless Loop Music Player

> **Born for Game Music.**
> A lightweight local music player for Windows, designed to solve the "gapless loop" issue inherent in standard media players when playing game BGMs or ambient tracks.

[中文文档](README.md) | **English**

---

## ✨ Key Features

*   **♾️ True Gapless Loop**: Implements sample-accurate seamless looping based on low-level `IWaveProvider` stream processing. No more clicks or gaps when the track loops.
*   **🎯 Arbitrary Loop Range**: Supports custom **Loop Start** and **Loop End** points, accurate to the single frame, allowing for infinite looping of any specific section.
*   **📊 Visual Control**: Provides an intuitive progress bar with drag-to-seek support, click-to-jump, and real-time precise time display.
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
3.  **Set Loop Points**:
    *   The software automatically detects and fills in the total sample count.
    *   **Loop Start Sample**: The track will jump back to this point after reaching the end point (default: 0).
    *   **Loop End Sample**: The track will loop when it reaches this point (default: total samples).
    *   *Simply modify these two values to define any loop range.*
4.  **Playback Control**:
    *   Click **"Play"** (播放) to start looping.
    *   Supports **"Pause"** (暂停) / **"Resume"**.
    *   Drag the progress bar to quickly seek.
5.  **Volume**: Adjust the slider to control the volume.

---

## 🏗️ Tech Stack

*   **Platform**: Windows (WinForms)
*   **Framework**: .NET 8.0
*   **Core Library**: [NAudio](https://github.com/naudio/NAudio)

---

## 📅 Roadmap

We are continuously improving the experience. Planned features include:

### 🔹 Completed

- [x] **Basic Control**: Play, Pause, Stop, Volume.
- [x] **Seamless Core**: `IWaveProvider`-based loop stream.
- [x] **Interval Loop**: Custom Loop Start and Loop End support.
- [x] **Visual Progress**: Progress bar, drag-to-seek, time display.
- [x] **UI Optimization**: Improved layout and spacing.
- [x] **Info Display**: Real-time sample rate and total sample count.
- [x] **Auto-Fill**: Automatically sets default loop range on load.

### 🔹 Planned

- [ ] **Loop Memory**: Auto-save and load the last used loop points for tracks.
- [ ] **Fine-tuning**: +/- 1 frame buttons for precise loop point adjustment.
- [ ] **Playlist Support**: Manage multiple tracks.
- [ ] **Waveform Visualization**: Visual aid for finding loop points.
- [ ] **Metadata Reading**: Auto-detect loop tags in wav/ogg headers.

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
