#pragma once

#include "Common.hpp"
#include <windows.h>
#include <functional>

namespace DualDriver {

// ViGEm API types for dynamic loading
typedef void* PVIGEM_CLIENT;
typedef void* PVIGEM_TARGET;

typedef struct _XUSB_REPORT {
    USHORT wButtons;
    BYTE bLeftTrigger;
    BYTE bRightTrigger;
    SHORT sThumbLX;
    SHORT sThumbLY;
    SHORT sThumbRX;
    SHORT sThumbRY;
} XUSB_REPORT, *PXUSB_REPORT;

typedef void(__stdcall* PVIGEM_X360_NOTIFICATION)(
    PVIGEM_CLIENT Client,
    PVIGEM_TARGET Target,
    UCHAR LargeMotor,
    UCHAR SmallMotor,
    UCHAR LedNumber,
    LPVOID UserData
);

class VirtualGamepad {
public:
    using RumbleCallback = std::function<void(uint8_t largeMotor, uint8_t smallMotor)>;

    VirtualGamepad();
    ~VirtualGamepad();

    bool Initialize();
    void Shutdown();

    bool IsAvailable() const { return m_available; }
    bool IsConnected() const { return m_connected; }

    void Update(const ControllerState& state);
    void SetRumbleCallback(RumbleCallback cb) { m_rumbleCb = cb; }

    void OnRumble(uint8_t largeMotor, uint8_t smallMotor);

private:
    bool LoadViGEmLibrary();

    HMODULE m_hViGEmDll = NULL;
    PVIGEM_CLIENT m_client = nullptr;
    PVIGEM_TARGET m_target = nullptr;

    bool m_available = false;
    bool m_connected = false;
    RumbleCallback m_rumbleCb;

    // Dynamic ViGEm function pointers
    typedef PVIGEM_CLIENT(__stdcall* vigem_alloc_fn)();
    typedef void(__stdcall* vigem_free_fn)(PVIGEM_CLIENT vigem);
    typedef INT(__stdcall* vigem_connect_fn)(PVIGEM_CLIENT vigem);
    typedef void(__stdcall* vigem_disconnect_fn)(PVIGEM_CLIENT vigem);
    typedef PVIGEM_TARGET(__stdcall* vigem_target_x360_alloc_fn)();
    typedef void(__stdcall* vigem_target_free_fn)(PVIGEM_TARGET target);
    typedef INT(__stdcall* vigem_target_add_fn)(PVIGEM_CLIENT vigem, PVIGEM_TARGET target);
    typedef INT(__stdcall* vigem_target_remove_fn)(PVIGEM_CLIENT vigem, PVIGEM_TARGET target);
    typedef INT(__stdcall* vigem_target_x360_send_fn)(PVIGEM_CLIENT vigem, PVIGEM_TARGET target, XUSB_REPORT report);
    typedef INT(__stdcall* vigem_target_x360_register_notification_fn)(PVIGEM_CLIENT vigem, PVIGEM_TARGET target, PVIGEM_X360_NOTIFICATION notification, LPVOID userData);
    typedef void(__stdcall* vigem_target_x360_unregister_notification_fn)(PVIGEM_TARGET target);

    vigem_alloc_fn fn_vigem_alloc = nullptr;
    vigem_free_fn fn_vigem_free = nullptr;
    vigem_connect_fn fn_vigem_connect = nullptr;
    vigem_disconnect_fn fn_vigem_disconnect = nullptr;
    vigem_target_x360_alloc_fn fn_vigem_target_x360_alloc = nullptr;
    vigem_target_free_fn fn_vigem_target_free = nullptr;
    vigem_target_add_fn fn_vigem_target_add = nullptr;
    vigem_target_remove_fn fn_vigem_target_remove = nullptr;
    vigem_target_x360_send_fn fn_vigem_target_x360_send = nullptr;
    vigem_target_x360_register_notification_fn fn_vigem_target_x360_register_notification = nullptr;
    vigem_target_x360_unregister_notification_fn fn_vigem_target_x360_unregister_notification = nullptr;
};

} // namespace DualDriver
