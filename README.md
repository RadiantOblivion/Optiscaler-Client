# ✦ OptiScaler Client

[![GitHub Release](https://img.shields.io/github/v/release/RadiantOblivion/Optiscaler-Client?style=flat-square&color=8A2BE2)](https://github.com/RadiantOblivion/Optiscaler-Client/releases/tag/v1.5.1)
[![License: GPL-3.0-or-later](https://img.shields.io/badge/License-GPL--3.0--or--later-yellow.svg?style=flat-square)](LICENSE)
[![Platform: Windows](https://img.shields.io/badge/Platform-Windows-0078D4?style=flat-square&logo=windows)](https://www.microsoft.com/windows)
[![Platform: Linux](https://img.shields.io/badge/Platform-Linux-FCC624?style=flat-square&logo=linux)](https://www.linux.org)

> **⚠️ Disclaimer:** This is **not** an official OptiScaler project. I am not affiliated with the OptiScaler team. This is a personal project developed without any commercial purpose. Anyone is free to try and use this software at their own risk.

**OptiScaler Client** is a modern, high-performance cross-platform desktop utility designed to simplify the installation, management, and update of the **OptiScaler** mod across your entire game library. Built with **C#** and **Avalonia UI**, it provides a premium, native experience on both **Windows** and **Linux** (including Steam Deck).

---

## Screenshots

* Main window

<img width="1266" height="752" alt="1 0 3_A" src="https://github.com/user-attachments/assets/09b0706a-e047-485c-9ac7-0847d80a3fc5" />

* Game management

<img width="1266" height="751" alt="1 0 3_B" src="https://github.com/user-attachments/assets/b4c86a32-13df-4ae2-921e-b89aacb7dfa1" />

* Main window after installation

<img width="1266" height="752" alt="1 0 3_C" src="https://github.com/user-attachments/assets/e88307c8-13af-4401-90a0-a6daaf8a4977" />

---

## 🚀 Key Features

*   **Auto-Scanner**: Deeply scans Steam, Epic Games, GOG, EA, Ubisoft, Battle.net, and Xbox libraries to find your games instantly.
*   **One-Click Install**: Automatically downloads and configures the latest OptiScaler versions for specific titles.
*   **Deep-Clean Uninstall**: Uses local installation manifests to ensure 100% clean and safe removal of all OptiScaler components without touching native game files.
*   **Cross-Platform Support**: Full compatibility with **Linux (Steam Deck / Desktop)** and **Windows 10/11**.
*   **Component Control**: Manage additional tools like **Fakenvapi** (for AMD/Intel GPUs), **Nukem's DLSSG-to-FSR3** mod, and **FSR 4 INT8** injection for non-RDNA 4 GPUs.
*   **Advanced Injection**: Support for various injection methods including `dxgi.dll`, `version.dll`, and `winmm.dll`.
*   **Localization**: Full support for multiple languages (English, Spanish & Brazilian Portuguese).
*   **Native Performance**: Lightweight, self-contained executable for zero-footprint installation.

---

## 📖 Usage Guide

Follow these simple steps to enhance your games:

1.  **Find your games**: Click **"Scan Games"** to automatically detect installed titles. You can also manage scan sources or add custom folders in **Settings**. Use **"Add Manually"** for standalone or DRM-free titles.
2.  **Select a Game**: Click the **"Manage"** button next to any game to enter the management dashboard.
3.  **Install OptiScaler**: Choose your desired version and click **"Auto Install"**. If the game uses a non-standard structure (like some UE5 titles), the client will intelligently detect the correct folder or allow for **"Manual Install"**.
4.  **Launch & Tweak**: Start your game normally. Once inside, press the **`Insert`** key to open the OptiScaler menu and fine-tune your upscaling settings in real-time.

---

## 🛠️ Installation & Requirements

### 🪟 Windows
1.  Download the latest `OptiscalerClient-windows-x64.zip` from the [Releases](https://github.com/RadiantOblivion/Optiscaler-Client/Releases) page.
2.  Extract and run `OptiscalerClient.exe`.
3.  **Requirements**: Windows 10/11. The app is self-contained (no .NET runtime installation required).

### 🐧 Linux

#### Steam Deck / Ubuntu / Fedora
1.  Download the latest `OptiscalerClient-linux-x64.tar.gz` from the [Releases](https://github.com/RadiantOblivion/Optiscaler-Client/Releases) page.
2.  Extract the contents to a folder.
3.  Run `OptiscalerClient`. (On Steam Deck, you can add it as a Non-Steam Game for easy access). \
OR
4. Run `chmod +x install.sh && ./install.sh` to install the app to your applications menu.

#### Arch
1.  Download the latest `OptiscalerClient-arch-x64.tar.gz` from the [Releases](https://github.com/RadiantOblivion/Optiscaler-Client/Releases) page.
2.  Extract PKGBUILD.
3.  Run `makepkg -si`.

---

## 🤝 Contributing

We welcome contributions! If you'd like to improve OptiScaler Client:

1.  **Fork** the project.
2.  Create your **Feature Branch** (`git checkout -b feature/AmazingFeature`).
3.  **Commit** your changes (`git commit -m 'Add some AmazingFeature'`).
4.  **Push** to the branch (`git push origin feature/AmazingFeature`).
5.  Open a **Pull Request**.

---

## 📄 License & Acknowledgments

### License

**OptiScaler Client** is free software: you can redistribute it and/or modify it under the terms of the **GNU General Public License** as published by the Free Software Foundation, either **version 3** of the License, or (at your option) **any later version**.

This program is distributed in the hope that it will be useful, but **WITHOUT ANY WARRANTY**; without even the implied warranty of **MERCHANTABILITY** or **FITNESS FOR A PARTICULAR PURPOSE**. See the [GNU General Public License](LICENSE) for more details.

### Acknowledgments & Third-Party Software

*   **Special thanks and deep respect to the OptiScaler development team** for creating and maintaining this incredible software that enhances gaming experiences for countless users worldwide.
*   **[OptiScaler](https://github.com/optiscaler/OptiScaler)**: The core upscaling technology that makes this possible.
*   **[fakenvapi](https://github.com/optiscaler/fakenvapi)**: Essential compatibility layer developed by the OptiScaler team.
*   **[NukemFG (DLSSG-to-FSR3)](https://github.com/Nukem9/dlssg-to-fsr3)**: Frame Generation bridge by Nukem.

This client application is merely a frontend interface to help users more easily manage and install the amazing work done by the OptiScaler team and other contributors. While OptiScaler Client itself is licensed under GPL-3.0-or-later, the third-party components it downloads and manages may be subject to their own respective licenses.

---

<p align="center">
  Developed with ❤️
</p>
