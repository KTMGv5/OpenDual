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

// WASAPI Endpoint Change Notification Client for Real-Time Device Hot-Plugging & Output Routing
class AudioNotificationClient : public IMMNotificationClient {
public:
    AudioNotificationClient(std::atomic<bool>& renderChanged, std::atomic<bool>& captureChanged)
        : m_renderChanged(renderChanged), m_captureChanged(captureChanged) {}

    // IUnknown methods
    STDMETHODIMP QueryInterface(REFIID riid, void** ppv) override {
        if (!ppv) return E_POINTER;
        if (riid == __uuidof(IUnknown) || riid == __uuidof(IMMNotificationClient)) {
            *ppv = static_cast<IMMNotificationClient*>(this);
            AddRef();
            return S_OK;
        }
        *ppv = nullptr;
        return E_NOINTERFACE;
    }

    STDMETHODIMP_(ULONG) AddRef() override {
        return ++m_refCount;
    }

    STDMETHODIMP_(ULONG) Release() override {
        return --m_refCount;
    }

    // IMMNotificationClient methods
    STDMETHODIMP OnDefaultDeviceChanged(EDataFlow flow, ERole role, LPCWSTR pwstrDeviceId) override {
        if (flow == eRender && (role == eConsole || role == eMultimedia)) {
            m_renderChanged.store(true);
        } else if (flow == eCapture && (role == eConsole || role == eCommunications)) {
            m_captureChanged.store(true);
        }
        return S_OK;
    }

    STDMETHODIMP OnDeviceStateChanged(LPCWSTR pwstrDeviceId, DWORD dwNewState) override {
        m_renderChanged.store(true);
        m_captureChanged.store(true);
        return S_OK;
    }

    STDMETHODIMP OnDeviceAdded(LPCWSTR pwstrDeviceId) override {
        return S_OK;
    }

    STDMETHODIMP OnDeviceRemoved(LPCWSTR pwstrDeviceId) override {
        m_renderChanged.store(true);
        m_captureChanged.store(true);
        return S_OK;
    }

    STDMETHODIMP OnPropertyValueChanged(LPCWSTR pwstrDeviceId, const PROPERTYKEY key) override {
        return S_OK;
    }

private:
    std::atomic<bool>& m_renderChanged;
    std::atomic<bool>& m_captureChanged;
    std::atomic<ULONG> m_refCount{ 1 };
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
    std::wstring GetActiveRenderDeviceName() const {
        std::lock_guard<std::mutex> lock(m_hapticMutex);
        return m_renderDeviceName;
    }
    std::wstring GetActiveCaptureDeviceName() const {
        std::lock_guard<std::mutex> lock(m_hapticMutex);
        return m_captureDeviceName;
    }
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

    // Dynamic Default Audio Endpoint Notifications
    std::atomic<bool> m_defaultDeviceChanged{ false };
    std::atomic<bool> m_captureDeviceChanged{ false };
    AudioNotificationClient m_notifyClient{ m_defaultDeviceChanged, m_captureDeviceChanged };

    // Loopback Audio Haptics State
    std::atomic<bool> m_loopbackRunning{ false };
    std::thread m_loopbackThread;
    mutable std::mutex m_hapticMutex;
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
