#pragma once

#include "Common.hpp"
#include <mmdeviceapi.h>
#include <endpointvolume.h>
#include <string>

namespace DualDriver {

class AudioSubsystem {
public:
    AudioSubsystem();
    ~AudioSubsystem();

    bool Initialize();
    void Shutdown();

    // Check & sync hardware mic mute button with Windows system microphone
    bool ProcessMicButton(bool micButtonPressed, bool syncEnabled);

    // Get current system microphone mute state
    bool IsSystemMicMuted();

    // Set system microphone mute state
    void SetSystemMicMute(bool mute);

    // Audio device information
    std::wstring GetActiveRenderDeviceName() const { return m_renderDeviceName; }
    std::wstring GetActiveCaptureDeviceName() const { return m_captureDeviceName; }
    bool HasDualSenseAudio() const { return m_hasDualSenseAudio; }

private:
    bool RefreshDevices();

    IMMDeviceEnumerator* m_pEnum = nullptr;
    IMMDevice* m_pCaptureDev = nullptr;
    IAudioEndpointVolume* m_pCaptureVolume = nullptr;

    bool m_initialized = false;
    bool m_hasDualSenseAudio = false;
    std::wstring m_renderDeviceName;
    std::wstring m_captureDeviceName;

    bool m_lastMicBtnState = false;
    bool m_currentMicMuted = false;
};

} // namespace DualDriver
