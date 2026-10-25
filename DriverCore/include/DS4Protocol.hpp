#pragma once

#include "Common.hpp"
#include <cstring>

namespace DualDriver {

class DS4Protocol {
public:
    static uint32_t ComputeCRC32(const uint8_t* data, size_t len) {
        uint32_t crc = 0xFFFFFFFF;
        for (size_t i = 0; i < len; i++) {
            crc ^= data[i];
            for (int j = 0; j < 8; j++) {
                crc = (crc >> 1) ^ (0xEDB88320 & (-(int)(crc & 1)));
            }
        }
        return ~crc;
    }

    static bool ParseInputReport(const uint8_t* data, size_t len, ControllerState& state, ConnectionType conn) {
        if (len < 64) return false;

        const uint8_t* p = data;
        if (conn == ConnectionType::Bluetooth && (data[0] == 0x11 || data[0] == 0x31)) {
            p = data + 2; // DS4 BT offset
        }

        // Sticks
        state.rawLX = p[1];
        state.rawLY = p[2];
        state.rawRX = p[3];
        state.rawRY = p[4];
        state.lx = (p[1] - 128.0f) / 128.0f;
        state.ly = -(p[2] - 128.0f) / 128.0f;
        state.rx = (p[3] - 128.0f) / 128.0f;
        state.ry = -(p[4] - 128.0f) / 128.0f;

        // D-Pad
        state.dpad = p[5] & 0x0F;
        state.dpadUp    = (state.dpad == 0 || state.dpad == 1 || state.dpad == 7);
        state.dpadRight = (state.dpad == 1 || state.dpad == 2 || state.dpad == 3);
        state.dpadDown  = (state.dpad == 3 || state.dpad == 4 || state.dpad == 5);
        state.dpadLeft  = (state.dpad == 5 || state.dpad == 6 || state.dpad == 7);

        // Face Buttons
        state.square   = (p[5] & 0x10) != 0;
        state.cross    = (p[5] & 0x20) != 0;
        state.circle   = (p[5] & 0x40) != 0;
        state.triangle = (p[5] & 0x80) != 0;

        // Shoulder & Center Buttons
        state.l1      = (p[6] & 0x01) != 0;
        state.r1      = (p[6] & 0x02) != 0;
        state.l2Btn   = (p[6] & 0x04) != 0;
        state.r2Btn   = (p[6] & 0x08) != 0;
        state.share   = (p[6] & 0x10) != 0;
        state.options = (p[6] & 0x20) != 0;
        state.l3      = (p[6] & 0x40) != 0;
        state.r3      = (p[6] & 0x80) != 0;

        // PS & Touchpad click
        state.ps          = (p[7] & 0x01) != 0;
        state.touchpadBtn = (p[7] & 0x02) != 0;
        state.micBtn      = false;

        // Triggers
        state.rawL2 = p[8];
        state.rawR2 = p[9];
        state.l2 = p[8] / 255.0f;
        state.r2 = p[9] / 255.0f;

        // Gyro & Accel
        state.gyroX  = (int16_t)(p[13] | (p[14] << 8));
        state.gyroY  = (int16_t)(p[15] | (p[16] << 8));
        state.gyroZ  = (int16_t)(p[17] | (p[18] << 8));
        state.accelX = (int16_t)(p[19] | (p[20] << 8));
        state.accelY = (int16_t)(p[21] | (p[22] << 8));
        state.accelZ = (int16_t)(p[23] | (p[24] << 8));

        // Battery
        uint8_t batRaw = p[30] & 0x0F;
        state.batteryPercent = (batRaw >= 10) ? 100 : batRaw * 10;
        state.isCharging = (p[30] & 0x10) != 0;

        // Touchpad Touch 1
        state.touch1.active = (p[35] & 0x80) == 0;
        state.touch1.id     = p[35] & 0x7F;
        state.touch1.x      = ((p[37] & 0x0F) << 8) | p[36];
        state.touch1.y      = (p[38] << 4) | ((p[37] & 0xF0) >> 4);

        // Touchpad Touch 2
        state.touch2.active = (p[39] & 0x80) == 0;
        state.touch2.id     = p[39] & 0x7F;
        state.touch2.x      = ((p[41] & 0x0F) << 8) | p[40];
        state.touch2.y      = (p[42] << 4) | ((p[41] & 0xF0) >> 4);

        return true;
    }

    static void BuildOutputReportUSB(uint8_t* out, size_t outLen, const ControllerConfig& config, uint8_t leftRumble, uint8_t rightRumble) {
        if (outLen < 32) return;
        std::memset(out, 0, outLen);

        out[0] = 0x05; // DS4 USB Output Report ID
        out[1] = 0xFF; // Flags (enable rumble, enable lightbar)
        out[4] = rightRumble;
        out[5] = leftRumble;
        out[6] = config.ledR;
        out[7] = config.ledG;
        out[8] = config.ledB;
    }

    static void BuildOutputReportBT(uint8_t* out, size_t outLen, const ControllerConfig& config, uint8_t leftRumble, uint8_t rightRumble) {
        if (outLen < 78) return;
        std::memset(out, 0, outLen);

        out[0] = 0x11;
        out[1] = 0x80;
        out[3] = 0xFF;
        out[6] = rightRumble;
        out[7] = leftRumble;
        out[8] = config.ledR;
        out[9] = config.ledG;
        out[10] = config.ledB;

        uint8_t crcBuffer[75];
        crcBuffer[0] = 0xA2;
        std::memcpy(&crcBuffer[1], out, 74);
        uint32_t crc = ComputeCRC32(crcBuffer, 75);

        out[74] = (uint8_t)(crc & 0xFF);
        out[75] = (uint8_t)((crc >> 8) & 0xFF);
        out[76] = (uint8_t)((crc >> 16) & 0xFF);
        out[77] = (uint8_t)((crc >> 24) & 0xFF);
    }
};

} // namespace DualDriver
