#include "AudioSubsystem.hpp"
#include <functiondiscoverykeys_devpkey.h>
#include <cmath>
#include <algorithm>
#include <iostream>

#pragma comment(lib, "ole32.lib")

namespace DualDriver {

// ============================================================================
// AudioBiquadFilter Implementation
// ============================================================================
void AudioBiquadFilter::SetLowPass(float sampleRate, float cutoffHz, float q) {
    if (sampleRate < 8000.0f) sampleRate = 48000.0f;
    cutoffHz = std::clamp(cutoffHz, 30.0f, sampleRate * 0.45f);

    float w0 = 2.0f * 3.1415926535f * cutoffHz / sampleRate;
    float alpha = std::sin(w0) / (2.0f * q);
    float cosw0 = std::cos(w0);

    float b0_raw = (1.0f - cosw0) * 0.5f;
    float b1_raw = 1.0f - cosw0;
    float b2_raw = (1.0f - cosw0) * 0.5f;
    float a0_raw = 1.0f + alpha;
    float a1_raw = -2.0f * cosw0;
    float a2_raw = 1.0f - alpha;

    b0 = b0_raw / a0_raw;
    b1 = b1_raw / a0_raw;
    b2 = b2_raw / a0_raw;
    a1 = a1_raw / a0_raw;
    a2 = a2_raw / a0_raw;
}

float AudioBiquadFilter::Process(float in) {
    float out = b0 * in + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
    x2 = x1;
    x1 = in;
    y2 = y1;
    y1 = out;
    return out;
}

void AudioBiquadFilter::Reset() {
    x1 = x2 = y1 = y2 = 0.0f;
}

// ============================================================================
// AudioSubsystem Implementation
// ============================================================================
AudioSubsystem::AudioSubsystem() {
}

AudioSubsystem::~AudioSubsystem() {
    Shutdown();
}

bool AudioSubsystem::Initialize() {
    if (m_initialized) return true;

    HRESULT hr = CoInitialize(NULL);
    if (FAILED(hr) && hr != RPC_E_CHANGED_MODE) {
        return false;
    }

    hr = CoCreateInstance(__uuidof(MMDeviceEnumerator), NULL, CLSCTX_ALL,
        __uuidof(IMMDeviceEnumerator), (void**)&m_pEnum);

    if (FAILED(hr) || !m_pEnum) {
        return false;
    }

    m_initialized = true;
    RefreshDevices();

    // Start background loopback listener for audio-reactive haptics
    StartLoopback();
    return true;
}

void AudioSubsystem::Shutdown() {
    StopLoopback();

    if (m_pCaptureVolume) {
        m_pCaptureVolume->Release();
        m_pCaptureVolume = nullptr;
    }
    if (m_pCaptureDev) {
        m_pCaptureDev->Release();
        m_pCaptureDev = nullptr;
    }
    if (m_pEnum) {
        m_pEnum->Release();
        m_pEnum = nullptr;
    }
    m_initialized = false;
}

bool AudioSubsystem::RefreshDevices() {
    if (!m_initialized || !m_pEnum) return false;

    if (m_pCaptureVolume) {
        m_pCaptureVolume->Release();
        m_pCaptureVolume = nullptr;
    }
    if (m_pCaptureDev) {
        m_pCaptureDev->Release();
        m_pCaptureDev = nullptr;
    }

    m_hasDualSenseAudio = false;
    m_renderDeviceName.clear();
    m_captureDeviceName.clear();

    // Find DualSense Capture/Microphone device
    IMMDeviceCollection* pCol = nullptr;
    if (SUCCEEDED(m_pEnum->EnumAudioEndpoints(eCapture, DEVICE_STATE_ACTIVE, &pCol))) {
        UINT count = 0;
        pCol->GetCount(&count);
        for (UINT i = 0; i < count; i++) {
            IMMDevice* pDev = nullptr;
            if (SUCCEEDED(pCol->Item(i, &pDev))) {
                IPropertyStore* pStore = nullptr;
                if (SUCCEEDED(pDev->OpenPropertyStore(STGM_READ, &pStore))) {
                    PROPVARIANT var;
                    PropVariantInit(&var);
                    if (SUCCEEDED(pStore->GetValue(PKEY_Device_FriendlyName, &var))) {
                        std::wstring name = var.pwszVal ? var.pwszVal : L"";
                        if (name.find(L"DualSense") != std::wstring::npos || name.find(L"Wireless Controller") != std::wstring::npos) {
                            m_hasDualSenseAudio = true;
                            m_captureDeviceName = name;
                            m_pCaptureDev = pDev;
                            pDev->AddRef();
                        }
                        PropVariantClear(&var);
                    }
                    pStore->Release();
                }
                pDev->Release();
                if (m_pCaptureDev) break;
            }
        }
        pCol->Release();
    }

    // Fall back to system default capture device
    if (!m_pCaptureDev) {
        m_pEnum->GetDefaultAudioEndpoint(eCapture, eConsole, &m_pCaptureDev);
    }

    if (m_pCaptureDev) {
        m_pCaptureDev->Activate(__uuidof(IAudioEndpointVolume), CLSCTX_ALL, NULL, (void**)&m_pCaptureVolume);
        
        if (m_captureDeviceName.empty()) {
            IPropertyStore* pStore = nullptr;
            if (SUCCEEDED(m_pCaptureDev->OpenPropertyStore(STGM_READ, &pStore))) {
                PROPVARIANT var;
                PropVariantInit(&var);
                if (SUCCEEDED(pStore->GetValue(PKEY_Device_FriendlyName, &var))) {
                    if (var.pwszVal) m_captureDeviceName = var.pwszVal;
                    PropVariantClear(&var);
                }
                pStore->Release();
            }
        }
    }

    // Check Render device name
    IMMDevice* pRender = nullptr;
    if (SUCCEEDED(m_pEnum->GetDefaultAudioEndpoint(eRender, eConsole, &pRender)) && pRender) {
        IPropertyStore* pStore = nullptr;
        if (SUCCEEDED(pRender->OpenPropertyStore(STGM_READ, &pStore))) {
            PROPVARIANT var;
            PropVariantInit(&var);
            if (SUCCEEDED(pStore->GetValue(PKEY_Device_FriendlyName, &var))) {
                if (var.pwszVal) m_renderDeviceName = var.pwszVal;
                PropVariantClear(&var);
            }
            pStore->Release();
        }
        pRender->Release();
    }

    m_currentMicMuted = IsSystemMicMuted();
    return true;
}

bool AudioSubsystem::IsSystemMicMuted() {
    if (!m_pCaptureVolume) return false;
    BOOL muted = FALSE;
    if (SUCCEEDED(m_pCaptureVolume->GetMute(&muted))) {
        m_currentMicMuted = (muted == TRUE);
        return m_currentMicMuted;
    }
    return false;
}

void AudioSubsystem::SetSystemMicMute(bool mute) {
    if (!m_pCaptureVolume) return;
    m_pCaptureVolume->SetMute(mute ? TRUE : FALSE, NULL);
    m_currentMicMuted = mute;
}

bool AudioSubsystem::ProcessMicButton(bool micButtonPressed, bool syncEnabled) {
    if (micButtonPressed && !m_lastMicBtnState) {
        if (syncEnabled) {
            bool nowMuted = !IsSystemMicMuted();
            SetSystemMicMute(nowMuted);
            m_currentMicMuted = nowMuted;
        } else {
            m_currentMicMuted = !m_currentMicMuted;
        }
    } else if (syncEnabled) {
        m_currentMicMuted = IsSystemMicMuted();
    }

    m_lastMicBtnState = micButtonPressed;
    return m_currentMicMuted;
}

// ============================================================================
// WASAPI Audio-Reactive Haptics
// ============================================================================
bool AudioSubsystem::StartLoopback() {
    if (m_loopbackRunning) return true;
    if (!m_pEnum) return false;

    m_loopbackRunning = true;
    m_loopbackThread = std::thread(&AudioSubsystem::LoopbackCaptureThread, this);
    return true;
}

void AudioSubsystem::StopLoopback() {
    if (!m_loopbackRunning) return;
    m_loopbackRunning = false;

    if (m_loopbackThread.joinable()) {
        m_loopbackThread.join();
    }
}

void AudioSubsystem::LoopbackCaptureThread() {
    HRESULT hr = CoInitialize(NULL);

    IMMDevice* pRender = nullptr;
    hr = m_pEnum->GetDefaultAudioEndpoint(eRender, eConsole, &pRender);
    if (FAILED(hr) || !pRender) {
        if (hr == S_OK || hr == S_FALSE) CoUninitialize();
        return;
    }

    IAudioClient* pClient = nullptr;
    hr = pRender->Activate(__uuidof(IAudioClient), CLSCTX_ALL, NULL, (void**)&pClient);
    pRender->Release();
    if (FAILED(hr) || !pClient) {
        if (hr == S_OK || hr == S_FALSE) CoUninitialize();
        return;
    }

    WAVEFORMATEX* pFormat = nullptr;
    hr = pClient->GetMixFormat(&pFormat);
    if (FAILED(hr) || !pFormat) {
        pClient->Release();
        if (hr == S_OK || hr == S_FALSE) CoUninitialize();
        return;
    }

    // AUDCLNT_STREAMFLAGS_LOOPBACK captures playback stream from Windows audio engine
    REFERENCE_TIME hnsBufferDuration = 1000000; // 100ms
    hr = pClient->Initialize(AUDCLNT_SHAREMODE_SHARED,
                             AUDCLNT_STREAMFLAGS_LOOPBACK,
                             hnsBufferDuration, 0, pFormat, NULL);
    if (FAILED(hr)) {
        CoTaskMemFree(pFormat);
        pClient->Release();
        if (hr == S_OK || hr == S_FALSE) CoUninitialize();
        return;
    }

    IAudioCaptureClient* pCapture = nullptr;
    hr = pClient->GetService(__uuidof(IAudioCaptureClient), (void**)&pCapture);
    if (FAILED(hr) || !pCapture) {
        CoTaskMemFree(pFormat);
        pClient->Release();
        if (hr == S_OK || hr == S_FALSE) CoUninitialize();
        return;
    }

    hr = pClient->Start();
    if (FAILED(hr)) {
        pCapture->Release();
        CoTaskMemFree(pFormat);
        pClient->Release();
        if (hr == S_OK || hr == S_FALSE) CoUninitialize();
        return;
    }

    float sampleRate = (float)pFormat->nSamplesPerSec;
    if (sampleRate < 8000.0f) sampleRate = 48000.0f;
    m_filterL.SetLowPass(sampleRate, (float)m_currentCutoffHz);
    m_filterR.SetLowPass(sampleRate, (float)m_currentCutoffHz);

    while (m_loopbackRunning) {
        UINT32 packetLength = 0;
        hr = pCapture->GetNextPacketSize(&packetLength);
        if (FAILED(hr)) break;

        while (packetLength > 0) {
            BYTE* pData = nullptr;
            UINT32 numFrames = 0;
            DWORD flags = 0;

            hr = pCapture->GetBuffer(&pData, &numFrames, &flags, NULL, NULL);
            if (SUCCEEDED(hr)) {
                if (!(flags & AUDCLNT_BUFFERFLAGS_SILENT) && pData && numFrames > 0) {
                    ProcessAudioSamples(pData, numFrames, pFormat);
                } else {
                    std::lock_guard<std::mutex> lock(m_hapticMutex);
                    m_peakL *= 0.92f;
                    m_peakR *= 0.92f;
                }
                pCapture->ReleaseBuffer(numFrames);
            }
            pCapture->GetNextPacketSize(&packetLength);
        }

        Sleep(8); // High update rate (~120Hz)
    }

    pClient->Stop();
    pCapture->Release();
    CoTaskMemFree(pFormat);
    pClient->Release();
    if (hr == S_OK || hr == S_FALSE) CoUninitialize();
}

void AudioSubsystem::ProcessAudioSamples(BYTE* pData, UINT32 numFrames, WAVEFORMATEX* pFormat) {
    int channels = pFormat->nChannels;
    if (channels < 1) return;

    bool isFloat = (pFormat->wFormatTag == WAVE_FORMAT_IEEE_FLOAT);
    if (pFormat->wFormatTag == WAVE_FORMAT_EXTENSIBLE) {
        WAVEFORMATEXTENSIBLE* pExt = (WAVEFORMATEXTENSIBLE*)pFormat;
        if (pExt->SubFormat == KSDATAFORMAT_SUBTYPE_IEEE_FLOAT) {
            isFloat = true;
        }
    }

    float localPeakL = 0.0f;
    float localPeakR = 0.0f;

    if (isFloat && pFormat->wBitsPerSample == 32) {
        float* pSamples = (float*)pData;
        for (UINT32 i = 0; i < numFrames; i++) {
            float sL = pSamples[i * channels + 0];
            float sR = pSamples[i * channels + (channels > 1 ? 1 : 0)];

            float fL = m_filterL.Process(sL);
            float fR = m_filterR.Process(sR);

            float aL = std::abs(fL);
            float aR = std::abs(fR);
            if (aL > localPeakL) localPeakL = aL;
            if (aR > localPeakR) localPeakR = aR;
        }
    } else if (pFormat->wBitsPerSample == 16) {
        int16_t* pSamples = (int16_t*)pData;
        for (UINT32 i = 0; i < numFrames; i++) {
            float sL = pSamples[i * channels + 0] / 32768.0f;
            float sR = pSamples[i * channels + (channels > 1 ? 1 : 0)] / 32768.0f;

            float fL = m_filterL.Process(sL);
            float fR = m_filterR.Process(sR);

            float aL = std::abs(fL);
            float aR = std::abs(fR);
            if (aL > localPeakL) localPeakL = aL;
            if (aR > localPeakR) localPeakR = aR;
        }
    }

    std::lock_guard<std::mutex> lock(m_hapticMutex);
    // Instant attack, smooth sustain
    if (localPeakL > m_peakL) m_peakL = localPeakL;
    else m_peakL = m_peakL * 0.82f + localPeakL * 0.18f;

    if (localPeakR > m_peakR) m_peakR = localPeakR;
    else m_peakR = m_peakR * 0.82f + localPeakR * 0.18f;
}

void AudioSubsystem::GetHapticRumble(uint8_t& outLeft, uint8_t& outRight, float intensity, int cutoffHz, bool stereo) {
    std::lock_guard<std::mutex> lock(m_hapticMutex);

    if (cutoffHz != m_currentCutoffHz && cutoffHz >= 40 && cutoffHz <= 300) {
        m_currentCutoffHz = cutoffHz;
        m_filterL.SetLowPass(48000.0f, (float)cutoffHz);
        m_filterR.SetLowPass(48000.0f, (float)cutoffHz);
    }

    // Natural decay over ~16ms tick
    m_peakL *= 0.94f;
    m_peakR *= 0.94f;

    // Noise gate threshold: filters out low-level ambient hum
    const float noiseGate = 0.015f;

    auto mapAmp = [noiseGate, intensity](float peak) -> uint8_t {
        if (peak <= noiseGate) return 0;
        float norm = (peak - noiseGate) / (0.70f - noiseGate);
        if (norm > 1.0f) norm = 1.0f;
        // Non-linear punch curve
        float punch = std::pow(norm, 0.75f);
        int val = (int)(punch * 255.0f * intensity);
        return (uint8_t)std::clamp(val, 0, 255);
    };

    uint8_t rL = mapAmp(m_peakL);
    uint8_t rR = mapAmp(m_peakR);

    if (stereo) {
        outLeft = rL;
        outRight = rR;
    } else {
        uint8_t m = (rL > rR) ? rL : rR;
        outLeft = m;
        outRight = m;
    }
}

} // namespace DualDriver
