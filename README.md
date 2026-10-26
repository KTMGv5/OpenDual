# OpenDual - PlayStation Controller Driver & Desktop Suite

[![Code Analysis & Build Verification](https://github.com/KTMGv5/OpenDual/actions/workflows/code-analysis.yml/badge.svg)](https://github.com/KTMGv5/OpenDual/actions/workflows/code-analysis.yml)
[![Release v1.5.0](https://img.shields.io/github/v/release/KTMGv5/OpenDual?color=00F5D4)](https://github.com/KTMGv5/OpenDual/releases/tag/v1.5.0)
[![License: MIT](https://img.shields.io/badge/License-MIT-blue.svg)](LICENSE)

**Open-source, ultra-low-latency Windows driver service and desktop suite for PlayStation DualShock 4 and DualSense (PS5) controllers on Windows 10 & 11.**

---

## Highlights & Key Features

* **True 1000Hz (1ms) Polling Engine**: High-priority multimedia scheduled C++20 engine using raw Windows HID asynchronous overlapped I/O (`AvSetMmThreadCharacteristicsW` + `timeBeginPeriod(1)`), achieving sub-millisecond input pipeline latency.
* **Interactive In-Game Control Center HUD (PS Button)**:
  * Persistent, non-focus-stealing PS5-style overlay (`WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW`) that opens when pressing the **PS button** without minimizing or dropping focus in games.
  * Stays open persistently until explicitly closed with Circle (O) or PS Button. Ephemeral toasts auto-dismiss cleanly after 3.5 seconds.
  * Customizable display scaling (Compact 85%, Standard 100%, Large 115%, Couch/TV 130%) so the HUD is never cut off on high-DPI screens.
  * **5 Controller-Navigable Tiles**:
    * **Tile 0 (Game & Profile)**: Live active game name, profile badge, and trigger modes. D-Pad Up/Down cycles profiles on the fly; Cross (X) applies.
    * **Tile 1 (Master Audio & Mic)**: Live Windows volume bar and endpoint name. D-Pad Up/Down adjusts volume in +/-5% steps; Cross (X) toggles master mute.
    * **Tile 2 (6-Axis Gyro Aiming)**: Motion aiming status and sensitivity. D-Pad Up/Down adjusts sensitivity in +/-0.1x steps; Cross (X) toggles gyro on/off.
    * **Tile 3 (PC Hardware Specs)**: Real-time CPU % utilization (`GetSystemTimes`), RAM load % and used GB / total GB (`GlobalMemoryStatusEx`) with zero CPU overhead.
    * **Tile 4 (Touchpad & Haptics)**: Live touchpad sensitivity and stereo haptic summary. D-Pad Up/Down adjusts sensitivity (+/-0.1x); Cross (X) toggles touchpad on/off.
  * **Universal Quick Action**: Square ([]) toggles mic mute from anywhere in the HUD. Mouse clicks and touch inputs are also fully supported.
* **Touchpad Mouse & Precision Notch Scrolling**:
  * **Discrete Notched Scroll Mode (Minecraft & Inventory Safe)**: Cleanly moves inventory hotbars, weapon selectors, and menus exactly one slot per physical two-finger swipe, eliminating frantic runaway scroll spinning.
  * **Micro-Jitter Deadzone & Dual-Slope Ballistics**: Filters out tiny finger tremors for pinpoint 36x36 slot clicking, accelerating smoothly for swift cross-screen desktop navigation.
  * **Tap-To-Click & Gestures**: Single-finger tap for Left Click, two-finger tap for Right Click, and two-finger smooth scrolling.
  * **Split Physical Click**: Left 75% zone triggers Left Click, Right 25% zone triggers Right Click.
  * Live touchpad sensitivity telemetry with on-the-fly controller shortcuts and Control Center HUD adjustments.
* **6-Axis Gyro Motion Aiming Engine**:
  * Reads DualSense and DS4 high-rate IMU angular velocities (Yaw, Pitch, Roll) and gravitational acceleration at 1000Hz.
  * Sub-pixel mouse injection (`SendInput`) with floating-point accumulation for buttery smooth micro-aiming.
  * Intelligent activation modes: **Hold L2 (ADS - Aim Down Sights)**, **Always On**, **Touchpad Touch**, or **Hold R2**.
  * Dynamic resting baseline auto-calibration eliminates resting gyro drift and jitter.
* **Game-Specific Auto-Profile Switching & Preset Library**:
  * Background Win32 foreground process monitor detects active games (`GetForegroundWindow` + `QueryFullProcessImageName`) with <0.001% CPU usage.
  * Instantly auto-switches adaptive trigger modes, RGB themes, audio haptics, and gyro settings per game, and smoothly restores defaults upon exit.
  * Curated built-in profiles: **Competitive Shooter (FPS)** with hair triggers and ADS gyro, **Sim Racing & ABS Brakes** with progressive pedal curves, **Action/RPG Bow & Combat**, and **Flight/Space Simulator**.
  * Full JSON export and community profile import (`.json`).
* **Bluetooth Power Management & Low-Battery Warnings**:
  * Configurable inactivity timer (5/10/15/30 min) powers down LEDs and motor loopbacks during idle periods.
  * Critical battery alert (<15%) pulses the DualSense lightbar with a gentle red indicator and delivers non-intrusive Windows / Quick HUD notifications.
* **In-App Automatic Suite Updater**:
  * Asynchronously queries the GitHub Releases API for `KTMGv5/OpenDual`, compares semantic versioning, and provides 1-click silent background downloading, in-place reinstallation, and relaunch.
* **DualSense Edge Extra Back Paddles & Function Buttons**:
  * Fully decodes and visualizes DualSense Edge hardware reports: Left Fn, Right Fn, Back Paddle LB, and Back Paddle RB.
* **Native Audio & Hardware Mic Mute Sync**:
  * Seamlessly detects DualSense built-in DAC, 3.5mm headphone jack, and microphone.
  * Synchronizes the controller's **hardware Mic Mute button** directly with Windows CoreAudio (`IAudioEndpointVolume`). Pressing the button on the controller mutes/unmutes the Windows system microphone in real time and toggles the physical amber LED.
* **Dynamic Windows System Theme & Accent Color**:
  * Automatically follows Windows 10/11 system Dark and Light modes.
  * Dynamically extracts and adapts to your **Windows Accent Color** (Blue, Red, Green, Purple, Cyan, Orange, etc.) in real time via Win32 message hooks (`WM_SETTINGCHANGE`, `WM_DWMCOLORIZATIONCOLORCHANGED`).
* **RGB Lightbar Studio & Glow Speed Control**:
  * Full 24-bit RGB customization with live hex preview and presets.
  * Animated modes: **Solid Color**, **Breathing / Pulse**, **Rainbow 360-degree Spectrum**, and **Battery Level Gauge**.
  * **Glow Cycle Speed Slider** (0.2x to 3.0x) with real-time synchronized hardware preview.
  * DualSense Player Indicator LEDs selector (Player 1, 2, 3, 4).
* **DualSense Adaptive Triggers**:
  * Stepper-motor resistance controls with independent Left & Right trigger configuration.
  * Preset profiles: **Hair Trigger** (instant competitive actuation), **Machine Gun / Pulse** (rapid mechanical recoil kickback), **Bow** (smooth tension curve), and **Rigid Stop** (custom hard stop point).
* **Zero-Lag Gamepad Output**:
  * Dynamic ViGEmBus client integration for 100% plug-and-play Xbox 360 controller emulation across all Windows PC games.
  * Two-way force feedback: routes game rumble events directly into the controller's physical motors.
* **Audio-Reactive Haptic Bass Rumble (Dynamic WASAPI Loopback)**:
  * Captures Windows system and game audio in real time via low-latency WASAPI loopback with **Dynamic Endpoint Migration** (`IMMNotificationClient`). Seamlessly follows default playback output switches between Soundbars, TV speakers, wireless headsets, and the DualSense / DS4 controller itself without dropping haptics.
  * Multi-channel support: handles 4-channel Quadraphonic DualSense streams (headphone stereo + native haptic audio blending) and 5.1/7.1 LFE subwoofer channel isolation.
  * DualSense firmware v2 compatible vibration flags (`DS_OUTPUT_VALID_FLAG2_COMPATIBLE_VIBRATION2`) enable crisp tactile rumble concurrent with high-res USB Audio headphone playback.
  * Isolates low-frequency explosions, footsteps, engine revs, and gunshots using an adjustable 4th-order low-pass filter (40Hz to 250Hz).
  * Direct stereo voice-coil tactile motor routing with soft-knee punch curve and fast musical release envelope (zero static buzz).
* **Audio-to-Trigger Mechanical Recoil Kick**:
  * Real-time audio transient spike detection tracks explosive gunshots, cannon fire, and grenade detonations.
  * Instantly pulses the DualSense adaptive trigger stepper motors for ~90-120ms with physical mechanical kickback.
* **Windows System Tray & Background Residency**:
  * Minimizing OpenDual automatically docks it cleanly to the Windows notification area / system tray.
  * System tray context menu: Open Hub, Restart Driver Engine, Toggle Mic Mute, or Exit.
* **Automated Authenticode Code Signing Suite**:
  * Includes automated scripts (`tools\sign_binaries.bat` and `tools\create_codesign_cert.ps1`) to stamp binaries with SHA-256 digital signatures and RFC 3161 timestamps.
  * Complete [Code Signing & SmartScreen Guide](docs/CODE_SIGNING_GUIDE.md) explaining local trust and commercial CA distribution.
* **Clean Windows Setup & Uninstaller Suite**:
  * Registered in Windows Registry (`HKCU\...\Uninstall\OpenDual`) and Windows Settings (Installed Apps / Add or Remove Programs).
  * App Paths registration for quick launch via `Win + R` -> `opendual`.
  * Start Menu and Desktop shortcuts.
  * Clean uninstaller (`OpenDualUninstaller.exe` / `uninstall.bat`) that safely stops processes, removes registry entries, removes shortcuts, and cleans up files.
* **Aggressive Updater & In-Place Reinstaller**:
  * Cleanly force-terminates all running driver and hub instances before overwriting files.
  * Automatic retry mechanisms on file locks and automatic restart of driver and hub upon update completion.

---

## Supported Hardware

| Controller | Connection | Audio & Mic | Touchpad Mouse | RGB Lightbar | Adaptive Triggers |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Sony DualSense (PS5)** | USB / Bluetooth | Native WASAPI | Multi-Touch | Full 24-bit + Speed | Full Stepper Motor |
| **Sony DualSense Edge** | USB / Bluetooth | Native WASAPI | Multi-Touch | Full 24-bit + Speed | Full Stepper Motor |
| **Sony DualShock 4 v2** | USB / Bluetooth | Native USB | Multi-Touch | Full 24-bit + Speed | N/A |
| **Sony DualShock 4 v1** | USB / Bluetooth | Native USB | Multi-Touch | Full 24-bit + Speed | N/A |

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
