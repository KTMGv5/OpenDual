#include "GyroEngine.hpp"
#include <algorithm>
#include <cmath>
#include <iostream>

namespace DualDriver {

GyroEngine::GyroEngine() {
    Reset();
}

void GyroEngine::Reset() {
    m_accumX = 0.0f;
    m_accumY = 0.0f;
    m_filterX = 0.0f;
    m_filterY = 0.0f;
    m_biasX = 0.0f;
    m_biasY = 0.0f;
    m_biasZ = 0.0f;
    m_wasAiming = false;
    m_lastProcessTime = std::chrono::steady_clock::time_point{};
}

void GyroEngine::Recalibrate() {
    m_biasX = 0.0f;
    m_biasY = 0.0f;
    m_biasZ = 0.0f;
    m_filterX = 0.0f;
    m_filterY = 0.0f;
    m_accumX = 0.0f;
    m_accumY = 0.0f;
    m_lastProcessTime = std::chrono::steady_clock::time_point{};
}

void GyroEngine::SendMouseMove(int dx, int dy) {
    if (dx == 0 && dy == 0) return;
    INPUT input = { 0 };
    input.type = INPUT_MOUSE;
    input.mi.dwFlags = MOUSEEVENTF_MOVE;
    input.mi.dx = dx;
    input.mi.dy = dy;
    SendInput(1, &input, sizeof(INPUT));
}

bool GyroEngine::Process(ControllerState& state, const ControllerConfig& config) {
    if (!config.gyroEnabled) {
        if (m_wasAiming) {
            Reset();
        }
        state.gyroStickX = 0.0f;
        state.gyroStickY = 0.0f;
        return false;
    }

    // Auto-calibrate drift while controller is stationary on a desk
    if (std::abs(state.gyroX) < 45 && std::abs(state.gyroY) < 45 && std::abs(state.gyroZ) < 45) {
        m_biasX = m_biasX * 0.998f + (float)state.gyroX * 0.002f;
        m_biasY = m_biasY * 0.998f + (float)state.gyroY * 0.002f;
        m_biasZ = m_biasZ * 0.998f + (float)state.gyroZ * 0.002f;
    }

    // Determine activation condition
    bool active = false;
    switch (config.gyroTriggerMode) {
    case 0: // Always On
        active = true;
        break;
    case 1: // Hold L2 (ADS - Aim Down Sights)
        active = (state.l2 > 0.12f || state.l2Btn);
        break;
    case 2: // Touchpad Touch
        active = (state.touch1.active || state.touch2.active);
        break;
    case 3: // Hold R2 (Firing)
        active = (state.r2 > 0.12f || state.r2Btn);
        break;
    default:
        active = (state.l2 > 0.12f || state.l2Btn);
        break;
    }

    if (!active) {
        m_wasAiming = false;
        m_filterX = 0.0f;
        m_filterY = 0.0f;
        m_accumX = 0.0f;
        m_accumY = 0.0f;
        state.gyroStickX = 0.0f;
        state.gyroStickY = 0.0f;
        return false;
    }

    auto now = std::chrono::steady_clock::now();

    // When newly activated, prime the filter and record timestamp to prevent sudden jumps
    if (!m_wasAiming || m_lastProcessTime.time_since_epoch().count() == 0) {
        m_wasAiming = true;
        m_lastProcessTime = now;
        m_filterX = 0.0f;
        m_filterY = 0.0f;
        m_accumX = 0.0f;
        m_accumY = 0.0f;
        state.gyroStickX = 0.0f;
        state.gyroStickY = 0.0f;
        return true;
    }

    // Accurate elapsed time delta between consecutive sensor reports (dt in seconds)
    float dt = std::chrono::duration<float>(now - m_lastProcessTime).count();
    m_lastProcessTime = now;
    dt = std::clamp(dt, 0.0005f, 0.05f); // Guard against clock anomalies

    // Physical sensor angular velocities in degrees per second (deg/s):
    // DualSense IMU gyroscope sensitivity is 16.384 LSB per deg/s (+/-2000 dps full-scale)
    // Physical hardware layout:
    // Gyro X (bytes 16-17) = Pitch (tilting controller nose up/down)
    // Gyro Y (bytes 18-19) = Yaw   (turning controller left/right around vertical axis)
    // Gyro Z (bytes 20-21) = Roll  (tilting controller side-to-side around forward axis)
    const float countsPerDps = 16.384f;
    float dpsPitch = ((float)state.gyroX - m_biasX) / countsPerDps;
    float dpsYaw   = ((float)state.gyroY - m_biasY) / countsPerDps;
    float dpsRoll  = ((float)state.gyroZ - m_biasZ) / countsPerDps;

    // Horizontal Aiming Space Fusion:
    float horizDps = 0.0f;
    if (config.gyroSpace == 0) {
        // Player Space (Recommended):
        // Fuses Yaw and Roll according to the controller's natural holding angle
        // AccelY is down when held flat, AccelZ is down when held upright
        float ay = (float)state.accelY;
        float az = (float)state.accelZ;
        float grav = std::sqrt(ay * ay + az * az);
        float cosPitch = 0.819f; // default ~35 deg tilt
        float sinPitch = 0.574f;
        if (grav > 2000.0f) {
            cosPitch = std::clamp(ay / grav, 0.0f, 1.0f);
            sinPitch = std::clamp(-az / grav, 0.0f, 1.0f);
            float norm = std::sqrt(cosPitch * cosPitch + sinPitch * sinPitch);
            if (norm > 0.001f) {
                cosPitch /= norm;
                sinPitch /= norm;
            }
        }
        horizDps = -(dpsYaw * cosPitch + dpsRoll * sinPitch);
    } else if (config.gyroSpace == 1) {
        // Pure Yaw (Compass / Steering wheel flat)
        horizDps = -dpsYaw;
    } else {
        // Pure Roll (Steering wheel tilt)
        horizDps = -dpsRoll;
    }

    // Default Vertical Aiming (Natural Pitch: tilting controller nose up aims up)
    float vertDps = dpsPitch;

    // Smooth Deadzone Filtering (in deg/s):
    // Replaces harsh step deadzone with smooth cubic easing to eliminate hand tremors
    // while keeping micro-aiming for snipers buttery smooth
    float speedDps = std::sqrt(horizDps * horizDps + vertDps * vertDps);
    float deadzoneDps = (float)config.gyroDeadzone;
    if (speedDps < deadzoneDps) {
        horizDps = 0.0f;
        vertDps = 0.0f;
    } else if (deadzoneDps > 0.1f) {
        float excess = speedDps - deadzoneDps;
        float ramp = (std::min)(1.0f, excess / (deadzoneDps * 2.0f + 0.1f));
        float factor = (excess / speedDps) * (ramp * ramp * (3.0f - 2.0f * ramp));
        horizDps *= factor;
        vertDps *= factor;
    }

    // Motion Smoothing filter (Exponential Moving Average in deg/s)
    float smooth = std::clamp(config.gyroSmooth, 0.0f, 0.85f);
    m_filterX = m_filterX * smooth + horizDps * (1.0f - smooth);
    m_filterY = m_filterY * smooth + vertDps * (1.0f - smooth);

    // TARGET 1: Virtual Right Thumbstick (sThumbRX / sThumbRY)
    // For controller-only games (Call of Duty, GTA, Cyberpunk, Halo, FIFA)
    if (config.gyroOutputMode == 1) {
        const float maxDpsForStick = 180.0f;
        float stickX = std::clamp((m_filterX / maxDpsForStick) * config.gyroSensX * (config.gyroInvertX ? -1.0f : 1.0f), -1.0f, 1.0f);
        float stickY = std::clamp((m_filterY / maxDpsForStick) * config.gyroSensY * (config.gyroInvertY ? -1.0f : 1.0f), -1.0f, 1.0f);
        state.gyroStickX = stickX;
        state.gyroStickY = stickY;
        return true;
    }

    state.gyroStickX = 0.0f;
    state.gyroStickY = 0.0f;

    // TARGET 0: High-Precision Sub-Pixel Mouse Cursor
    // Screen resolution normalization:
    // 1080p (1920x1080) -> resScale = 1.0
    // 1440p (2560x1440) -> resScale = 1.333
    // 4K (3840x2160)    -> resScale = 2.0
    int screenHeight = GetSystemMetrics(SM_CYSCREEN);
    if (screenHeight <= 0) screenHeight = 1080;
    float resScale = (float)screenHeight / 1080.0f;

    // Natural Sensitivity: 24.0 pixels per physical degree of rotation on 1080p
    const float pixelsPerDeg = 24.0f * resScale;

    // Integration of angular velocity (deg/s * dt) = degrees turned in this interval
    float degX = m_filterX * dt;
    float degY = m_filterY * dt;

    float dx = degX * pixelsPerDeg * config.gyroSensX * (config.gyroInvertX ? -1.0f : 1.0f);
    float dy = -degY * pixelsPerDeg * config.gyroSensY * (config.gyroInvertY ? -1.0f : 1.0f);

    m_accumX += dx;
    m_accumY += dy;

    int moveX = (int)m_accumX;
    int moveY = (int)m_accumY;

    m_accumX -= (float)moveX;
    m_accumY -= (float)moveY;

    if (moveX != 0 || moveY != 0) {
        SendMouseMove(moveX, moveY);
    }

    return true;
}

} // namespace DualDriver
