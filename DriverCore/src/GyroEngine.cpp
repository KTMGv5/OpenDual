#include "GyroEngine.hpp"
#include <algorithm>
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
}

void GyroEngine::Recalibrate() {
    m_biasX = 0.0f;
    m_biasY = 0.0f;
    m_biasZ = 0.0f;
    m_filterX = 0.0f;
    m_filterY = 0.0f;
    m_accumX = 0.0f;
    m_accumY = 0.0f;
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

bool GyroEngine::Process(const ControllerState& state, const ControllerConfig& config) {
    if (!config.gyroEnabled) {
        if (m_wasAiming) {
            Reset();
        }
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
        return false;
    }

    // When newly activated, prime the filter to avoid sudden jumps
    if (!m_wasAiming) {
        m_filterX = 0.0f;
        m_filterY = 0.0f;
        m_accumX = 0.0f;
        m_accumY = 0.0f;
        m_wasAiming = true;
    }

    // Unbiased angular velocities
    // Gyro Z = Yaw (turning left/right)
    // Gyro X = Pitch (tilting nose up/down)
    // Gyro Y = Roll (tilting side to side)
    float rawYaw = (float)state.gyroZ - m_biasZ;
    float rawPitch = (float)state.gyroX - m_biasX;

    // Optional World-space compensation if controller is held angled (e.g. 35 deg pitch)
    // float rawRoll = (float)state.gyroY - m_biasY;
    // rawYaw = rawYaw * 0.85f - rawRoll * 0.52f;

    // Deadzone filtering to prevent micro-jitter
    float deadzone = (float)config.gyroDeadzone;
    if (std::fabs(rawYaw) < deadzone) {
        rawYaw = 0.0f;
    } else {
        rawYaw = (rawYaw > 0.0f) ? (rawYaw - deadzone) : (rawYaw + deadzone);
    }

    if (std::fabs(rawPitch) < deadzone) {
        rawPitch = 0.0f;
    } else {
        rawPitch = (rawPitch > 0.0f) ? (rawPitch - deadzone) : (rawPitch + deadzone);
    }

    // Smoothing filter (Exponential Moving Average)
    float smooth = std::clamp(config.gyroSmooth, 0.0f, 0.90f);
    m_filterX = m_filterX * smooth + rawYaw * (1.0f - smooth);
    m_filterY = m_filterY * smooth + rawPitch * (1.0f - smooth);

    // Ballistics scaling:
    // Scale factor tuned for 1000Hz DualSense polling rate to pixel deltas
    const float baseSensitivity = 0.018f;
    float dx = -m_filterX * baseSensitivity * config.gyroSensX * (config.gyroInvertX ? -1.0f : 1.0f);
    float dy = -m_filterY * baseSensitivity * config.gyroSensY * (config.gyroInvertY ? -1.0f : 1.0f);

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
