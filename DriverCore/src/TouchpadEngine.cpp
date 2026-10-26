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
    m_accX = m_accY = m_accScroll = m_accHScroll = 0.0f;
    m_lastScrollTime = std::chrono::steady_clock::time_point{};
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

    // 1. Two-finger Scrolling (Discrete Notched Mode for Minecraft Hotbars & Inventory)
    if (t1 && t2) {
        if (m_wasTouch1 && m_wasTouch2) {
            int dy1 = y1 - m_lastY1;
            int dy2 = y2 - m_lastY2;
            int avgDy = (dy1 + dy2) / 2;

            int dx1 = x1 - m_lastX1;
            int dx2 = x2 - m_lastX2;
            int avgDx = (dx1 + dx2) / 2;

            m_accScroll += (float)(-avgDy) * config.scrollSensitivity;

            auto nowMs = std::chrono::duration_cast<std::chrono::milliseconds>(now - m_lastScrollTime).count();
            const float notchThreshold = 42.0f; // clean physical gesture threshold
            const int scrollCooldownMs = 65;    // tactile slot-by-slot wheel detent cadence

            if (std::abs(m_accScroll) >= notchThreshold && nowMs >= scrollCooldownMs) {
                int dir = (m_accScroll > 0) ? 1 : -1;
                SendMouseWheel(dir * WHEEL_DELTA);
                m_accScroll = 0.0f;
                m_lastScrollTime = now;
            }

            // Horizontal scroll with detent
            m_accHScroll += (float)avgDx * config.scrollSensitivity;
            if (std::abs(m_accHScroll) >= notchThreshold && nowMs >= scrollCooldownMs) {
                int dir = (m_accHScroll > 0) ? 1 : -1;
                SendMouseHWheel(dir * WHEEL_DELTA);
                m_accHScroll = 0.0f;
                m_lastScrollTime = now;
            }
        }
        m_touch1Moved = true;
    }
    // 2. Single-finger Cursor movement (High-Precision Inventory Targeting)
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
            if (std::abs(x1 - m_startX1) > 12 || std::abs(y1 - m_startY1) > 12) {
                m_touch1Moved = true;
            }

            // Anti-jitter micro-deadzone: prevents cursor dancing when hovering over inventory items
            if (std::abs(dx) <= 1 && std::abs(dy) <= 1) {
                dx = 0;
                dy = 0;
            }

            // Natural inventory-tuned scaling (0.24f allows pixel-by-pixel slot control)
            float baseScale = 0.24f;
            float fx = (float)dx * config.mouseSensitivity * baseScale;
            float fy = (float)dy * config.mouseSensitivity * baseScale;

            if (config.invertX) fx = -fx;
            if (config.invertY) fy = -fy;

            // Dual-curve ballistics:
            // Fine movements (< 7.0f) have 100% linear 1:1 precision for inventory items
            // Rapid swipes (> 7.0f) accelerate smoothly up to 2.0x for screen crossing
            float speed = std::sqrt(fx * fx + fy * fy);
            if (speed > 7.0f) {
                float accelFactor = 1.0f + (speed - 7.0f) * 0.035f;
                if (accelFactor > 2.0f) accelFactor = 2.0f;
                fx *= accelFactor;
                fy *= accelFactor;
            }

            m_accX += fx;
            m_accY += fy;

            int stepX = (int)m_accX;
            int stepY = (int)m_accY;

            if (stepX != 0 || stepY != 0) {
                SendMouseMove(stepX, stepY);
                m_accX -= (float)stepX;
                m_accY -= (float)stepY;
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
