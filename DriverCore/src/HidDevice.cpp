#include "HidDevice.hpp"
#include <setupapi.h>
#include <hidsdi.h>
#include <iostream>

#pragma comment(lib, "setupapi.lib")
#pragma comment(lib, "hid.lib")

namespace DualDriver {

HidDevice::HidDevice() {
    QueryPerformanceFrequency(&m_perfFreq);
    QueryPerformanceCounter(&m_hzCalcStartTime);
    QueryPerformanceCounter(&m_lastReportTime);
}

HidDevice::~HidDevice() {
    Close();
}

bool HidDevice::Open(const std::string& path) {
    Close();

    m_handle = CreateFileA(path.c_str(),
        GENERIC_READ | GENERIC_WRITE,
        FILE_SHARE_READ | FILE_SHARE_WRITE,
        NULL, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, NULL);

    if (m_handle == INVALID_HANDLE_VALUE) {
        // Try read-only
        m_handle = CreateFileA(path.c_str(),
            GENERIC_READ,
            FILE_SHARE_READ | FILE_SHARE_WRITE,
            NULL, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, NULL);
    }

    if (m_handle == INVALID_HANDLE_VALUE) {
        return false;
    }

    m_path = path;

    // Query attributes
    HIDD_ATTRIBUTES attr = { sizeof(HIDD_ATTRIBUTES) };
    if (HidD_GetAttributes(m_handle, &attr)) {
        m_vid = attr.VendorID;
        m_pid = attr.ProductID;
    }

    // Query caps
    PHIDP_PREPARSED_DATA pData = NULL;
    if (HidD_GetPreparsedData(m_handle, &pData)) {
        HIDP_CAPS caps;
        if (HidP_GetCaps(pData, &caps) == HIDP_STATUS_SUCCESS) {
            m_inReportLen = caps.InputReportByteLength;
            m_outReportLen = caps.OutputReportByteLength;
        }
        HidD_FreePreparsedData(pData);
    }

    // Set Windows HID driver queue depth for low-latency streaming
    (void)HidD_SetNumInputBuffers(m_handle, 64);

    m_ovRead.hEvent = CreateEvent(NULL, TRUE, FALSE, NULL);
    m_ovWrite.hEvent = CreateEvent(NULL, TRUE, FALSE, NULL);

    m_reportCounter = 0;
    m_currentHz = 0;
    m_totalReports = 0;
    QueryPerformanceCounter(&m_hzCalcStartTime);
    QueryPerformanceCounter(&m_lastReportTime);

    return true;
}

void HidDevice::Close() {
    if (m_handle != INVALID_HANDLE_VALUE) {
        CancelIo(m_handle);
        CloseHandle(m_handle);
        m_handle = INVALID_HANDLE_VALUE;
    }

    if (m_ovRead.hEvent) {
        CloseHandle(m_ovRead.hEvent);
        m_ovRead.hEvent = NULL;
    }
    if (m_ovWrite.hEvent) {
        CloseHandle(m_ovWrite.hEvent);
        m_ovWrite.hEvent = NULL;
    }
}

bool HidDevice::ReadReport(uint8_t* buffer, size_t bufferSize, DWORD timeoutMs) {
    if (m_handle == INVALID_HANDLE_VALUE || !buffer || bufferSize < m_inReportLen) {
        return false;
    }

    ResetEvent(m_ovRead.hEvent);
    DWORD bytesRead = 0;

    LARGE_INTEGER tBefore;
    QueryPerformanceCounter(&tBefore);

    BOOL ok = ReadFile(m_handle, buffer, m_inReportLen, &bytesRead, &m_ovRead);
    if (!ok) {
        DWORD err = GetLastError();
        if (err == ERROR_IO_PENDING) {
            DWORD waitRes = WaitForSingleObject(m_ovRead.hEvent, timeoutMs);
            if (waitRes == WAIT_OBJECT_0) {
                if (GetOverlappedResult(m_handle, &m_ovRead, &bytesRead, FALSE)) {
                    ok = TRUE;
                }
            } else {
                CancelIo(m_handle);
                return false;
            }
        } else {
            return false;
        }
    }

    if (ok && bytesRead > 0) {
        LARGE_INTEGER tAfter;
        QueryPerformanceCounter(&tAfter);

        // Calculate input processing latency in milliseconds
        float latency = (float)(tAfter.QuadPart - tBefore.QuadPart) * 1000.0f / (float)m_perfFreq.QuadPart;
        m_currentLatencyMs = latency;
        m_totalReports++;
        m_reportCounter++;

        // Update polling rate calculation once every 250ms
        float elapsedSec = (float)(tAfter.QuadPart - m_hzCalcStartTime.QuadPart) / (float)m_perfFreq.QuadPart;
        if (elapsedSec >= 0.25f) {
            m_currentHz = (uint32_t)(m_reportCounter / elapsedSec);
            m_reportCounter = 0;
            m_hzCalcStartTime = tAfter;
        }

        m_lastReportTime = tAfter;
        return true;
    }

    return false;
}

bool HidDevice::WriteReport(const uint8_t* buffer, size_t bufferSize, DWORD timeoutMs) {
    if (m_handle == INVALID_HANDLE_VALUE || !buffer) {
        return false;
    }

    ResetEvent(m_ovWrite.hEvent);
    DWORD bytesWritten = 0;

    DWORD sizeToWrite = (DWORD)bufferSize;
    if (m_outReportLen > 0 && sizeToWrite > m_outReportLen) {
        sizeToWrite = m_outReportLen;
    }

    BOOL ok = WriteFile(m_handle, buffer, sizeToWrite, &bytesWritten, &m_ovWrite);
    if (!ok) {
        DWORD err = GetLastError();
        if (err == ERROR_IO_PENDING) {
            DWORD waitRes = WaitForSingleObject(m_ovWrite.hEvent, timeoutMs);
            if (waitRes == WAIT_OBJECT_0) {
                if (GetOverlappedResult(m_handle, &m_ovWrite, &bytesWritten, FALSE)) {
                    ok = TRUE;
                }
            } else {
                CancelIo(m_handle);
                return false;
            }
        } else {
            return false;
        }
    }

    return (ok && bytesWritten > 0);
}

} // namespace DualDriver
