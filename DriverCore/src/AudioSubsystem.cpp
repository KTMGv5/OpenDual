#include "AudioSubsystem.hpp"
#include <functiondiscoverykeys_devpkey.h>
#include <cmath>
#include <algorithm>
#include <iostream>

#pragma comment(lib, "ole32.lib")

namespace DualDriver {

// ============================================================================
// 64-bit Biquad Stage Implementation
// ============================================================================
void BiquadStage::SetLowPass(double sampleRate, double cutoffHz, double q) {
    if (sampleRate < 8000.0) sampleRate = 48000.0;
    cutoffHz = std::clamp(cutoffHz, 20.0, sampleRate * 0.45);

    double w0 = 2.0 * 3.14159265358979323846 * cutoffHz / sampleRate;
    double alpha = std::sin(w0) / (2.0 * q);
    double cosw0 = std::cos(w0);

    double b0_raw = (1.0 - cosw0) * 0.5;
    double b1_raw = 1.0 - cosw0;
    double b2_raw = (1.0 - cosw0) * 0.5;
    double a0_raw = 1.0 + alpha;
    double a1_raw = -2.0 * cosw0;
    double a2_raw = 1.0 - alpha;

    b0 = b0_raw / a0_raw;
    b1 = b1_raw / a0_raw;
    b2 = b2_raw / a0_raw;
    a1 = a1_raw / a0_raw;
    a2 = a2_raw / a0_raw;
}

double BiquadStage::Process(double in) {
    double out = b0 * in + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;
    x2 = x1;
    x1 = in;
    y2 = y1;
    y1 = out;
    return out;
}

void BiquadStage::Reset() {
    x1 = x2 = y1 = y2 = 0.0;
}

// ============================================================================
// 4th-Order (24 dB/octave) Cascaded Butterworth Low-Pass Filter
// ============================================================================
void AudioLowPass4thOrder::SetLowPass(double sampleRate, double cutoffHz) {
    // Stage 1: Q1 = 1 / (2 * cos(pi / 8)) = 0.5411961
    // Stage 2: Q2 = 1 / (2 * cos(3*pi / 8)) = 1.3065630
    m_stage1.SetLowPass(sampleRate, cutoffHz, 0.541196100146197);
    m_stage2.SetLowPass(sampleRate, cutoffHz, 1.3065629648763765);
}

float AudioLowPass4thOrder::Process(float in) {
    double s1 = m_stage1.Process((double)in);
    double s2 = m_stage2.Process(s1);
    return (float)s2;
}

void AudioLowPass4thOrder::Reset() {
    m_stage1.Reset();
    m_stage2.Reset();
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

    // Register notification callback for dynamic Windows default audio endpoint switching
    m_pEnum->RegisterEndpointNotificationCallback(&m_notifyClient);

    RefreshDevices();

    // Start background loopback listener for audio-reactive haptics
    StartLoopback();
    return true;
}

void AudioSubsystem::Shutdown() {
    StopLoopback();

    if (m_pEnum) {
        m_pEnum->UnregisterEndpointNotificationCallback(&m_notifyClient);
    }

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

    {
        std::lock_guard<std::mutex> lock(m_hapticMutex);
        m_hasDualSenseAudio = false;
        m_renderDeviceName.clear();
        m_captureDeviceName.clear();
    }

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
                    if (var.pwszVal) {
                        std::lock_guard<std::mutex> lock(m_hapticMutex);
                        m_captureDeviceName = var.pwszVal;
                    }
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
                if (var.pwszVal) {
                    std::lock_guard<std::mutex> lock(m_hapticMutex);
                    m_renderDeviceName = var.pwszVal;
                }
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
    if (m_captureDeviceChanged.exchange(false)) {
        RefreshDevices();
    }

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

    while (m_loopbackRunning) {
        m_defaultDeviceChanged.store(false);

        IMMDevice* pRender = nullptr;
        hr = m_pEnum->GetDefaultAudioEndpoint(eRender, eConsole, &pRender);
        if (FAILED(hr) || !pRender) {
            // No default render endpoint currently active, wait and retry
            for (int i = 0; i < 20 && m_loopbackRunning && !m_defaultDeviceChanged.load(); i++) {
                Sleep(50);
            }
            continue;
        }

        // Cache render device friendly name
        {
            IPropertyStore* pStore = nullptr;
            if (SUCCEEDED(pRender->OpenPropertyStore(STGM_READ, &pStore))) {
                PROPVARIANT var;
                PropVariantInit(&var);
                if (SUCCEEDED(pStore->GetValue(PKEY_Device_FriendlyName, &var)) && var.pwszVal) {
                    std::lock_guard<std::mutex> lock(m_hapticMutex);
                    m_renderDeviceName = var.pwszVal;
                }
                PropVariantClear(&var);
                pStore->Release();
            }
        }

        IAudioClient* pClient = nullptr;
        hr = pRender->Activate(__uuidof(IAudioClient), CLSCTX_ALL, NULL, (void**)&pClient);
        pRender->Release();
        pRender = nullptr;

        if (FAILED(hr) || !pClient) {
            Sleep(100);
            continue;
        }

        WAVEFORMATEX* pFormat = nullptr;
        hr = pClient->GetMixFormat(&pFormat);
        if (FAILED(hr) || !pFormat) {
            pClient->Release();
            Sleep(100);
            continue;
        }

        // AUDCLNT_STREAMFLAGS_LOOPBACK captures playback stream from Windows audio engine
        REFERENCE_TIME hnsBufferDuration = 1000000; // 100ms
        hr = pClient->Initialize(AUDCLNT_SHAREMODE_SHARED,
                                 AUDCLNT_STREAMFLAGS_LOOPBACK,
                                 hnsBufferDuration, 0, pFormat, NULL);
        if (FAILED(hr)) {
            CoTaskMemFree(pFormat);
            pClient->Release();
            Sleep(100);
            continue;
        }

        IAudioCaptureClient* pCapture = nullptr;
        hr = pClient->GetService(__uuidof(IAudioCaptureClient), (void**)&pCapture);
        if (FAILED(hr) || !pCapture) {
            CoTaskMemFree(pFormat);
            pClient->Release();
            Sleep(100);
            continue;
        }

        hr = pClient->Start();
        if (FAILED(hr)) {
            pCapture->Release();
            CoTaskMemFree(pFormat);
            pClient->Release();
            Sleep(100);
            continue;
        }

        float sampleRate = (float)pFormat->nSamplesPerSec;
        if (sampleRate < 8000.0f) sampleRate = 48000.0f;
        m_sampleRate = sampleRate;
        m_filterL.SetLowPass((double)m_sampleRate, (double)m_currentCutoffHz);
        m_filterR.SetLowPass((double)m_sampleRate, (double)m_currentCutoffHz);

        // Dynamic capture loop on active endpoint
        while (m_loopbackRunning && !m_defaultDeviceChanged.load()) {
            UINT32 packetLength = 0;
            hr = pCapture->GetNextPacketSize(&packetLength);
            if (FAILED(hr)) {
                // Device was invalidated (e.g. AUDCLNT_E_DEVICE_INVALIDATED 0x88890004) or endpoint changed
                break;
            }

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
                        m_peakL *= 0.80f;
                        m_peakR *= 0.80f;
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

        // Small grace period for Windows audio routing engine to finish switching
        if (m_loopbackRunning && m_defaultDeviceChanged.load()) {
            Sleep(50);
        }
    }

    if (hr == S_OK || hr == S_FALSE) CoUninitialize();
}

void AudioSubsystem::ProcessAudioSamples(BYTE* pData, UINT32 numFrames, WAVEFORMATEX* pFormat) {
    int channels = pFormat->nChannels;
    if (channels < 1 || numFrames == 0 || !pData) return;

    // Check if target cutoff changed and recompute 4th-order filter
    int targetCutoff = m_targetCutoffHz.load();
    if (targetCutoff != m_currentCutoffHz) {
        m_currentCutoffHz = targetCutoff;
        m_filterL.SetLowPass((double)m_sampleRate, (double)m_currentCutoffHz);
        m_filterR.SetLowPass((double)m_sampleRate, (double)m_currentCutoffHz);
    }

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

            // If 4-channel Quadraphonic (DualSense USB Audio):
            // Channels 0 & 1 = Headphone Audio, Channels 2 & 3 = Dedicated Voice-Coil Haptics
            if (channels == 4) {
                float hL = pSamples[i * channels + 2];
                float hR = pSamples[i * channels + 3];
                if (std::abs(hL) > 0.001f || std::abs(hR) > 0.001f) {
                    sL = sL * 0.5f + hL;
                    sR = sR * 0.5f + hR;
                }
            } else if (channels >= 6) {
                // 5.1 / 7.1 Surround setups: Channel 3 is standard LFE (Subwoofer)
                float lfe = pSamples[i * channels + 3];
                sL += lfe * 0.5f;
                sR += lfe * 0.5f;
            }

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

            if (channels == 4) {
                float hL = pSamples[i * channels + 2] / 32768.0f;
                float hR = pSamples[i * channels + 3] / 32768.0f;
                if (std::abs(hL) > 0.001f || std::abs(hR) > 0.001f) {
                    sL = sL * 0.5f + hL;
                    sR = sR * 0.5f + hR;
                }
            } else if (channels >= 6) {
                float lfe = pSamples[i * channels + 3] / 32768.0f;
                sL += lfe * 0.5f;
                sR += lfe * 0.5f;
            }

            float fL = m_filterL.Process(sL);
            float fR = m_filterR.Process(sR);

            float aL = std::abs(fL);
            float aR = std::abs(fR);
            if (aL > localPeakL) localPeakL = aL;
            if (aR > localPeakR) localPeakR = aR;
        }
    } else if (pFormat->wBitsPerSample == 24) {
        BYTE* pBytes = pData;
        int bytesPerFrame = pFormat->nBlockAlign;
        for (UINT32 i = 0; i < numFrames; i++) {
            BYTE* framePtr = pBytes + i * bytesPerFrame;
            int32_t rawL = (framePtr[0]) | (framePtr[1] << 8) | ((int8_t)framePtr[2] << 16);
            int32_t rawR = rawL;
            if (channels > 1) {
                rawR = (framePtr[3]) | (framePtr[4] << 8) | ((int8_t)framePtr[5] << 16);
            }
            float sL = rawL / 8388608.0f;
            float sR = rawR / 8388608.0f;

            float fL = m_filterL.Process(sL);
            float fR = m_filterR.Process(sR);

            float aL = std::abs(fL);
            float aR = std::abs(fR);
            if (aL > localPeakL) localPeakL = aL;
            if (aR > localPeakR) localPeakR = aR;
        }
    } else if (pFormat->wBitsPerSample == 32 && !isFloat) {
        int32_t* pSamples = (int32_t*)pData;
        for (UINT32 i = 0; i < numFrames; i++) {
            float sL = pSamples[i * channels + 0] / 2147483648.0f;
            float sR = pSamples[i * channels + (channels > 1 ? 1 : 0)] / 2147483648.0f;

            float fL = m_filterL.Process(sL);
            float fR = m_filterR.Process(sR);

            float aL = std::abs(fL);
            float aR = std::abs(fR);
            if (aL > localPeakL) localPeakL = aL;
            if (aR > localPeakR) localPeakR = aR;
        }
    }

