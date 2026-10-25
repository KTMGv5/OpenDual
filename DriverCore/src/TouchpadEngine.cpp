#include "TouchpadEngine.hpp"
#include <cmath>
#include <algorithm>

namespace DualDriver {

TouchpadEngine::TouchpadEngine() {
    Reset();
}

void TouchpadEngine::Reset() {
    m_wasTouch1 = false;
    m_wasTouch2 = false;
    m_lastX1 = m_lastY1 = 0;
    m_lastX2 = m_lastY2 = 0;
    m_startX1 = m_startY1 = 0;
    m_touch1Moved = false;
    m_wasPadClicked = false;
    m_holdingLeftClick = false;
    m_holdingRightClick = false;
    m_accX = m_accY = m_accScroll = 0.0f;
}

void TouchpadEngine::SendMouseMove(int dx, int dy) {
    if (dx == 0 && dy == 0) return;
    INPUT input = { 0 };
    input.type = INPUT_MOUSE;
    input.mi.dwFlags = MOUSEEVENTF_MOVE;
    input.mi.dx = dx;
    input.mi.dy = dy;
    SendInput(1, &input, sizeof(INPUT));
}

void TouchpadEngine::SendMouseClick(DWORD downFlag, DWORD upFlag) {
    INPUT inputs[2] = { 0 };
    inputs[0].type = INPUT_MOUSE;
    inputs[0].mi.dwFlags = downFlag;

    inputs[1].type = INPUT_MOUSE;
    inputs[1].mi.dwFlags = upFlag;

    SendInput(2, inputs, sizeof(INPUT));
}

void TouchpadEngine::SendMouseWheel(int delta) {
    if (delta == 0) return;
    INPUT input = { 0 };
    input.type = INPUT_MOUSE;
    input.mi.dwFlags = MOUSEEVENTF_WHEEL;
    input.mi.mouseData = delta;
    SendInput(1, &input, sizeof(INPUT));
}

void TouchpadEngine::SendMouseHWheel(int delta) {
    if (delta == 0) return;
    INPUT input = { 0 };
    input.type = INPUT_MOUSE;
    input.mi.dwFlags = MOUSEEVENTF_HWHEEL;
    input.mi.mouseData = delta;
    SendInput(1, &input, sizeof(INPUT));
}

void TouchpadEngine::Process(const ControllerState& state, const ControllerConfig& config) {
    if (!config.mouseEnabled) {
        Reset();
        return;
    }

    auto now = std::chrono::steady_clock::now();
    bool t1 = state.touch1.active;
    bool t2 = state.touch2.active;
    int x1 = state.touch1.x;
    int y1 = state.touch1.y;
    int x2 = state.touch2.x;
    int y2 = state.touch2.y;

    // 1. Two-finger Scrolling
    if (t1 && t2) {
        if (m_wasTouch1 && m_wasTouch2) {
            int dy1 = y1 - m_lastY1;
            int dy2 = y2 - m_lastY2;
            int avgDy = (dy1 + dy2) / 2;

            int dx1 = x1 - m_lastX1;
            int dx2 = x2 - m_lastX2;
            int avgDx = (dx1 + dx2) / 2;

            // Vertical scroll (invert so moving fingers down scrolls down)
            if (std::abs(avgDy) > 2) {
                m_accScroll += (-avgDy) * config.scrollSensitivity * 3.0f;
                int scrollSteps = (int)(m_accScroll / 10.0f);
                if (scrollSteps != 0) {
                    SendMouseWheel(scrollSteps * WHEEL_DELTA / 6);
                    m_accScroll -= scrollSteps * 10.0f;
                }
            }

            // Horizontal scroll
            if (std::abs(avgDx) > 4) {
                SendMouseHWheel((avgDx > 0 ? 1 : -1) * WHEEL_DELTA / 6);
            }
        }
        m_touch1Moved = true;
    }
    // 2. Single-finger Cursor movement
    else if (t1 && !t2) {
        if (!m_wasTouch1) {
            // Finger down
            m_startX1 = x1;
            m_startY1 = y1;
            m_touch1StartTime = now;
            m_touch1Moved = false;
        } else {
            int dx = x1 - m_lastX1;
            int dy = y1 - m_lastY1;

            // Check if finger moved beyond tap threshold
            if (std::abs(x1 - m_startX1) > 15 || std::abs(y1 - m_startY1) > 15) {
                m_touch1Moved = true;
            }

            // Apply ballistics & sensitivity with balanced base scaling (raw coords are 0..1920)
            float baseScale = 0.28f;
            float fx = (float)dx * config.mouseSensitivity * baseScale;
            float fy = (float)dy * config.mouseSensitivity * baseScale;

            if (config.invertX) fx = -fx;
            if (config.invertY) fy = -fy;

            // Speed acceleration curve (swipes move faster, slow movements are precise)
            float speed = std::sqrt(fx * fx + fy * fy);
            if (speed > 5.0f) {
                float accelFactor = 1.0f + (speed - 5.0f) * 0.04f;
                if (accelFactor > 2.2f) accelFactor = 2.2f;
                fx *= accelFactor;
                fy *= accelFactor;
            }

            m_accX += fx;
            m_accY += fy;

            int stepX = (int)m_accX;
            int stepY = (int)m_accY;

            if (stepX != 0 || stepY != 0) {
                SendMouseMove(stepX, stepY);
                m_accX -= stepX;
                m_accY -= stepY;
            }
        }
    }
    // 3. Finger release - Check Tap-To-Click
    else if (!t1 && m_wasTouch1) {
        if (config.tapToClick && !m_touch1Moved) {
            auto duration = std::chrono::duration_cast<std::chrono::milliseconds>(now - m_touch1StartTime).count();
            if (duration < 250) {
                // Short tap detected!
                if (m_wasTouch2) {
                    // Two finger tap -> Right Click
                    SendMouseClick(MOUSEEVENTF_RIGHTDOWN, MOUSEEVENTF_RIGHTUP);
                } else {
                    // Single finger tap -> Left Click
                    SendMouseClick(MOUSEEVENTF_LEFTDOWN, MOUSEEVENTF_LEFTUP);
                }
            }
        }
    }

    // 4. Physical Pad Click Button
    bool padClicked = state.touchpadBtn;
    if (padClicked && !m_wasPadClicked) {
        // Physical click down: check if right side (X > 1440 on 0..1920 scale = right 25%)
        if (t1 && x1 > 1440) {
            INPUT input = { 0 };
            input.type = INPUT_MOUSE;
            input.mi.dwFlags = MOUSEEVENTF_RIGHTDOWN;
            SendInput(1, &input, sizeof(INPUT));
            m_holdingRightClick = true;
        } else {
            INPUT input = { 0 };
            input.type = INPUT_MOUSE;
            input.mi.dwFlags = MOUSEEVENTF_LEFTDOWN;
            SendInput(1, &input, sizeof(INPUT));
            m_holdingLeftClick = true;
        }
    } else if (!padClicked && m_wasPadClicked) {
        // Physical click released
        if (m_holdingRightClick) {
            INPUT input = { 0 };
            input.type = INPUT_MOUSE;
            input.mi.dwFlags = MOUSEEVENTF_RIGHTUP;
            SendInput(1, &input, sizeof(INPUT));
            m_holdingRightClick = false;
        }
        if (m_holdingLeftClick) {
            INPUT input = { 0 };
            input.type = INPUT_MOUSE;
            input.mi.dwFlags = MOUSEEVENTF_LEFTUP;
            SendInput(1, &input, sizeof(INPUT));
            m_holdingLeftClick = false;
        }
    }

    m_wasPadClicked = padClicked;
    m_wasTouch1 = t1;
    m_wasTouch2 = t2;
    m_lastX1 = x1;
    m_lastY1 = y1;
    m_lastX2 = x2;
    m_lastY2 = y2;
}

} // namespace DualDriver
