#include "ControllerEngine.hpp"
#include <iostream>
#include <csignal>
#include <atomic>

static std::atomic<bool> g_stopRequested{ false };

void SignalHandler(int signum) {
    g_stopRequested = true;
}

int main(int argc, char* argv[]) {
    std::cout << "====================================================\n";
    std::cout << "  OpenDualDriver - High-Performance Controller Service\n";
    std::cout << "  Native PS4 / PS5 Windows 10 & 11 Driver Engine\n";
    std::cout << "====================================================\n";

    signal(SIGINT, SignalHandler);
    signal(SIGTERM, SignalHandler);

    DualDriver::ControllerConfig initialConfig;

    // Parse command line arguments
    for (int i = 1; i < argc; i++) {
        std::string arg = argv[i];
        if ((arg == "--sens" || arg == "-s") && i + 1 < argc) {
            initialConfig.mouseSensitivity = std::stof(argv[++i]);
        } else if (arg == "--scroll" && i + 1 < argc) {
            initialConfig.scrollSensitivity = std::stof(argv[++i]);
        } else if (arg == "--mouse" && i + 1 < argc) {
            initialConfig.mouseEnabled = (std::stoi(argv[++i]) != 0);
        } else if (arg == "--help" || arg == "-h") {
            std::cout << "Usage: OpenDualDriver.exe [options]\n";
            std::cout << "Options:\n";
            std::cout << "  --sens <float>    Set touchpad mouse sensitivity (default: 1.2, range: 0.2 to 3.0)\n";
            std::cout << "  --scroll <float>  Set two-finger scroll sensitivity (default: 1.0)\n";
            std::cout << "  --mouse <0|1>     Enable or disable touchpad mouse cursor (default: 1)\n";
            std::cout << "  --help            Show this help message\n";
            std::cout << "\nController Shortcuts:\n";
            std::cout << "  Hold PS Button + D-Pad Up    : Increase sensitivity (+0.1x)\n";
            std::cout << "  Hold PS Button + D-Pad Down  : Decrease sensitivity (-0.1x)\n";
            std::cout << "  Hold PS Button + Touchpad Click : Toggle mouse cursor ON/OFF\n";
            return 0;
        }
    }

    DualDriver::ControllerEngine engine;
    engine.SetConfig(initialConfig);

    if (!engine.Start()) {
        std::cerr << "[Error] Failed to initialize controller engine.\n";
        return 1;
    }

    std::cout << "[DriverCore] Service running at 1000Hz (1ms).\n";
    std::cout << "[DriverCore] Touchpad sensitivity: " << initialConfig.mouseSensitivity << "x (Use PS + DPad Up/Down to adjust)\n";
    std::cout << "[DriverCore] Named pipe IPC ready: \\\\.\\pipe\\OpenDualDriver\n";
    std::cout << "[DriverCore] Press Ctrl+C to stop.\n\n";

    while (!g_stopRequested) {
        Sleep(1000);
        DualDriver::ControllerState st = engine.GetState();
        if (st.connected) {
            std::cout << "\r[Status] Connected | Polling: " << st.pollingRateHz 
                      << " Hz | Latency: " << st.latencyMs 
                      << " ms | Battery: " << (int)st.batteryPercent << "%" 
                      << (st.isCharging ? " (Charging)" : "")
                      << " | Mic: " << (st.micMuted ? "MUTED" : "ACTIVE")
                      << "    " << std::flush;
        } else {
            std::cout << "\r[Status] Waiting for PS4/PS5 controller...                        " << std::flush;
        }
    }

    std::cout << "\n[DriverCore] Stopping service...\n";
    engine.Stop();
    std::cout << "[DriverCore] Service stopped cleanly.\n";
    return 0;
}
