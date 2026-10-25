#pragma once

#include "Common.hpp"
#include "HidDevice.hpp"
#include "TouchpadEngine.hpp"
#include "AudioSubsystem.hpp"
#include "VirtualGamepad.hpp"
#include "IpcServer.hpp"
#include <thread>
#include <atomic>
#include <mutex>

namespace DualDriver {

class ControllerEngine {
public:
    ControllerEngine();
    ~ControllerEngine();

    bool Start();
    void Stop();

    void SetConfig(const ControllerConfig& config);
    ControllerConfig GetConfig() const;

    ControllerState GetState() const;

private:
    void PollingThread();
    void OutputThread();
    bool DetectAndOpenDevice();
    void UpdateRgbEffects();

    HidDevice m_device;
    TouchpadEngine m_touchpad;
    AudioSubsystem m_audio;
    VirtualGamepad m_gamepad;
    IpcServer m_ipc;

    std::atomic<bool> m_running{ false };
    std::thread m_pollThread;
    std::thread m_outThread;

    mutable std::mutex m_stateMutex;
    ControllerState m_state;
    ControllerConfig m_config;

    // Rumble feedback
    std::atomic<uint8_t> m_leftRumble{ 0 };
    std::atomic<uint8_t> m_rightRumble{ 0 };

    // Dynamic RGB effect counters
    float m_effectPhase = 0.0f;
    uint8_t m_activeR = 0;
    uint8_t m_activeG = 120;
    uint8_t m_activeB = 255;
};

} // namespace DualDriver
