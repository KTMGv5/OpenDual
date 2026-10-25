#include "ControllerEngine.hpp"
#include "DualSenseProtocol.hpp"
#include "DS4Protocol.hpp"
#include <setupapi.h>
#include <hidsdi.h>
#include <avrt.h>
#include <timeapi.h>
#include <iostream>
#include <cmath>
#include <algorithm>

#pragma comment(lib, "avrt.lib")
#pragma comment(lib, "winmm.lib")

namespace DualDriver {

// Helper: HSV to RGB for Rainbow Cycle
static void HsvToRgb(float h, float s, float v, uint8_t& r, uint8_t& g, uint8_t& b) {
    float c = v * s;
    float x = c * (1.0f - std::fabs(std::fmod(h / 60.0f, 2.0f) - 1.0f));
    float m = v - c;
    float rf = 0, gf = 0, bf = 0;

    if (h < 60)      { rf = c; gf = x; bf = 0; }
    else if (h < 120){ rf = x; gf = c; bf = 0; }
    else if (h < 180){ rf = 0; gf = c; bf = x; }
    else if (h < 240){ rf = 0; gf = x; bf = c; }
    else if (h < 300){ rf = x; gf = 0; bf = c; }
    else             { rf = c; gf = 0; bf = x; }

    r = (uint8_t)std::clamp((rf + m) * 255.0f, 0.0f, 255.0f);
    g = (uint8_t)std::clamp((gf + m) * 255.0f, 0.0f, 255.0f);
    b = (uint8_t)std::clamp((bf + m) * 255.0f, 0.0f, 255.0f);
}

static std::string WideToUtf8(const std::wstring& wstr) {
    if (wstr.empty()) return std::string();
    int sizeNeeded = WideCharToMultiByte(CP_UTF8, 0, wstr.data(), (int)wstr.size(), NULL, 0, NULL, NULL);
    if (sizeNeeded <= 0) return std::string();
    std::string str(sizeNeeded, 0);
    WideCharToMultiByte(CP_UTF8, 0, wstr.data(), (int)wstr.size(), &str[0], sizeNeeded, NULL, NULL);
    return str;
}

ControllerEngine::ControllerEngine() {
}

ControllerEngine::~ControllerEngine() {
    Stop();
}

bool ControllerEngine::Start() {
    if (m_running) return true;

    timeBeginPeriod(1);

    m_audio.Initialize();
    m_gamepad.Initialize();

    m_gamepad.SetRumbleCallback([this](uint8_t largeMotor, uint8_t smallMotor) {
        m_leftRumble = largeMotor;
        m_rightRumble = smallMotor;
    });

    m_ipc.SetConfigChangeCallback([this](const ControllerConfig& newCfg) {
        SetConfig(newCfg);
    });
    m_ipc.Start();

    m_running = true;
    m_pollThread = std::thread(&ControllerEngine::PollingThread, this);
    m_outThread = std::thread(&ControllerEngine::OutputThread, this);

    return true;
}

void ControllerEngine::Stop() {
    if (!m_running) return;

    m_running = false;

    if (m_pollThread.joinable()) {
        m_pollThread.join();
    }
    if (m_outThread.joinable()) {
        m_outThread.join();
    }

    m_device.Close();
    m_ipc.Stop();
    m_gamepad.Shutdown();
    m_audio.Shutdown();

    timeEndPeriod(1);
}

void ControllerEngine::SetConfig(const ControllerConfig& config) {
    std::lock_guard<std::mutex> lock(m_stateMutex);
    m_config = config;
}

ControllerConfig ControllerEngine::GetConfig() const {
    std::lock_guard<std::mutex> lock(m_stateMutex);
    return m_config;
}

ControllerState ControllerEngine::GetState() const {
    std::lock_guard<std::mutex> lock(m_stateMutex);
    return m_state;
}

bool ControllerEngine::DetectAndOpenDevice() {
    GUID hidGuid;
    HidD_GetHidGuid(&hidGuid);

    HDEVINFO devInfo = SetupDiGetClassDevsA(&hidGuid, NULL, NULL, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
    if (devInfo == INVALID_HANDLE_VALUE) return false;

    SP_DEVICE_INTERFACE_DATA devData = { sizeof(SP_DEVICE_INTERFACE_DATA) };
    DWORD index = 0;

    std::vector<std::string> candidatePaths;
    while (SetupDiEnumDeviceInterfaces(devInfo, NULL, &hidGuid, index++, &devData)) {
        DWORD detailSize = 0;
        SetupDiGetDeviceInterfaceDetailA(devInfo, &devData, NULL, 0, &detailSize, NULL);

        std::vector<BYTE> buffer(detailSize);
        PSP_DEVICE_INTERFACE_DETAIL_DATA_A detailData = (PSP_DEVICE_INTERFACE_DETAIL_DATA_A)buffer.data();
        detailData->cbSize = sizeof(SP_DEVICE_INTERFACE_DETAIL_DATA_A);

        if (SetupDiGetDeviceInterfaceDetailA(devInfo, &devData, detailData, detailSize, NULL, NULL)) {
            char* path = detailData->DevicePath;
            if (strstr(path, "054c") || strstr(path, "054C")) {
                candidatePaths.push_back(path);
            }
        }
    }
    SetupDiDestroyDeviceInfoList(devInfo);

    // Test candidates to find the active, responsive controller
    for (const auto& path : candidatePaths) {
        if (m_device.Open(path)) {
            // Verify by attempting to read 1 report with 100ms timeout
            uint8_t testBuf[128] = { 0 };
            if (m_device.ReadReport(testBuf, sizeof(testBuf), 100)) {
                std::lock_guard<std::mutex> lock(m_stateMutex);
                m_state.connected = true;
                m_state.devicePath = path;

                uint16_t pid = m_device.GetProductId();
                if (pid == 0x0CE6) {
                    m_state.model = ControllerModel::DualSense;
                } else if (pid == 0x0DF2) {
                    m_state.model = ControllerModel::DualSenseEdge;
                } else if (pid == 0x05C4) {
                    m_state.model = ControllerModel::DualShock4_v1;
                } else if (pid == 0x09CC) {
                    m_state.model = ControllerModel::DualShock4_v2;
                } else {
                    m_state.model = ControllerModel::DualSense;
                }

                // Check connection type (USB vs BT) based on report length or path
                if (m_device.GetInputReportLength() > 64 || path.find("{00001124-") != std::string::npos || path.find("bth_") != std::string::npos) {
                    m_state.connection = ConnectionType::Bluetooth;
                } else {
                    m_state.connection = ConnectionType::USB;
                }

                std::cout << "[DriverCore] Controller connected: " << path << " (Model: " << (int)m_state.model << ")\n";
                return true;
            } else {
                m_device.Close();
            }
        }
    }

    return false;
}

void ControllerEngine::PollingThread() {
    // Elevate thread to Pro Audio / Games multimedia priority
    DWORD taskIndex = 0;
    HANDLE hTask = AvSetMmThreadCharacteristicsW(L"Games", &taskIndex);
    SetThreadPriority(GetCurrentThread(), THREAD_PRIORITY_TIME_CRITICAL);

    uint8_t inBuffer[128] = { 0 };

    while (m_running) {
        if (!m_device.IsOpen()) {
            {
                std::lock_guard<std::mutex> lock(m_stateMutex);
                m_state.connected = false;
            }
            if (!DetectAndOpenDevice()) {
                Sleep(500);
                continue;
            }
        }

        // 1000Hz (1ms) Overlapped Read
        if (m_device.ReadReport(inBuffer, sizeof(inBuffer), 15)) {
            ControllerState localState;
            ControllerConfig localConfig;

            {
                std::lock_guard<std::mutex> lock(m_stateMutex);
                localState = m_state;
                localConfig = m_config;
            }

            bool parsed = false;
            if (localState.model == ControllerModel::DualSense || localState.model == ControllerModel::DualSenseEdge) {
                parsed = DualSenseProtocol::ParseInputReport(inBuffer, m_device.GetInputReportLength(), localState, localState.connection);
            } else {
                parsed = DS4Protocol::ParseInputReport(inBuffer, m_device.GetInputReportLength(), localState, localState.connection);
            }

            if (parsed) {
                // Telemetry
                localState.pollingRateHz = m_device.GetCurrentPollingRate();
                localState.latencyMs = m_device.GetCurrentLatencyMs();
                localState.totalReports = m_device.GetTotalReports();

                // Controller Shortcut: Hold PS Button + DPad Up/Down to adjust Touchpad Sensitivity
                static bool lastDpadUp = false;
                static bool lastDpadDown = false;
                static bool lastPadToggle = false;
                if (localState.ps) {
                    if (localState.dpadUp && !lastDpadUp) {
                        localConfig.mouseSensitivity = std::clamp(localConfig.mouseSensitivity + 0.1f, 0.2f, 3.0f);
                        std::cout << "\n[Touchpad] Sensitivity increased: " << localConfig.mouseSensitivity << "x\n";
                        {
                            std::lock_guard<std::mutex> lock(m_stateMutex);
                            m_config.mouseSensitivity = localConfig.mouseSensitivity;
                        }
                    } else if (localState.dpadDown && !lastDpadDown) {
                        localConfig.mouseSensitivity = std::clamp(localConfig.mouseSensitivity - 0.1f, 0.2f, 3.0f);
                        std::cout << "\n[Touchpad] Sensitivity decreased: " << localConfig.mouseSensitivity << "x\n";
                        {
                            std::lock_guard<std::mutex> lock(m_stateMutex);
                            m_config.mouseSensitivity = localConfig.mouseSensitivity;
                        }
                    } else if (localState.touchpadBtn && !lastPadToggle) {
                        localConfig.mouseEnabled = !localConfig.mouseEnabled;
                        std::cout << "\n[Touchpad] Mouse cursor emulation: " << (localConfig.mouseEnabled ? "ENABLED" : "DISABLED") << "\n";
                        {
                            std::lock_guard<std::mutex> lock(m_stateMutex);
                            m_config.mouseEnabled = localConfig.mouseEnabled;
                        }
                    }
                }
                lastDpadUp = localState.dpadUp;
                lastDpadDown = localState.dpadDown;
                lastPadToggle = localState.touchpadBtn;

                // Process hardware mic button synchronization
                localState.micMuted = m_audio.ProcessMicButton(localState.micBtn, localConfig.syncMicMute);

                // Check for user activity (to manage power saving / idle sleep)
                bool hadActivity = localState.cross || localState.circle || localState.square || localState.triangle ||
                                   localState.l1 || localState.r1 || localState.l2Btn || localState.r2Btn ||
                                   localState.share || localState.options || localState.l3 || localState.r3 ||
                                   localState.ps || localState.touchpadBtn || localState.micBtn ||
                                   localState.fnLeft || localState.fnRight || localState.paddleLeft || localState.paddleRight ||
                                   std::abs((int)localState.rawLX - 128) > 18 || std::abs((int)localState.rawLY - 128) > 18 ||
                                   std::abs((int)localState.rawRX - 128) > 18 || std::abs((int)localState.rawRY - 128) > 18 ||
                                   localState.rawL2 > 20 || localState.rawR2 > 20 ||
                                   localState.touch1.active || localState.touch2.active ||
                                   std::abs(localState.gyroX) > 85 || std::abs(localState.gyroY) > 85 || std::abs(localState.gyroZ) > 85;

                uint64_t nowTick = GetTickCount64();
                if (hadActivity || m_lastActivityTick.load() == 0) {
                    m_lastActivityTick.store(nowTick);
                    localState.isIdle = false;
                } else if (localConfig.idleTimeoutMinutes > 0) {
                    uint64_t idleMs = nowTick - m_lastActivityTick.load();
                    localState.isIdle = (idleMs >= (uint64_t)localConfig.idleTimeoutMinutes * 60000ULL);
                } else {
                    localState.isIdle = false;
                }

                // Process Touchpad mouse ballistics & gestures (only if not idle)
                if (!localState.isIdle) {
                    m_touchpad.Process(localState, localConfig);
                }

                // Process 6-Axis Gyro Motion Aiming
                if (!localState.isIdle) {
                    localState.gyroActive = m_gyro.Process(localState, localConfig);
                } else {
                    localState.gyroActive = false;
                }

                // Update Virtual Gamepad (XInput)
                if (localConfig.emulateXbox360) {
                    m_gamepad.Update(localState);
                }

                {
                    std::lock_guard<std::mutex> lock(m_stateMutex);
                    m_state = localState;
                }
            }
        } else {
            // Read failed or timed out: device may have disconnected
            if (GetLastError() == ERROR_DEVICE_NOT_CONNECTED || GetLastError() == ERROR_GEN_FAILURE) {
                std::cout << "[DriverCore] Controller disconnected.\n";
                m_device.Close();
                m_touchpad.Reset();
                std::lock_guard<std::mutex> lock(m_stateMutex);
                m_state.connected = false;
            }
        }
    }

    if (hTask) {
        AvRevertMmThreadCharacteristics(hTask);
    }
}

void ControllerEngine::UpdateRgbEffects() {
    ControllerConfig cfg;
    ControllerState st;
    {
        std::lock_guard<std::mutex> lock(m_stateMutex);
        cfg = m_config;
        st = m_state;
    }

    float speed = cfg.effectSpeed > 0.05f ? cfg.effectSpeed : 1.0f;
    m_effectPhase += 0.05f * speed;
    if (m_effectPhase > 10000.0f) m_effectPhase = 0.0f;

    switch (cfg.rgbEffect) {
    case RgbEffect::Solid:
        m_activeR = cfg.ledR;
        m_activeG = cfg.ledG;
        m_activeB = cfg.ledB;
        break;

    case RgbEffect::Breathing: {
        float factor = (std::sin(m_effectPhase) + 1.0f) * 0.5f; // 0.0 to 1.0
        m_activeR = (uint8_t)(cfg.ledR * factor);
        m_activeG = (uint8_t)(cfg.ledG * factor);
        m_activeB = (uint8_t)(cfg.ledB * factor);
        break;
    }

    case RgbEffect::Rainbow: {
        float hue = std::fmod(m_effectPhase * 50.0f, 360.0f);
        HsvToRgb(hue, 1.0f, 1.0f, m_activeR, m_activeG, m_activeB);
        break;
    }

    case RgbEffect::BatteryGauge: {
        // 100% = Green (0,255,0), 50% = Yellow (255,200,0), 10% = Red (255,0,0)
        float pct = st.batteryPercent / 100.0f;
        if (pct > 0.5f) {
            float t = (pct - 0.5f) * 2.0f;
            m_activeR = (uint8_t)((1.0f - t) * 255.0f);
            m_activeG = 255;
            m_activeB = 0;
        } else {
            float t = pct * 2.0f;
            m_activeR = 255;
            m_activeG = (uint8_t)(t * 200.0f);
            m_activeB = 0;
        }
        break;
    }

    default:
        m_activeR = cfg.ledR;
        m_activeG = cfg.ledG;
        m_activeB = cfg.ledB;
        break;
    }
}

void ControllerEngine::OutputThread() {
    uint8_t outBuf[128] = { 0 };

    while (m_running) {
        if (m_device.IsOpen()) {
            ControllerState state;
            ControllerConfig config;
            {
                std::lock_guard<std::mutex> lock(m_stateMutex);
                state = m_state;
                config = m_config;
            }

            UpdateRgbEffects();

            // Override config RGB with active effect color
            ControllerConfig activeCfg = config;
            activeCfg.ledR = m_activeR;
            activeCfg.ledG = m_activeG;
            activeCfg.ledB = m_activeB;

            uint8_t lRumble = m_leftRumble;
            uint8_t rRumble = m_rightRumble;
            bool micMuted = state.micMuted;

            // Audio-Reactive Haptics (WASAPI Loopback to controller voice-coil motors)
            uint8_t audioHapticL = 0;
            uint8_t audioHapticR = 0;
            if (config.audioHapticsEnabled) {
                m_audio.GetHapticRumble(audioHapticL, audioHapticR, config.hapticIntensity, config.hapticCutoffHz, config.hapticStereo);
                lRumble = (uint8_t)std::clamp((int)lRumble + (int)audioHapticL, 0, 255);
                rRumble = (uint8_t)std::clamp((int)rRumble + (int)audioHapticR, 0, 255);
            }
            state.hapticMotorL = audioHapticL;
            state.hapticMotorR = audioHapticR;

            // Audio-to-Trigger Mechanical Recoil Kick
            static int kickCountdownFrames = 0;
            if (config.audioHapticsEnabled && config.audioTriggerKick) {
                if (m_audio.CheckTriggerKick(config.audioTriggerThreshold)) {
                    // Trigger recoil kick: pulse triggers for ~100ms (approx 6 frames at 60Hz)
                    kickCountdownFrames = 6;
                }
            }

            if (kickCountdownFrames > 0) {
                kickCountdownFrames--;
                // Override trigger mode with physical recoil kick
                activeCfg.leftTrigger.mode = TriggerMode::Pulse;
                activeCfg.leftTrigger.startPos = 15;
                activeCfg.leftTrigger.force = 225;
                activeCfg.leftTrigger.frequency = 6;
                activeCfg.leftTrigger.endPos = 240;

                activeCfg.rightTrigger.mode = TriggerMode::Pulse;
                activeCfg.rightTrigger.startPos = 15;
                activeCfg.rightTrigger.force = 225;
                activeCfg.rightTrigger.frequency = 6;
                activeCfg.rightTrigger.endPos = 240;
            }

            // Power saving & Low battery overrides
            if (state.isIdle) {
                // Controller is asleep due to inactivity
                activeCfg.ledR = 0;
                activeCfg.ledG = 0;
                activeCfg.ledB = 0;
                activeCfg.playerLeds = 0;
                activeCfg.leftTrigger.mode = TriggerMode::Off;
                activeCfg.rightTrigger.mode = TriggerMode::Off;
                lRumble = 0;
                rRumble = 0;
            } else if (state.batteryPercent <= 15 && !state.isCharging && config.lowBatteryWarning) {
                // Pulse lightbar red every second when battery is critical (<15%)
                float pulse = (std::sin(m_effectPhase * 3.0f) + 1.0f) * 0.5f;
                activeCfg.ledR = (uint8_t)(pulse * 255.0f);
                activeCfg.ledG = 0;
                activeCfg.ledB = 0;
            }

            if (state.model == ControllerModel::DualSense || state.model == ControllerModel::DualSenseEdge) {
                if (state.connection == ConnectionType::USB) {
                    DualSenseProtocol::BuildOutputReportUSB(outBuf, sizeof(outBuf), activeCfg, lRumble, rRumble, micMuted);
                    m_device.WriteReport(outBuf, 48, 10);
                } else {
                    DualSenseProtocol::BuildOutputReportBT(outBuf, sizeof(outBuf), activeCfg, lRumble, rRumble, micMuted);
                    m_device.WriteReport(outBuf, 78, 10);
                }
            } else if (state.model == ControllerModel::DualShock4_v1 || state.model == ControllerModel::DualShock4_v2) {
                if (state.connection == ConnectionType::USB) {
                    DS4Protocol::BuildOutputReportUSB(outBuf, sizeof(outBuf), activeCfg, lRumble, rRumble);
                    m_device.WriteReport(outBuf, 32, 10);
                } else {
                    DS4Protocol::BuildOutputReportBT(outBuf, sizeof(outBuf), activeCfg, lRumble, rRumble);
                    m_device.WriteReport(outBuf, 78, 10);
                }
            }

            state.renderAudioDev = WideToUtf8(m_audio.GetActiveRenderDeviceName());
            state.captureAudioDev = WideToUtf8(m_audio.GetActiveCaptureDeviceName());

            // Broadcast state to desktop app via IPC at ~60Hz
            m_ipc.BroadcastState(state, activeCfg);
        }

        Sleep(16); // ~60 updates per second for output reports & IPC telemetry
    }
}

} // namespace DualDriver
