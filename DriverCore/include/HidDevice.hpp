#pragma once

#include "Common.hpp"
#include <string>
#include <vector>

namespace DualDriver {

class HidDevice {
public:
    HidDevice();
    ~HidDevice();

    bool Open(const std::string& path);
    void Close();
    bool IsOpen() const { return m_handle != INVALID_HANDLE_VALUE; }

    // Read single input report (blocking with timeout in ms)
    bool ReadReport(uint8_t* buffer, size_t bufferSize, DWORD timeoutMs = 10);

    // Send output report
    bool WriteReport(const uint8_t* buffer, size_t bufferSize, DWORD timeoutMs = 20);

    const std::string& GetPath() const { return m_path; }
    uint16_t GetVendorId() const { return m_vid; }
    uint16_t GetProductId() const { return m_pid; }
    uint16_t GetInputReportLength() const { return m_inReportLen; }
    uint16_t GetOutputReportLength() const { return m_outReportLen; }

    // Performance telemetry
    uint32_t GetCurrentPollingRate() const { return m_currentHz; }
    float GetCurrentLatencyMs() const { return m_currentLatencyMs; }
    uint64_t GetTotalReports() const { return m_totalReports; }

private:
    HANDLE m_handle = INVALID_HANDLE_VALUE;
    std::string m_path;
    uint16_t m_vid = 0;
    uint16_t m_pid = 0;
    uint16_t m_inReportLen = 64;
    uint16_t m_outReportLen = 48;

    OVERLAPPED m_ovRead = { 0 };
    OVERLAPPED m_ovWrite = { 0 };

    // Latency & polling calculation
    LARGE_INTEGER m_perfFreq = { 0 };
    LARGE_INTEGER m_lastReportTime = { 0 };
    uint32_t m_reportCounter = 0;
    uint32_t m_currentHz = 0;
    float m_currentLatencyMs = 0.0f;
    uint64_t m_totalReports = 0;
    LARGE_INTEGER m_hzCalcStartTime = { 0 };
};

} // namespace DualDriver
