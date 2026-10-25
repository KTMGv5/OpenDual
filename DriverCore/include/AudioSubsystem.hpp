#pragma once

#include "Common.hpp"
#include <mmdeviceapi.h>
#include <endpointvolume.h>
#include <audioclient.h>
#include <string>
#include <thread>
#include <atomic>
#include <mutex>

namespace DualDriver {

struct AudioBiquadFilter {
    float b0 = 0.0f, b1 = 0.0f, b2 = 0.0f;
    float a1 = 0.0f, a2 = 0.0f;
    float x1 = 0.0f, x2 = 0.0f;
    float y1 = 0.0f, y2 = 0.0f;

    void SetLowPass(float sampleRate, float cutoffHz, float q = 0.707f);
    float Process(float in);
    void Reset();
};

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

    // Audio-Reactive Haptics
    bool StartLoopback();
    void StopLoopback();
    bool IsLoopbackRunning() const { return m_loopbackRunning; }
    void GetHapticRumble(uint8_t& outLeft, uint8_t& outRight, float intensity, int cutoffHz, bool stereo);
    bool CheckTriggerKick(float threshold = 0.45f);

    // Audio device information
    std::wstring GetActiveRenderDeviceName() const { return m_renderDeviceName; }
    std::wstring GetActiveCaptureDeviceName() const { return m_captureDeviceName; }
    bool HasDualSenseAudio() const { return m_hasDualSenseAudio; }

private:
    bool RefreshDevices();
    void LoopbackCaptureThread();
    void ProcessAudioSamples(BYTE* pData, UINT32 numFrames, WAVEFORMATEX* pFormat);

    IMMDeviceEnumerator* m_pEnum = nullptr;
    IMMDevice* m_pCaptureDev = nullptr;
    IAudioEndpointVolume* m_pCaptureVolume = nullptr;

    bool m_initialized = false;
    bool m_hasDualSenseAudio = false;
    std::wstring m_renderDeviceName;
    std::wstring m_captureDeviceName;

    bool m_lastMicBtnState = false;
    bool m_currentMicMuted = false;

    // Loopback Audio Haptics State
    std::atomic<bool> m_loopbackRunning{ false };
    std::thread m_loopbackThread;
    std::mutex m_hapticMutex;
    float m_peakL = 0.0f;
    float m_peakR = 0.0f;
    float m_lastPeak = 0.0f;
    float m_sampleRate = 48000.0f;
    int m_currentCutoffHz = 100;
    AudioBiquadFilter m_filterL;
    AudioBiquadFilter m_filterR;
};

} // namespace DualDriver
