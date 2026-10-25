#include "IpcServer.hpp"
#include <iostream>
#include <sstream>

namespace DualDriver {

IpcServer::IpcServer() {
}

IpcServer::~IpcServer() {
    Stop();
}

bool IpcServer::Start(const std::string& pipeName) {
    if (m_running) return true;

    m_pipeName = pipeName;
    m_running = true;
    m_thread = std::thread(&IpcServer::ServerThread, this);
    return true;
}

void IpcServer::Stop() {
    if (!m_running) return;

    m_running = false;

    // Create a dummy client connection to unblock ConnectNamedPipe
    HANDLE hDummy = CreateFileA(m_pipeName.c_str(),
        GENERIC_READ | GENERIC_WRITE, 0, NULL, OPEN_EXISTING, 0, NULL);
    if (hDummy != INVALID_HANDLE_VALUE) {
        CloseHandle(hDummy);
    }

    if (m_thread.joinable()) {
        m_thread.join();
    }

    std::lock_guard<std::mutex> lock(m_pipeMutex);
    for (HANDLE h : m_clientPipes) {
        DisconnectNamedPipe(h);
        CloseHandle(h);
    }
    m_clientPipes.clear();
}

void IpcServer::ServerThread() {
    while (m_running) {
        HANDLE hPipe = CreateNamedPipeA(
            m_pipeName.c_str(),
            PIPE_ACCESS_DUPLEX,
            PIPE_TYPE_MESSAGE | PIPE_READMODE_MESSAGE | PIPE_WAIT,
            PIPE_UNLIMITED_INSTANCES,
            8192, 8192, 0, NULL);

        if (hPipe == INVALID_HANDLE_VALUE) {
            Sleep(100);
            continue;
        }

        BOOL connected = ConnectNamedPipe(hPipe, NULL) ? TRUE : (GetLastError() == ERROR_PIPE_CONNECTED);
        if (connected && m_running) {
            {
                std::lock_guard<std::mutex> lock(m_pipeMutex);
                m_clientPipes.push_back(hPipe);
            }
            std::thread(&IpcServer::HandleClient, this, hPipe).detach();
        } else {
            CloseHandle(hPipe);
        }
    }
}

void IpcServer::HandleClient(HANDLE hPipe) {
    char buffer[4096];
    DWORD bytesRead = 0;

    while (m_running) {
        DWORD bytesAvailable = 0;
        if (!PeekNamedPipe(hPipe, NULL, 0, NULL, &bytesAvailable, NULL)) {
            // Client pipe broken/disconnected
            break;
        }

        if (bytesAvailable > 0) {
            DWORD toRead = (bytesAvailable < sizeof(buffer) - 1) ? bytesAvailable : (DWORD)(sizeof(buffer) - 1);
            BOOL ok = ReadFile(hPipe, buffer, toRead, &bytesRead, NULL);
            if (ok && bytesRead > 0) {
                buffer[bytesRead] = '\0';
                ProcessCommand(std::string(buffer, bytesRead));
            }
        } else {
            Sleep(15);
        }
    }

    // Client disconnected
    {
        std::lock_guard<std::mutex> lock(m_pipeMutex);
        auto it = std::find(m_clientPipes.begin(), m_clientPipes.end(), hPipe);
        if (it != m_clientPipes.end()) {
            m_clientPipes.erase(it);
        }
    }
    DisconnectNamedPipe(hPipe);
    CloseHandle(hPipe);
}

void IpcServer::ProcessCommand(const std::string& msg) {
    // Basic JSON command parser
    if (!m_configCb) return;

    ControllerConfig cfg = m_lastConfig;
    bool changed = false;

    // Helper to find integer values: "key": 123
    auto getInt = [&](const std::string& key, int& out) {
        size_t pos = msg.find("\"" + key + "\"");
        if (pos != std::string::npos) {
            size_t colon = msg.find(':', pos);
            if (colon != std::string::npos) {
                out = std::stoi(msg.substr(colon + 1));
                return true;
            }
        }
        return false;
    };

    // Helper to find float values
    auto getFloat = [&](const std::string& key, float& out) {
        size_t pos = msg.find("\"" + key + "\"");
        if (pos != std::string::npos) {
            size_t colon = msg.find(':', pos);
            if (colon != std::string::npos) {
                out = std::stof(msg.substr(colon + 1));
                return true;
            }
        }
        return false;
    };

    // Helper to find boolean values
    auto getBool = [&](const std::string& key, bool& out) {
        size_t pos = msg.find("\"" + key + "\"");
        if (pos != std::string::npos) {
            size_t colon = msg.find(':', pos);
            if (colon != std::string::npos) {
                std::string sub = msg.substr(colon + 1, 10);
                if (sub.find("true") != std::string::npos) { out = true; return true; }
                if (sub.find("false") != std::string::npos) { out = false; return true; }
            }
        }
        return false;
    };

    int r = 0, g = 0, b = 0, effect = 0, playerLeds = 0;
    float effSpeed = 1.0f;
    if (getInt("ledR", r)) { cfg.ledR = (uint8_t)r; changed = true; }
    if (getInt("ledG", g)) { cfg.ledG = (uint8_t)g; changed = true; }
    if (getInt("ledB", b)) { cfg.ledB = (uint8_t)b; changed = true; }
    if (getInt("rgbEffect", effect)) { cfg.rgbEffect = (RgbEffect)effect; changed = true; }
    if (getInt("playerLeds", playerLeds)) { cfg.playerLeds = (uint8_t)playerLeds; changed = true; }
    if (getFloat("effectSpeed", effSpeed)) { cfg.effectSpeed = effSpeed; changed = true; }

    float mouseSens = 0.0f, scrollSens = 0.0f;
    bool mouseEn = false, tapClick = false, syncMic = false;
    if (getFloat("mouseSens", mouseSens)) { cfg.mouseSensitivity = mouseSens; changed = true; }
    if (getFloat("scrollSens", scrollSens)) { cfg.scrollSensitivity = scrollSens; changed = true; }
    if (getBool("mouseEnabled", mouseEn)) { cfg.mouseEnabled = mouseEn; changed = true; }
    if (getBool("tapToClick", tapClick)) { cfg.tapToClick = tapClick; changed = true; }
    if (getBool("syncMicMute", syncMic)) { cfg.syncMicMute = syncMic; changed = true; }

    // Adaptive Triggers
    int lMode = 0, lStart = 0, lForce = 0, lFreq = 0;
    if (getInt("ltMode", lMode)) { cfg.leftTrigger.mode = (TriggerMode)lMode; changed = true; }
    if (getInt("ltStart", lStart)) { cfg.leftTrigger.startPos = (uint8_t)lStart; changed = true; }
    if (getInt("ltForce", lForce)) { cfg.leftTrigger.force = (uint8_t)lForce; changed = true; }
    if (getInt("ltFreq", lFreq)) { cfg.leftTrigger.frequency = (uint8_t)lFreq; changed = true; }

    int rMode = 0, rStart = 0, rForce = 0, rFreq = 0;
    if (getInt("rtMode", rMode)) { cfg.rightTrigger.mode = (TriggerMode)rMode; changed = true; }
    if (getInt("rtStart", rStart)) { cfg.rightTrigger.startPos = (uint8_t)rStart; changed = true; }
    if (getInt("rtForce", rForce)) { cfg.rightTrigger.force = (uint8_t)rForce; changed = true; }
    if (getInt("rtFreq", rFreq)) { cfg.rightTrigger.frequency = (uint8_t)rFreq; changed = true; }

    if (changed) {
        m_lastConfig = cfg;
        m_configCb(cfg);
    }
}

void IpcServer::BroadcastState(const ControllerState& s, const ControllerConfig& c) {
    if (!m_running) return;

    std::ostringstream ss;
    ss << "{"
       << "\"connected\":" << (s.connected ? "true" : "false") << ","
       << "\"model\":" << (int)s.model << ","
       << "\"conn\":" << (int)s.connection << ","
       << "\"battery\":" << (int)s.batteryPercent << ","
       << "\"charging\":" << (s.isCharging ? "true" : "false") << ","
       << "\"hz\":" << s.pollingRateHz << ","
       << "\"latency\":" << s.latencyMs << ","
       << "\"reports\":" << s.totalReports << ","
       << "\"lx\":" << s.lx << ","
       << "\"ly\":" << s.ly << ","
       << "\"rx\":" << s.rx << ","
       << "\"ry\":" << s.ry << ","
       << "\"l2\":" << s.l2 << ","
       << "\"r2\":" << s.r2 << ","
       << "\"dpad\":" << (int)s.dpad << ","
       << "\"sq\":" << (s.square ? "true" : "false") << ","
       << "\"cr\":" << (s.cross ? "true" : "false") << ","
       << "\"ci\":" << (s.circle ? "true" : "false") << ","
       << "\"tr\":" << (s.triangle ? "true" : "false") << ","
       << "\"l1\":" << (s.l1 ? "true" : "false") << ","
       << "\"r1\":" << (s.r1 ? "true" : "false") << ","
       << "\"l2b\":" << (s.l2Btn ? "true" : "false") << ","
       << "\"r2b\":" << (s.r2Btn ? "true" : "false") << ","
       << "\"share\":" << (s.share ? "true" : "false") << ","
       << "\"options\":" << (s.options ? "true" : "false") << ","
       << "\"l3\":" << (s.l3 ? "true" : "false") << ","
       << "\"r3\":" << (s.r3 ? "true" : "false") << ","
       << "\"ps\":" << (s.ps ? "true" : "false") << ","
       << "\"padBtn\":" << (s.touchpadBtn ? "true" : "false") << ","
       << "\"micBtn\":" << (s.micBtn ? "true" : "false") << ","
       << "\"micMuted\":" << (s.micMuted ? "true" : "false") << ","
       << "\"t1\":{\"on\":" << (s.touch1.active ? "true" : "false") << ",\"x\":" << s.touch1.x << ",\"y\":" << s.touch1.y << "},"
       << "\"t2\":{\"on\":" << (s.touch2.active ? "true" : "false") << ",\"x\":" << s.touch2.x << ",\"y\":" << s.touch2.y << "},"
       << "\"cfg\":{"
           << "\"r\":" << (int)c.ledR << ",\"g\":" << (int)c.ledG << ",\"b\":" << (int)c.ledB << ","
           << "\"effect\":" << (int)c.rgbEffect << ","
           << "\"speed\":" << c.effectSpeed << ","
           << "\"mouse\":" << (c.mouseEnabled ? "true" : "false") << ","
           << "\"syncMic\":" << (c.syncMicMute ? "true" : "false") << ","
           << "\"ltMode\":" << (int)c.leftTrigger.mode << ","
           << "\"rtMode\":" << (int)c.rightTrigger.mode
       << "}"
       << "}\n";

    std::string json = ss.str();

    std::lock_guard<std::mutex> lock(m_pipeMutex);
    for (auto it = m_clientPipes.begin(); it != m_clientPipes.end(); ) {
        DWORD written = 0;
        BOOL ok = WriteFile(*it, json.data(), (DWORD)json.size(), &written, NULL);
        if (!ok) {
            DisconnectNamedPipe(*it);
            CloseHandle(*it);
            it = m_clientPipes.erase(it);
        } else {
            ++it;
        }
    }
}

} // namespace DualDriver
