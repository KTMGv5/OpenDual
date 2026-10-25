#pragma once

#include "Common.hpp"
#include <string>
#include <thread>
#include <atomic>
#include <mutex>
#include <vector>
#include <functional>

namespace DualDriver {

class IpcServer {
public:
    using ConfigChangeCallback = std::function<void(const ControllerConfig& newConfig)>;

    IpcServer();
    ~IpcServer();

    bool Start(const std::string& pipeName = "\\\\.\\pipe\\OpenDualDriver");
    void Stop();

    void BroadcastState(const ControllerState& state, const ControllerConfig& config);
    void SetConfigChangeCallback(ConfigChangeCallback cb) { m_configCb = cb; }

private:
    void ServerThread();
    void HandleClient(HANDLE hPipe);
    void ProcessCommand(const std::string& jsonCmd);

    std::string m_pipeName;
    std::atomic<bool> m_running{ false };
    std::thread m_thread;
    std::mutex m_pipeMutex;
    std::vector<HANDLE> m_clientPipes;

    ConfigChangeCallback m_configCb;
    ControllerConfig m_lastConfig;
};

} // namespace DualDriver
