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

// 64-bit precision biquad stage
struct BiquadStage {
    double b0 = 1.0, b1 = 0.0, b2 = 0.0;
    double a1 = 0.0, a2 = 0.0;
    double x1 = 0.0, x2 = 0.0;
    double y1 = 0.0, y2 = 0.0;

    void SetLowPass(double sampleRate, double cutoffHz, double q);
    double Process(double in);
    void Reset();
};

// 4th-Order 24 dB/octave Cascaded Butterworth Low-Pass Filter
class AudioLowPass4thOrder {
public:
    void SetLowPass(double sampleRate, double cutoffHz);
    float Process(float in);
    void Reset();

private:
    BiquadStage m_stage1;
    BiquadStage m_stage2;
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
    std::atomic<int> m_targetCutoffHz{ 100 };
    int m_currentCutoffHz = 100;
    AudioLowPass4thOrder m_filterL;
    AudioLowPass4thOrder m_filterR;
};

} // namespace DualDriver
