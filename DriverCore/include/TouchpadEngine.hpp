#pragma once

#include "Common.hpp"
#include <chrono>

namespace DualDriver {

class TouchpadEngine {
public:
    TouchpadEngine();
    ~TouchpadEngine() = default;

    void Process(const ControllerState& state, const ControllerConfig& config);
    void Reset();

private:
    void SendMouseMove(int dx, int dy);
    void SendMouseClick(DWORD downFlag, DWORD upFlag);
    void SendMouseWheel(int delta);
    void SendMouseHWheel(int delta);

    bool m_wasTouch1 = false;
    bool m_wasTouch2 = false;
    int m_lastX1 = 0, m_lastY1 = 0;
    int m_lastX2 = 0, m_lastY2 = 0;
    int m_startX1 = 0, m_startY1 = 0;
    std::chrono::steady_clock::time_point m_touch1StartTime;
    bool m_touch1Moved = false;

    // Physical button click state
    bool m_wasPadClicked = false;
    bool m_holdingLeftClick = false;
    bool m_holdingRightClick = false;

    // Sub-pixel accumulator
    float m_accX = 0.0f;
    float m_accY = 0.0f;
    float m_accScroll = 0.0f;
};

} // namespace DualDriver
