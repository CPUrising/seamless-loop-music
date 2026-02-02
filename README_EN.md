# Seamless Loop Music Player

[![License: Ms-PL](https://img.shields.io/badge/License-Ms--PL-blue.svg)](https://opensource.org/licenses/MS-PL)
[![Logic](https://img.shields.io/badge/Logic-.NET%20Framework%204.8-purple.svg)](https://dotnet.microsoft.com/)

[中文版](README.md) | [English Version](README_EN.md)

A specialized tool designed for seamless looping of game music and ambient tracks.
Featuring the built-in **"Reverse Look-Behind Matching Algorithm"**, it achieves millisecond-precision auto-alignment for loop points with a single click.

![Screenshot](docs/screenshot.png)
![1770042833764](image/README/1770042833764.png)

## ✨ Key Features

* **🎛️ Smart Match**: Forget manual tweaking of 0.01s. Our algorithm analyzes waveforms using unique "Audio Fingerprinting" to align loop points with sub-atomic precision.
* **🧠 Memory Recall**: Close the app anytime. Upon reopening, it remembers your last track (and politely waits for you to press play).
* **♾️ True Seamless**: Achieves Zero-Gap Looping by manipulating the underlying audio stream, fooling the sound driver into perceiving a continuous wave.
* **🔧 Compatibility**: Downgraded to .NET Framework 4.8 for maximum compatibility on Windows 10/11 without extra runtime installations.
* **📂 Playlist Management**: Folder import support included.

## 🚀 Quick Start

1. Download the latest [Release](https://github.com/yourusername/seamless-loop-music/releases).
2. Unzip and run `seamless loop music.exe`.
3. Drag and drop your game BGM files (.mp3, .wav, .ogg supported).
4. Roughly set an End point, then click **"Smart Match"**.
5. Put on your headphones and enjoy the silky smooth transition.

## 🕹️ Acknowledgement & Inspiration

This project is inspired by [**AokanaMusicPlayer**](https://github.com/melodicule/AokanaMusicPlayer).
We gratefully acknowledge the foundational architecture provided by @melodicule's open-source work.

Building upon that foundation, we have **independently developed** the following core technologies:

* **Smart Alignment Algorithm**: Implements SAD (Sum of Absolute Differences) cross-correlation logic for automated waveform matching without human intervention.
* **Non-Destructive Preview**: A new preview logic that simulates jumps in memory without disrupting the original playback stream.

## 📜 License

This project is open-sourced under the **Microsoft Public License (Ms-PL)**.
This allows you to freely use and modify the code, provided that you distribute it under the same license and include a copy of the original agreement.

---

*Created with ❤️ by cpu & Lev Zenith*