    std::lock_guard<std::mutex> lock(m_hapticMutex);
    // Instant attack on rising peaks, fast musical release on decay (~60ms)
    if (localPeakL > m_peakL) {
        m_peakL = localPeakL;
    } else {
        m_peakL = m_peakL * 0.88f + localPeakL * 0.12f;
    }

    if (localPeakR > m_peakR) {
        m_peakR = localPeakR;
    } else {
        m_peakR = m_peakR * 0.88f + localPeakR * 0.12f;
    }
}

void AudioSubsystem::GetHapticRumble(uint8_t& outLeft, uint8_t& outRight, float intensity, int cutoffHz, bool stereo) {
    if (cutoffHz >= 30 && cutoffHz <= 350) {
        m_targetCutoffHz.store(cutoffHz);
    }

    std::lock_guard<std::mutex> lock(m_hapticMutex);

    // Fast envelope release (~70ms half-life at 60Hz) to silence rumble between drum beats/footsteps
    m_peakL *= 0.86f;
    m_peakR *= 0.86f;

    // Musical noise gate: eliminates continuous ambient room noise, dialogue bleed, and system hiss
    const float noiseGate = 0.024f;
    const float nominalMax = 0.20f;

    auto mapAmp = [noiseGate, nominalMax, intensity](float peak) -> uint8_t {
        if (peak <= noiseGate || intensity <= 0.01f) return 0;
        float norm = (peak - noiseGate) / (nominalMax - noiseGate);
        if (norm > 1.0f) norm = 1.0f;

        // Quadratic dynamic expansion (punch curve):
        // Suppresses continuous low-level drones while providing crisp, hard-hitting tactile punch
        float punch = norm * norm;
        float scaled = punch * intensity;
        if (scaled > 1.0f) scaled = 1.0f;
        return (uint8_t)std::clamp((int)(scaled * 255.0f), 0, 255);
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

bool AudioSubsystem::CheckTriggerKick(float threshold) {
    std::lock_guard<std::mutex> lock(m_hapticMutex);
    float currentMax = (m_peakL > m_peakR) ? m_peakL : m_peakR;

    // Transient detection: difference between instantaneous peak and low-passed envelope
    float delta = currentMax - m_lastPeak;
    m_lastPeak = m_lastPeak * 0.70f + currentMax * 0.30f; // Track baseline

    static ULONGLONG s_lastKickTick = 0;
    ULONGLONG now = GetTickCount64();

    // Must exceed threshold, have sharp positive onset (delta > 0.10f), and honor cooldown (120ms)
    if (currentMax >= threshold && delta >= 0.10f && (now - s_lastKickTick >= 120)) {
        s_lastKickTick = now;
        return true;
    }
    return false;
}

} // namespace DualDriver
