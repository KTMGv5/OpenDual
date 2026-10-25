#pragma once

#include "Common.hpp"
#include <cstring>
#include <cmath>

namespace DualDriver {

class DualSenseProtocol {
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
        if (conn == ConnectionType::Bluetooth && data[0] == 0x31) {
            // Bluetooth report 0x31 has 1 byte offset
            p = data + 1;
        }

        // Sticks
        state.rawLX = p[1];
        state.rawLY = p[2];
        state.rawRX = p[3];
        state.rawRY = p[4];
        state.lx = (p[1] - 128.0f) / 128.0f;
        state.ly = -(p[2] - 128.0f) / 128.0f; // Invert Y so up is positive
        state.rx = (p[3] - 128.0f) / 128.0f;
        state.ry = -(p[4] - 128.0f) / 128.0f;

        // Triggers
        state.rawL2 = p[5];
        state.rawR2 = p[6];
        state.l2 = p[5] / 255.0f;
        state.r2 = p[6] / 255.0f;

        // D-Pad (0=N, 1=NE, 2=E, 3=SE, 4=S, 5=SW, 6=W, 7=NW, 8=Neutral)
        state.dpad = p[8] & 0x0F;
        state.dpadUp    = (state.dpad == 0 || state.dpad == 1 || state.dpad == 7);
        state.dpadRight = (state.dpad == 1 || state.dpad == 2 || state.dpad == 3);
        state.dpadDown  = (state.dpad == 3 || state.dpad == 4 || state.dpad == 5);
        state.dpadLeft  = (state.dpad == 5 || state.dpad == 6 || state.dpad == 7);

        // Face Buttons
        state.square   = (p[8] & 0x10) != 0;
        state.cross    = (p[8] & 0x20) != 0;
        state.circle   = (p[8] & 0x40) != 0;
        state.triangle = (p[8] & 0x80) != 0;

        // Shoulder & Center Buttons
        state.l1      = (p[9] & 0x01) != 0;
        state.r1      = (p[9] & 0x02) != 0;
        state.l2Btn   = (p[9] & 0x04) != 0;
        state.r2Btn   = (p[9] & 0x08) != 0;
        state.share   = (p[9] & 0x10) != 0; // Create button
        state.options = (p[9] & 0x20) != 0;
        state.l3      = (p[9] & 0x40) != 0;
        state.r3      = (p[9] & 0x80) != 0;

        // PS, Touchpad, Mic buttons
        state.ps          = (p[10] & 0x01) != 0;
        state.touchpadBtn = (p[10] & 0x02) != 0;
        state.micBtn      = (p[10] & 0x04) != 0;

        // DualSense Edge Function Buttons & Back Paddles
        state.fnLeft      = (p[10] & 0x10) != 0;
        state.fnRight     = (p[10] & 0x20) != 0;
        state.paddleLeft  = (p[11] & 0x01) != 0;
        state.paddleRight = (p[11] & 0x02) != 0;

        // Gyro & Accel
        state.gyroX  = (int16_t)(p[16] | (p[17] << 8));
        state.gyroY  = (int16_t)(p[18] | (p[19] << 8));
        state.gyroZ  = (int16_t)(p[20] | (p[21] << 8));
        state.accelX = (int16_t)(p[22] | (p[23] << 8));
        state.accelY = (int16_t)(p[24] | (p[25] << 8));
        state.accelZ = (int16_t)(p[26] | (p[27] << 8));

        // Touchpad Touch 1
        state.touch1.active = (p[33] & 0x80) == 0; // 0 = down, 1 = up
        state.touch1.id     = p[33] & 0x7F;
        state.touch1.x      = ((p[35] & 0x0F) << 8) | p[34];
        state.touch1.y      = (p[36] << 4) | ((p[35] & 0xF0) >> 4);

        // Touchpad Touch 2
        state.touch2.active = (p[37] & 0x80) == 0;
        state.touch2.id     = p[37] & 0x7F;
        state.touch2.x      = ((p[39] & 0x0F) << 8) | p[38];
        state.touch2.y      = (p[40] << 4) | ((p[39] & 0xF0) >> 4);

