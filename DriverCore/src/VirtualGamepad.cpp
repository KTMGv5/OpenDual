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

    m_hViGEmDll = LoadLibraryA("ViGEmClient.dll");
    if (!m_hViGEmDll) {
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
    fn_vigem_target_ds4_alloc = (vigem_target_ds4_alloc_fn)GetProcAddress(m_hViGEmDll, "vigem_target_ds4_alloc");
    fn_vigem_target_free = (vigem_target_free_fn)GetProcAddress(m_hViGEmDll, "vigem_target_free");
    fn_vigem_target_add = (vigem_target_add_fn)GetProcAddress(m_hViGEmDll, "vigem_target_add");
    fn_vigem_target_remove = (vigem_target_remove_fn)GetProcAddress(m_hViGEmDll, "vigem_target_remove");
    fn_vigem_target_x360_send = (vigem_target_x360_send_fn)GetProcAddress(m_hViGEmDll, "vigem_target_x360_update");
    if (!fn_vigem_target_x360_send) {
        fn_vigem_target_x360_send = (vigem_target_x360_send_fn)GetProcAddress(m_hViGEmDll, "vigem_target_x360_send");
    }
    fn_vigem_target_ds4_send = (vigem_target_ds4_send_fn)GetProcAddress(m_hViGEmDll, "vigem_target_ds4_update");
    fn_vigem_target_x360_register_notification = (vigem_target_x360_register_notification_fn)GetProcAddress(m_hViGEmDll, "vigem_target_x360_register_notification");
    fn_vigem_target_x360_unregister_notification = (vigem_target_x360_unregister_notification_fn)GetProcAddress(m_hViGEmDll, "vigem_target_x360_unregister_notification");

    if (!fn_vigem_alloc || !fn_vigem_connect || !fn_vigem_target_x360_alloc || !fn_vigem_target_add || !fn_vigem_target_x360_send) {
        FreeLibrary(m_hViGEmDll);
        m_hViGEmDll = NULL;
        return false;
    }

    return true;
}

void VirtualGamepad::SetMode(int padMode) {
    if (padMode == m_currentMode && (m_connected || padMode == 2)) return;
    Shutdown();
    m_currentMode = padMode;
    if (padMode != 2) {
        Initialize(padMode);
    }
}

bool VirtualGamepad::Initialize(int padMode) {
    m_currentMode = padMode;
    if (padMode == 2) return true;
    if (m_connected) return true;

    if (!LoadViGEmLibrary()) {
        m_available = false;
        return false;
    }

    m_available = true;
    m_client = fn_vigem_alloc();
    if (!m_client) return false;

    int res = fn_vigem_connect(m_client);
    if (res != 0) {
        fn_vigem_free(m_client);
        m_client = nullptr;
        return false;
    }

    if (m_currentMode == 1 && fn_vigem_target_ds4_alloc && fn_vigem_target_ds4_send) {
        m_target = fn_vigem_target_ds4_alloc();
    } else {
        m_currentMode = 0;
        m_target = fn_vigem_target_x360_alloc();
    }

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

    if (m_currentMode == 0 && fn_vigem_target_x360_register_notification) {
        fn_vigem_target_x360_register_notification(m_client, m_target, X360NotificationCallback, this);
    }

    m_connected = true;
    return true;
}

void VirtualGamepad::Shutdown() {
    if (m_connected) {
        if (m_client && m_target) {
            if (m_currentMode == 0 && fn_vigem_target_x360_unregister_notification) {
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
}

void VirtualGamepad::OnRumble(uint8_t largeMotor, uint8_t smallMotor) {
    if (m_rumbleCb) {
        m_rumbleCb(largeMotor, smallMotor);
    }
}

void VirtualGamepad::Update(const ControllerState& state) {
    if (!m_connected || !m_client || !m_target || m_currentMode == 2) {
        return;
    }

    float combinedRx = std::clamp(state.rx + state.gyroStickX, -1.0f, 1.0f);
    float combinedRy = std::clamp(state.ry + state.gyroStickY, -1.0f, 1.0f);

    if (m_currentMode == 1 && fn_vigem_target_ds4_send) {
        DS4_REPORT ds4 = { 0 };
        ds4.bThumbLX = (BYTE)std::clamp((state.lx + 1.0f) * 127.5f, 0.0f, 255.0f);
        ds4.bThumbLY = (BYTE)std::clamp((1.0f - state.ly) * 127.5f, 0.0f, 255.0f);
        ds4.bThumbRX = (BYTE)std::clamp((combinedRx + 1.0f) * 127.5f, 0.0f, 255.0f);
        ds4.bThumbRY = (BYTE)std::clamp((1.0f - combinedRy) * 127.5f, 0.0f, 255.0f);

        USHORT btns = state.dpad & 0x0F;
        if (state.square)   btns |= (1 << 4);
        if (state.cross)    btns |= (1 << 5);
        if (state.circle)   btns |= (1 << 6);
        if (state.triangle) btns |= (1 << 7);
        if (state.l1)       btns |= (1 << 8);
        if (state.r1)       btns |= (1 << 9);
        if (state.l2Btn)    btns |= (1 << 10);
        if (state.r2Btn)    btns |= (1 << 11);
        if (state.share)    btns |= (1 << 12);
        if (state.options)  btns |= (1 << 13);
        if (state.l3)       btns |= (1 << 14);
        if (state.r3)       btns |= (1 << 15);
        ds4.wButtons = btns;

        if (state.ps)          ds4.bSpecial |= 0x01;
        if (state.touchpadBtn) ds4.bSpecial |= 0x02;
        ds4.bTriggerL = state.rawL2;
        ds4.bTriggerR = state.rawR2;

        fn_vigem_target_ds4_send(m_client, m_target, ds4);
        return;
    }

    if (!fn_vigem_target_x360_send) return;

    XUSB_REPORT report = { 0 };
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

    report.bLeftTrigger = state.rawL2;
    report.bRightTrigger = state.rawR2;

    report.sThumbLX = (SHORT)std::clamp(state.lx * 32767.0f, -32768.0f, 32767.0f);
    report.sThumbLY = (SHORT)std::clamp(state.ly * 32767.0f, -32768.0f, 32767.0f);
    report.sThumbRX = (SHORT)std::clamp(combinedRx * 32767.0f, -32768.0f, 32767.0f);
    report.sThumbRY = (SHORT)std::clamp(combinedRy * 32767.0f, -32768.0f, 32767.0f);

    fn_vigem_target_x360_send(m_client, m_target, report);
}

} // namespace DualDriver
