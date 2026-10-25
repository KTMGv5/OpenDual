#include "AudioSubsystem.hpp"
#include <functiondiscoverykeys_devpkey.h>
#include <iostream>

#pragma comment(lib, "ole32.lib")

namespace DualDriver {

AudioSubsystem::AudioSubsystem() {
}

AudioSubsystem::~AudioSubsystem() {
    Shutdown();
}

bool AudioSubsystem::Initialize() {
    if (m_initialized) return true;

    HRESULT hr = CoInitialize(NULL);
    // RPC_E_CHANGED_MODE is ok if already initialized as MTA/STA
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
    return true;
}

void AudioSubsystem::Shutdown() {
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

    // Find DualSense Capture/Microphone device (or fall back to default console capture device)
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

    // If no DualSense mic explicitly found, bind to system default capture device
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
    // Detect rising edge: button just pressed
    if (micButtonPressed && !m_lastMicBtnState) {
        if (syncEnabled) {
            bool nowMuted = !IsSystemMicMuted();
            SetSystemMicMute(nowMuted);
            m_currentMicMuted = nowMuted;
        } else {
            m_currentMicMuted = !m_currentMicMuted;
        }
    } else if (syncEnabled) {
        // Keep in sync with external changes (e.g. user muted via Discord or Windows UI)
        m_currentMicMuted = IsSystemMicMuted();
    }

    m_lastMicBtnState = micButtonPressed;
    return m_currentMicMuted;
}

} // namespace DualDriver
