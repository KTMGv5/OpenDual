#pragma once

#include "Common.hpp"
#include <windows.h>
#include <cmath>
#include <chrono>

namespace DualDriver {

class GyroEngine {
public:
    GyroEngine();
    void Reset();

    // Processes gyro input and injects mouse movement or sets gyroStickX/Y on state
    // Returns true if gyro aiming was actively engaged during this report
    bool Process(ControllerState& state, const ControllerConfig& config);

    // Reset bias calibration baseline
    void Recalibrate();

private:
    void SendMouseMove(int dx, int dy);

    float m_accumX = 0.0f;
    float m_accumY = 0.0f;

    // Filtered angular velocities in deg/s
    float m_filterX = 0.0f;
    float m_filterY = 0.0f;

    // Baseline drift calibration offsets in raw counts
    float m_biasX = 0.0f;
    float m_biasY = 0.0f;
    float m_biasZ = 0.0f;

    bool m_wasAiming = false;
    std::chrono::steady_clock::time_point m_lastProcessTime{};
};

} // namespace DualDriver
