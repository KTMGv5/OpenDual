# OpenDual — PlayStation Controller Driver & Desktop Suite

[![Code Analysis & Build Verification](https://github.com/KTMGv5/OpenDual/actions/workflows/code-analysis.yml/badge.svg)](https://github.com/KTMGv5/OpenDual/actions/workflows/code-analysis.yml)
[![Release v1.3.0](https://img.shields.io/github/v/release/KTMGv5/OpenDual?color=00F5D4)](https://github.com/KTMGv5/OpenDual/releases/tag/v1.3.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Open-source, ultra-low-latency Windows driver service and desktop suite for PlayStation DualShock 4 and DualSense (PS5) controllers on Windows 10 & 11.**

---

## Highlights & Key Features

* **True 1000Hz (1ms) Polling Engine**: High-priority multimedia scheduled C++20 engine using raw Windows HID asynchronous overlapped I/O (`AvSetMmThreadCharacteristicsW` + `timeBeginPeriod(1)`), achieving sub-millisecond input pipeline latency.
* **Native Audio & Hardware Mic Mute Sync**:
  * Seamlessly detects DualSense built-in DAC, 3.5mm headphone jack, and microphone.
  * Synchronizes the controller's **hardware Mic Mute button** directly with Windows CoreAudio (`IAudioEndpointVolume`). Pressing the button on the controller mutes/unmutes the Windows system microphone in real time and toggles the physical amber LED.
  * Audio volume fix ensures headphone jack playback remains active without manual Windows volume slider adjustments.
* **Dynamic Windows System Theme & Accent Color**:
  * Automatically follows Windows 10/11 system Dark and Light modes.
  * Dynamically extracts and adapts to your **Windows Accent Color** (Blue, Red, Green, Purple, Cyan, Orange, etc.) in real time via Win32 message hooks (`WM_SETTINGCHANGE`, `WM_DWMCOLORIZATIONCOLORCHANGED`).
* **Touchpad Mouse & Gestures Engine**:
  * Ballistic cursor movement with speed acceleration and sub-pixel accumulation.
  * **Tap-To-Click**: Single-finger tap for Left Click, two-finger tap for Right Click.
  * **Two-Finger Scrolling**: Smooth vertical and horizontal wheel scrolling.
  * **Split Physical Click**: Left 75% zone triggers Left Click, Right 25% zone triggers Right Click.
  * Configurable sensitivity sliders and on-the-fly controller hotkeys.
* **RGB Lightbar Studio & Glow Speed Control**:
  * Full 24-bit RGB customization with live hex preview and presets.
  * Animated modes: **Solid Color**, **Breathing / Pulse**, **Rainbow 360° Spectrum**, and **Battery Level Gauge**.
  * **Glow Cycle Speed Slider** (0.2x to 3.0x) with real-time synchronized hardware preview.
  * DualSense Player Indicator LEDs selector (Player 1, 2, 3, 4).
* **DualSense Adaptive Triggers**:
  * Stepper-motor resistance controls with independent Left & Right trigger configuration.
  * Preset profiles: **Hair Trigger** (instant competitive actuation), **Machine Gun / Pulse** (rapid mechanical recoil kickback), **Bow** (smooth tension curve), and **Rigid Stop** (custom hard stop point).
* **Zero-Lag Gamepad Output**:
  * Dynamic ViGEmBus client integration for 100% plug-and-play Xbox 360 controller emulation across all Windows PC games.
  * Two-way force feedback: routes game rumble events directly into the controller's physical motors.
* **Audio-Reactive Haptic Bass Rumble (WASAPI Loopback)**:
  * Captures Windows system and game audio in real time via low-latency WASAPI loopback.
  * Isolates low-frequency explosions, footsteps, engine revs, and gunshots using an adjustable biquad low-pass filter (60Hz to 200Hz).
  * Direct stereo voice-coil tactile motor routing with soft-knee punch curve and debounced, non-jittery sliders.
* **Audio-to-Trigger Mechanical Recoil Kick**:
  * Real-time audio transient spike detection tracks explosive gunshots, cannon fire, and grenade detonations.
  * Instantly pulses the DualSense adaptive trigger stepper motors for ~90-120ms with physical mechanical kickback.
* **In-Game Virtual Quick HUD (PS Button Overlay)**:
  * Floating, translucent, non-focus-stealing PS5 Control Center-inspired HUD overlay.
  * Pops up when pressing the controller's **PS button** without minimizing or interrupting full-screen games.
  * Displays live battery %, charging state, master audio volume, hardware mic status, and adaptive trigger profiles. Auto-fades after 3.5s or toggles off on next press.
* **Windows System Tray & Background Residency**:
  * Minimizing OpenDual automatically docks it cleanly to the Windows notification area / system tray.
  * System tray context menu: Open Hub, Restart Driver Engine, Toggle Mic Mute, or Exit.
* **Aggressive Updater & In-Place Reinstaller**:
  * Cleanly force-terminates all running driver and hub instances before overwriting files.
  * Automatic retry mechanisms on file locks and automatic restart of driver and hub upon update completion.
* **Automated Authenticode Code Signing Suite**:
  * Includes automated scripts (`tools\sign_binaries.bat` and `tools\create_codesign_cert.ps1`) to stamp binaries with SHA-256 digital signatures and RFC 3161 timestamps.
  * Complete [Code Signing & SmartScreen Guide](docs/CODE_SIGNING_GUIDE.md) explaining local trust and commercial CA distribution.
* **Clean Windows Setup & Uninstaller Suite**:
  * Registered in Windows Registry (`HKCU\...\Uninstall\OpenDual`) and Windows Settings (Installed Apps / Add or Remove Programs).
  * App Paths registration for quick launch via `Win + R` -> `opendual`.
  * Start Menu and Desktop shortcuts.
  * Clean uninstaller (`OpenDualUninstaller.exe` / `uninstall.bat`) that safely stops processes, removes registry entries, removes shortcuts, and cleans up files.

---

## Supported Hardware

| Controller | Connection | Audio & Mic | Touchpad Mouse | RGB Lightbar | Adaptive Triggers |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Sony DualSense (PS5)** | USB / Bluetooth | Native WASAPI | Multi-Touch | Full 24-bit + Speed | Full Stepper Motor |
| **Sony DualSense Edge** | USB / Bluetooth | Native WASAPI | Multi-Touch | Full 24-bit + Speed | Full Stepper Motor |
| **Sony DualShock 4 v2** | USB / Bluetooth | Native USB | Multi-Touch | Full 24-bit + Speed | — |
| **Sony DualShock 4 v1** | USB / Bluetooth | Native USB | Multi-Touch | Full 24-bit + Speed | — |

---

## Quick Start & Installation

### 1. Install to Windows (Recommended)
Run `setup.bat` or launch:
```cmd
.\dist\OpenDualSetup.exe
```
This installs OpenDual into `%LocalAppData%\Programs\OpenDual`, creates Desktop and Start Menu shortcuts, and registers the app in Windows Settings (Installed Apps).

### 2. Standalone Portable Run
Run directly without installing:
```cmd
.\dist\OpenDualHub.exe
```

### 3. Uninstalling
Open **Windows Settings** -> **Apps** -> **Installed Apps**, locate **OpenDual PlayStation Controller Suite**, and click **Uninstall**.
Or run:
```cmd
.\uninstall.bat
```

### 4. Controller Hotkeys (Adjust without opening the app)
* **Hold PS Button + D-Pad Up**: Increase touchpad sensitivity (+0.1x)
* **Hold PS Button + D-Pad Down**: Decrease touchpad sensitivity (-0.1x)
* **Hold PS Button + Touchpad Click**: Toggle mouse cursor ON/OFF

---

## Building from Source

### Prerequisites
* Windows 10 or 11 (64-bit)
* Visual Studio 2022 / 2026 with C++ Desktop Development (MSVC C++20)
* CMake 3.20+
* .NET 10 SDK

### One-Click Build
Run the build script from the repository root:
```cmd
build_all.bat
```
This compiles `DriverCore` (C++20), builds `DesktopApp` (.NET 10 WPF), builds `Installer` (OpenDualSetup), generates icons and assets, and packages single-file standalone binaries into `.\dist\`.