        // Battery
        uint8_t batRaw = p[53] & 0x0F;
        state.batteryPercent = (batRaw >= 10) ? 100 : batRaw * 10;
        state.isCharging = (p[53] & 0x10) != 0;

        return true;
    }

    static void BuildOutputReportUSB(uint8_t* out, size_t outLen, const ControllerConfig& config, uint8_t leftRumble, uint8_t rightRumble, bool micMuted) {
        if (outLen < 48) return;
        std::memset(out, 0, outLen);

        out[0] = 0x02; // DualSense USB Output Report ID
        
        // valid_flag0:
        // 0x01: Compatible Vibration / Rumble
        // 0x02: Haptics Select
        // 0x04: Right Trigger Motor Enable
        // 0x08: Left Trigger Motor Enable
        // NOTE: DO NOT set 0x10 (headphone volume enable) or 0x20/0x40/0x80! Setting them overrides
        // and silences the headphone DAC. Leaving them unset lets Windows USB Audio control volume natively.
        out[1] = 0x0F; 

        // valid_flag1:
        // 0x01: Mic Mute LED
        // 0x04: Lightbar Setup
        // 0x10: Player LEDs
        // 0x20: Lightbar RGB
        out[2] = 0x35; 

        // Rumble
        out[3] = rightRumble;
        out[4] = leftRumble;

        // Headphone Volume (0..127): Set to max (0x7F) and speaker to max (0xFF) as safeguard
        out[5] = 0x7F;
        out[6] = 0xFF;

        // Mic Mute LED (0=off, 1=solid amber, 2=pulse)
        out[9] = micMuted ? 0x01 : 0x00;

        // Right Trigger
        out[11] = (uint8_t)config.rightTrigger.mode;
        out[12] = config.rightTrigger.startPos;
        out[13] = config.rightTrigger.force;
        out[14] = config.rightTrigger.frequency;
        out[15] = config.rightTrigger.endPos;

        // Left Trigger
        out[22] = (uint8_t)config.leftTrigger.mode;
        out[23] = config.leftTrigger.startPos;
        out[24] = config.leftTrigger.force;
        out[25] = config.leftTrigger.frequency;
        out[26] = config.leftTrigger.endPos;

        // Player LEDs
        out[44] = config.playerLeds;

        // Lightbar RGB
        out[45] = config.ledR;
        out[46] = config.ledG;
        out[47] = config.ledB;
    }

    static void BuildOutputReportBT(uint8_t* out, size_t outLen, const ControllerConfig& config, uint8_t leftRumble, uint8_t rightRumble, bool micMuted) {
        if (outLen < 78) return;
        std::memset(out, 0, outLen);

        out[0] = 0x31; // DualSense BT Output Report ID
        out[1] = 0x02; // Tag

        out[2] = 0x0F; // valid_flag0 (rumble + triggers, no audio mute)
        out[3] = 0x35; // valid_flag1 (LEDs + lightbar)

        out[4] = rightRumble;
        out[5] = leftRumble;

        out[6] = 0x7F; // Headphone volume safeguard
        out[7] = 0xFF; // Speaker volume safeguard

        out[10] = micMuted ? 0x01 : 0x00;

        out[12] = (uint8_t)config.rightTrigger.mode;
        out[13] = config.rightTrigger.startPos;
        out[14] = config.rightTrigger.force;
        out[15] = config.rightTrigger.frequency;
        out[16] = config.rightTrigger.endPos;

        out[23] = (uint8_t)config.leftTrigger.mode;
        out[24] = config.leftTrigger.startPos;
        out[25] = config.leftTrigger.force;
        out[26] = config.leftTrigger.frequency;
        out[27] = config.leftTrigger.endPos;

        out[45] = config.playerLeds;

        out[46] = config.ledR;
        out[47] = config.ledG;
        out[48] = config.ledB;

        // Calculate Bluetooth CRC32 over header 0xA2 + report bytes 0..73
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
