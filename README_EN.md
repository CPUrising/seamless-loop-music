# 🎵 Seamless Loop Music Player

> **Born for Game Music.**
> A lightweight local music player for Windows, designed to solve the "gapless loop" issue inherent in standard media players when playing game BGMs or ambient tracks.

[中文文档](README.md) | **English**

---

## ✨ Key Features

*   **♾️ True Seamless Looping**: Based on `IWaveProvider` for sample-accurate looping, eliminating any gaps or clicks.
* **📂 Batch Management**: Support **folder import** with recursive scanning to automatically generate a playlist.
* **💾 Config Persistence**: Automatically remembers loop points for each track (using filename + duration fingerprint) and reloads them next time.
* **🎯 Arbitrary Loop Range**: Custom **Start** and **End** points with a **"Confirm Settings"** button for hot updates.
* **📊 Visual Control**: Intuitive progress bar, real-time time display, and track navigation (Prev/Next).
* **🚀 Lightweight**: Built on .NET 8, available as a standalone executable.

---

## 🎮 Use Cases

* **Game Dev/Testing**: Quickly verify loop points for massive audio assets.
* **BGM Appreciation**: Immerse yourself in game music loops without interruptions.
* **Ambience**: Create a white noise playlist for endless looping.

---

## 🛠️ User Guide

1. **Import Music**:
   * Click **"Import Music Folder"** to select a directory.
   * The software will scan and populate the playlist on the right.
2. **Playback Control**:
   * Double-click a track in the list to play.
   * Use **[<< Prev]** / **[Next >>]** buttons to navigate.
3. **Set Loop Points**:
   * Enter **Loop Start/End Samples**.
   * The **"Confirm Settings"** button lights up upon changes.
   * Click **"Confirm Settings"** (or just hit Play) to apply and save changes instantly.
4. **Volume**: Adjust the slider at the bottom.

---

## 🏗️ Tech Stack

*   **Platform**: Windows (WinForms)
*   **Framework**: .NET 8.0
*   **Core Lib**: [NAudio](https://github.com/naudio/NAudio)

---

## 📅 Roadmap

### 🔹 Completed

- [x] **Core Playback**: Play/Pause/Stop, Seamless Loop Stream
- [x] **Loop Range**: Custom Start/End, Hot Update
- [x] **Batch Management**: Folder Import, Playlist, Navigation
- [x] **Persistence**: CSV Storage, Config Auto-load
- [x] **UX**: Confirm Button, Drag-to-Seek, Expanded UI

### 🔹 Planned

- [ ] **Fine-tuning**: +/- 1 frame buttons for precise loop point adjustment.
- [ ] **Waveform Visualization**: Visual aid for finding loop points.
- [ ] **Metadata Reading**: Auto-detect loop tags in wav/ogg headers.

---

## 🤝 Acknowledgements

This project was inspired by [AokanaMusicPlayer](https://github.com/melodicule/AokanaMusicPlayer), specifically regarding the logic for seamless loop streams.

## 📄 License

**Microsoft Public License (Ms-PL)**.
This project contains code logic derived from AokanaMusicPlayer and adheres to its original Ms-PL license.
