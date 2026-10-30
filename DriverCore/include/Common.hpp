#pragma once

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdint>
#include <string>
#include <vector>
#include <memory>
#include <functional>

namespace DualDriver {

enum class ControllerModel {
    Unknown,
    DualShock4_v1,
    DualShock4_v2,
    DualSense,
    DualSenseEdge
};

enum class ConnectionType {
    Unknown,
    USB,
    Bluetooth
};

enum class TriggerMode : uint8_t {
    Off = 0,
    Rigid = 1,          // Continuous resistance starting at position
    Pulse = 2,          // Gallop / Machine Gun vibration
    Bow = 5,            // Increasing resistance
    TriggerStop = 6     // Hard stop
};

enum class RgbEffect : uint8_t {
    Solid = 0,
    Breathing = 1,
    Rainbow = 2,
    BatteryGauge = 3,
    AudioReactive = 4
};

struct TriggerConfig {
    TriggerMode mode = TriggerMode::Off;
    uint8_t startPos = 0;       // 0..255
    uint8_t force = 0;          // 0..255
    uint8_t frequency = 0;      // 0..255 (for pulse mode)
    uint8_t endPos = 255;       // for section/bow mode
};

struct ControllerConfig {
    // RGB Lightbar
    uint8_t ledR = 0;
    uint8_t ledG = 120;
    uint8_t ledB = 255;         // Default PlayStation Blue
    uint8_t brightness = 255;
    RgbEffect rgbEffect = RgbEffect::Rainbow;
    float effectSpeed = 1.0f;

    // Player LEDs (DualSense)
    uint8_t playerLeds = 0x04;  // Center LED (Player 1)

    // Adaptive Triggers
    TriggerConfig leftTrigger;
    TriggerConfig rightTrigger;

    // Touchpad Mouse Emulation
    bool mouseEnabled = true;
    float mouseSensitivity = 1.2f;
    float scrollSensitivity = 1.0f;
    bool tapToClick = true;
    bool touchpadVolumeGesture = true;  // Slide right edge of touchpad (x > 1650) to adjust master volume
    bool invertX = false;
    bool invertY = false;

    // Audio & Mic Mute Sync
    bool syncMicMute = true;
    bool audioHapticsEnabled = false;
    float hapticIntensity = 0.30f;      // 0.0 to 2.0 (User default: 30%)
    int hapticCutoffHz = 40;            // 40 Hz low-pass (User default: 40 Hz)
    bool hapticStereo = true;           // True = directional stereo, False = combined mono
    bool audioTriggerKick = false;      // Audio-to-trigger mechanical recoil kick
    float audioTriggerThreshold = 0.20f;// Transient spike threshold (User default: 20%)
    bool speakerEnabled = false;        // Controller speaker hardware audio output (Default: OFF)

    // 6-Axis Gyro Motion Aiming
    bool gyroEnabled = false;
    int gyroTriggerMode = 1;            // 0 = Always On, 1 = Hold L2 (ADS), 2 = Touchpad Touch, 3 = Hold R2
    int gyroOutputMode = 0;             // 0 = Mouse Cursor (Sub-Pixel Mouse), 1 = Right Thumbstick (R-Stick Camera)
    int gyroSpace = 0;                  // 0 = Player Space (Auto-Tilt Fusion), 1 = Yaw Only, 2 = Roll Only
    float gyroSensX = 1.0f;             // 0.1 to 5.0
    float gyroSensY = 1.0f;             // 0.1 to 5.0
    bool gyroInvertX = false;
    bool gyroInvertY = false;
    int gyroDeadzone = 3;               // Sensor deadzone in deg/s (default 3 deg/s)
    float gyroSmooth = 0.20f;           // Smoothing factor (0.0 to 0.85)

    // Power Management & Battery
    int idleTimeoutMinutes = 10;        // 0 = Never, 5, 10, 15, 30 min
    bool lowBatteryWarning = true;      // Flash red pulse when battery <= 15%

    // Output Mode
    bool emulateXbox360 = true; // ViGEmBus XInput emulation
};

struct TouchPoint {
    bool active = false;
    uint8_t id = 0;
    int x = 0;
    int y = 0;
};

struct ControllerState {
    ControllerModel model = ControllerModel::Unknown;
    ConnectionType connection = ConnectionType::Unknown;
    std::string devicePath;
    bool connected = false;

    // Sticks: normalized -1.0 to 1.0, and raw 0..255
    uint8_t rawLX = 128, rawLY = 128;
    uint8_t rawRX = 128, rawRY = 128;
    float lx = 0.0f, ly = 0.0f;
    float rx = 0.0f, ry = 0.0f;

    // Triggers: 0..255 and normalized 0.0 to 1.0
    uint8_t rawL2 = 0, rawR2 = 0;
    float l2 = 0.0f, r2 = 0.0f;

    // DPad (0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW, 8=Neutral)
    uint8_t dpad = 8;
    bool dpadUp = false, dpadDown = false, dpadLeft = false, dpadRight = false;

    // Buttons
    bool square = false, cross = false, circle = false, triangle = false;
    bool l1 = false, r1 = false, l2Btn = false, r2Btn = false;
    bool share = false, options = false;
    bool l3 = false, r3 = false;
    bool ps = false, touchpadBtn = false;
    bool micBtn = false;

    // DualSense Edge Extra Buttons & Back Paddles
    bool fnLeft = false, fnRight = false;
    bool paddleLeft = false, paddleRight = false;

    // Touchpad
    TouchPoint touch1;
    TouchPoint touch2;

    // Motion Sensors (int16_t raw)
    int16_t gyroX = 0, gyroY = 0, gyroZ = 0;
    int16_t accelX = 0, accelY = 0, accelZ = 0;

    // Gyro & Activity status
    bool gyroActive = false;
    float gyroStickX = 0.0f;
    float gyroStickY = 0.0f;
    bool isIdle = false;

    // Battery
    uint8_t batteryPercent = 100;
    bool isCharging = false;

    // Telemetry / Performance
    uint32_t pollingRateHz = 0;
    float latencyMs = 0.0f;
    uint64_t totalReports = 0;

    // Mic hardware status
    bool micMuted = false;

    // Audio jack status & Windows master volume
    bool headphonesConnected = false;
    float masterVolume = 1.0f;

    // Audio-Reactive Haptics live output levels (0..255)
    uint8_t hapticMotorL = 0;
    uint8_t hapticMotorR = 0;

    // Active Audio endpoints
    std::string renderAudioDev;
    std::string captureAudioDev;
};

} // namespace DualDriver
