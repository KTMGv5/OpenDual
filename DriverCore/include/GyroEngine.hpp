#pragma once

#include "Common.hpp"
#include <windows.h>
#include <cmath>

namespace DualDriver {

class GyroEngine {
public:
    GyroEngine();
    void Reset();

    // Processes gyro input and injects mouse movement if active
    // Returns true if gyro aiming was actively engaged during this report
    bool Process(const ControllerState& state, const ControllerConfig& config);

    // Reset bias calibration baseline
    void Recalibrate();

private:
    void SendMouseMove(int dx, int dy);

    float m_accumX = 0.0f;
    float m_accumY = 0.0f;

    // Moving average filter
    float m_filterX = 0.0f;
    float m_filterY = 0.0f;

    // Baseline drift calibration offsets
    float m_biasX = 0.0f;
    float m_biasY = 0.0f;
    float m_biasZ = 0.0f;

    bool m_wasAiming = false;
};

} // namespace DualDriver
