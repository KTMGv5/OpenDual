#include "VirtualGamepad.hpp"
#include <iostream>
#include <algorithm>

namespace DualDriver {

static void __stdcall X360NotificationCallback(
    PVIGEM_CLIENT Client,
    PVIGEM_TARGET Target,
    UCHAR LargeMotor,
    UCHAR SmallMotor,
    UCHAR LedNumber,
    LPVOID UserData)
{
    if (UserData) {
        VirtualGamepad* pad = reinterpret_cast<VirtualGamepad*>(UserData);
        pad->OnRumble(LargeMotor, SmallMotor);
    }
}

VirtualGamepad::VirtualGamepad() {
}

VirtualGamepad::~VirtualGamepad() {
    Shutdown();
}

bool VirtualGamepad::LoadViGEmLibrary() {
    if (m_hViGEmDll) return true;

    // Try loading ViGEmClient.dll from local dir or system PATH
    m_hViGEmDll = LoadLibraryA("ViGEmClient.dll");
    if (!m_hViGEmDll) {
        // Try standard install locations
        m_hViGEmDll = LoadLibraryA("C:\\Program Files\\Nefarius Software Solutions\\ViGEm Bus Driver\\ViGEmClient.dll");
    }

    if (!m_hViGEmDll) {
        return false;
    }

    fn_vigem_alloc = (vigem_alloc_fn)GetProcAddress(m_hViGEmDll, "vigem_alloc");
    fn_vigem_free = (vigem_free_fn)GetProcAddress(m_hViGEmDll, "vigem_free");
    fn_vigem_connect = (vigem_connect_fn)GetProcAddress(m_hViGEmDll, "vigem_connect");
    fn_vigem_disconnect = (vigem_disconnect_fn)GetProcAddress(m_hViGEmDll, "vigem_disconnect");
    fn_vigem_target_x360_alloc = (vigem_target_x360_alloc_fn)GetProcAddress(m_hViGEmDll, "vigem_target_x360_alloc");
    fn_vigem_target_free = (vigem_target_free_fn)GetProcAddress(m_hViGEmDll, "vigem_target_free");
    fn_vigem_target_add = (vigem_target_add_fn)GetProcAddress(m_hViGEmDll, "vigem_target_add");
    fn_vigem_target_remove = (vigem_target_remove_fn)GetProcAddress(m_hViGEmDll, "vigem_target_remove");
    fn_vigem_target_x360_send = (vigem_target_x360_send_fn)GetProcAddress(m_hViGEmDll, "vigem_target_x360_send");
    fn_vigem_target_x360_register_notification = (vigem_target_x360_register_notification_fn)GetProcAddress(m_hViGEmDll, "vigem_target_x360_register_notification");
    fn_vigem_target_x360_unregister_notification = (vigem_target_x360_unregister_notification_fn)GetProcAddress(m_hViGEmDll, "vigem_target_x360_unregister_notification");

    if (!fn_vigem_alloc || !fn_vigem_connect || !fn_vigem_target_x360_alloc || !fn_vigem_target_add || !fn_vigem_target_x360_send) {
        FreeLibrary(m_hViGEmDll);
        m_hViGEmDll = NULL;
        return false;
    }

    return true;
}

bool VirtualGamepad::Initialize() {
    if (m_connected) return true;

    if (!LoadViGEmLibrary()) {
        m_available = false;
        return false;
    }

    m_available = true;
    m_client = fn_vigem_alloc();
    if (!m_client) return false;

    int res = fn_vigem_connect(m_client);
    if (res != 0) { // VIGEM_ERROR_NONE = 0
        fn_vigem_free(m_client);
        m_client = nullptr;
        return false;
    }

    m_target = fn_vigem_target_x360_alloc();
    if (!m_target) {
        fn_vigem_disconnect(m_client);
        fn_vigem_free(m_client);
        m_client = nullptr;
        return false;
    }

    res = fn_vigem_target_add(m_client, m_target);
    if (res != 0) {
        fn_vigem_target_free(m_target);
        m_target = nullptr;
        fn_vigem_disconnect(m_client);
        fn_vigem_free(m_client);
        m_client = nullptr;
        return false;
    }

    if (fn_vigem_target_x360_register_notification) {
        fn_vigem_target_x360_register_notification(m_client, m_target, X360NotificationCallback, this);
    }

    m_connected = true;
    return true;
}

void VirtualGamepad::Shutdown() {
    if (m_connected) {
        if (m_client && m_target) {
            if (fn_vigem_target_x360_unregister_notification) {
                fn_vigem_target_x360_unregister_notification(m_target);
            }
            if (fn_vigem_target_remove) {
                fn_vigem_target_remove(m_client, m_target);
            }
            if (fn_vigem_target_free) {
                fn_vigem_target_free(m_target);
            }
            m_target = nullptr;
        }

        if (m_client) {
            if (fn_vigem_disconnect) fn_vigem_disconnect(m_client);
            if (fn_vigem_free) fn_vigem_free(m_client);
            m_client = nullptr;
        }
        m_connected = false;
    }

    if (m_hViGEmDll) {
        FreeLibrary(m_hViGEmDll);
        m_hViGEmDll = NULL;
    }
}

void VirtualGamepad::OnRumble(uint8_t largeMotor, uint8_t smallMotor) {
    if (m_rumbleCb) {
        m_rumbleCb(largeMotor, smallMotor);
    }
}

void VirtualGamepad::Update(const ControllerState& state) {
    if (!m_connected || !m_client || !m_target || !fn_vigem_target_x360_send) {
        return;
    }

    XUSB_REPORT report = { 0 };

    // Buttons bitmask for Xbox 360:
    // 0x0001: DPad Up
    // 0x0002: DPad Down
    // 0x0004: DPad Left
    // 0x0008: DPad Right
    // 0x0010: Start
    // 0x0020: Back
    // 0x0040: Left Thumb
    // 0x0080: Right Thumb
    // 0x0100: Left Shoulder
    // 0x0200: Right Shoulder
    // 0x0400: Guide
    // 0x1000: A (Cross)
    // 0x2000: B (Circle)
    // 0x4000: X (Square)
    // 0x8000: Y (Triangle)

    if (state.dpadUp)    report.wButtons |= 0x0001;
    if (state.dpadDown)  report.wButtons |= 0x0002;
    if (state.dpadLeft)  report.wButtons |= 0x0004;
    if (state.dpadRight) report.wButtons |= 0x0008;

    if (state.options)   report.wButtons |= 0x0010; // Start
    if (state.share)     report.wButtons |= 0x0020; // Back
    if (state.l3)        report.wButtons |= 0x0040; // L3
    if (state.r3)        report.wButtons |= 0x0080; // R3
    if (state.l1)        report.wButtons |= 0x0100; // LB
    if (state.r1)        report.wButtons |= 0x0200; // RB
    if (state.ps)        report.wButtons |= 0x0400; // Guide

    if (state.cross)     report.wButtons |= 0x1000; // A
    if (state.circle)    report.wButtons |= 0x2000; // B
    if (state.square)    report.wButtons |= 0x4000; // X
    if (state.triangle)  report.wButtons |= 0x8000; // Y

    // Triggers (0..255)
    report.bLeftTrigger = state.rawL2;
    report.bRightTrigger = state.rawR2;

    // Analog Sticks (-32768..32767)
    // Invert LY/RY back to standard Xbox format (up = positive, right = positive)
    report.sThumbLX = (SHORT)std::clamp(state.lx * 32767.0f, -32768.0f, 32767.0f);
    report.sThumbLY = (SHORT)std::clamp(state.ly * 32767.0f, -32768.0f, 32767.0f);
    report.sThumbRX = (SHORT)std::clamp(state.rx * 32767.0f, -32768.0f, 32767.0f);
    report.sThumbRY = (SHORT)std::clamp(state.ry * 32767.0f, -32768.0f, 32767.0f);

    fn_vigem_target_x360_send(m_client, m_target, report);
}

} // namespace DualDriver
