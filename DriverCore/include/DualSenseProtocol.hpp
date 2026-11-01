#pragma once

#include "Common.hpp"
#include <cstring>
#include <cmath>
#include <algorithm>

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

    static bool ParseInputReport(const uint8_t* data, size_t len, ControllerState& state, ConnectionType conn, const ControllerConfig& config = ControllerConfig{}) {
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

        // Hardware zero-shift calibration
        float centerLX = 128.0f + (float)config.zeroShiftLX;
        float centerLY = 128.0f + (float)config.zeroShiftLY;
        float centerRX = 128.0f + (float)config.zeroShiftRX;
        float centerRY = 128.0f + (float)config.zeroShiftRY;

        auto clampUnit = [](float v) { return v < -1.0f ? -1.0f : (v > 1.0f ? 1.0f : v); };
        state.lx = clampUnit(((float)p[1] - centerLX) / 128.0f);
        state.ly = clampUnit(-((float)p[2] - centerLY) / 128.0f); // Invert Y so up is positive
        state.rx = clampUnit(((float)p[3] - centerRX) / 128.0f);
        state.ry = clampUnit(-((float)p[4] - centerRY) / 128.0f);

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

        // Headphone Jack Detection: status[1] bit 0 (DS_STATUS1_HP_DETECT)
        state.headphonesConnected = (p[54] & 0x01) != 0;

        return true;
    }

    static inline uint8_t NormalizeZone(uint8_t pos) {
        if (pos <= 9) return pos;
        int val = pos * 9 / 255;
        if (val < 0) return 0;
        if (val > 9) return 9;
        return (uint8_t)val;
    }

    static inline uint8_t NormalizeForce(uint8_t force) {
        if (force >= 1 && force <= 8) return force;
        if (force == 0) return 1;
        int val = (force * 8 + 127) / 255;
        if (val < 1) return 1;
        if (val > 8) return 8;
        return (uint8_t)val;
    }

    static void FormatTriggerPayload(uint8_t* p, const TriggerConfig& tc) {
        std::memset(p, 0, 11);
        switch (tc.mode) {
        case TriggerMode::Off:
            p[0] = 0x00;
            break;

        case TriggerMode::Rigid: { // 0x21 Feedback (Continuous resistance wall)
            uint8_t pos = NormalizeZone(tc.startPos);
            uint8_t strength = NormalizeForce(tc.force);
            uint8_t forceVal = (uint8_t)((strength - 1) & 0x07);
            uint32_t forceZones = 0;
            uint16_t activeZones = 0;
            for (int i = pos; i < 10; i++) {
                forceZones |= ((uint32_t)forceVal << (3 * i));
                activeZones |= (uint16_t)(1 << i);
            }
            p[0] = 0x21; // Feedback Mode
            p[1] = (uint8_t)(activeZones & 0xFF);
            p[2] = (uint8_t)((activeZones >> 8) & 0xFF);
            p[3] = (uint8_t)(forceZones & 0xFF);
            p[4] = (uint8_t)((forceZones >> 8) & 0xFF);
            p[5] = (uint8_t)((forceZones >> 16) & 0xFF);
            p[6] = (uint8_t)((forceZones >> 24) & 0xFF);
            break;
        }

        case TriggerMode::Pulse: { // 0x26 Vibration (Machine Gun rapid cycling)
            uint8_t pos = NormalizeZone(tc.startPos);
            uint8_t amplitude = NormalizeForce(tc.force);
            uint8_t freq = (tc.frequency > 0) ? tc.frequency : 32; // Default ~32Hz rapid fire
            uint8_t strengthVal = (uint8_t)((amplitude - 1) & 0x07);
            uint32_t ampZones = 0;
            uint16_t activeZones = 0;
            for (int i = pos; i < 10; i++) {
                ampZones |= ((uint32_t)strengthVal << (3 * i));
                activeZones |= (uint16_t)(1 << i);
            }
            p[0] = 0x26; // Vibration Mode
            p[1] = (uint8_t)(activeZones & 0xFF);
            p[2] = (uint8_t)((activeZones >> 8) & 0xFF);
            p[3] = (uint8_t)(ampZones & 0xFF);
            p[4] = (uint8_t)((ampZones >> 8) & 0xFF);
            p[5] = (uint8_t)((ampZones >> 16) & 0xFF);
            p[6] = (uint8_t)((ampZones >> 24) & 0xFF);
            p[9] = freq;
            break;
        }

        case TriggerMode::Bow: { // 0x22 Bow (Progressive tension & release)
            uint8_t startPos = NormalizeZone(tc.startPos);
            if (startPos > 7) startPos = 1;
            uint8_t endPos = (tc.endPos > startPos && tc.endPos <= 8) ? tc.endPos : 8;
            uint8_t strength = NormalizeForce(tc.force);
            uint8_t snapForce = NormalizeForce(tc.force);
            uint16_t startAndStop = (uint16_t)((1 << startPos) | (1 << endPos));
            uint32_t forcePair = (uint32_t)((((strength - 1) & 0x07) << 0) | (((snapForce - 1) & 0x07) << 3));
            p[0] = 0x22; // Bow Mode
            p[1] = (uint8_t)(startAndStop & 0xFF);
            p[2] = (uint8_t)((startAndStop >> 8) & 0xFF);
            p[3] = (uint8_t)(forcePair & 0xFF);
            p[4] = (uint8_t)((forcePair >> 8) & 0xFF);
            break;
        }

        case TriggerMode::TriggerStop: { // 0x25 Weapon (Click-Snap barrier break point)
            uint8_t startPos = NormalizeZone(tc.startPos);
            if (startPos > 7) startPos = 2;
            uint8_t endPos = (tc.endPos > startPos && tc.endPos <= 9) ? tc.endPos : (uint8_t)((startPos + 4 < 9) ? (startPos + 4) : 9);
            uint8_t strength = NormalizeForce(tc.force);
            uint16_t startAndStop = (uint16_t)((1 << startPos) | (1 << endPos));
            p[0] = 0x25; // Weapon Mode
            p[1] = (uint8_t)(startAndStop & 0xFF);
            p[2] = (uint8_t)((startAndStop >> 8) & 0xFF);
            p[3] = (uint8_t)((strength - 1) & 0x07);
            break;
        }

        default:
            p[0] = 0x00;
            break;
        }
    }

    static void BuildOutputReportUSB(uint8_t* out, size_t outLen, const ControllerConfig& config, uint8_t leftRumble, uint8_t rightRumble, bool micMuted, float masterVol = 1.0f, bool hpConnected = false) {
        if (outLen < 48) return;
        std::memset(out, 0, outLen);

        out[0] = 0x02; // DualSense USB Output Report ID
        
        // valid_flag0:
        // 0x01: Compatible Vibration / Rumble
        // 0x02: Haptics Select
        // 0x04: Right Trigger Motor Enable
        // 0x08: Left Trigger Motor Enable
        // 0x10: Headphone Volume Enable
        // 0x20: Speaker Volume Enable
        // 0x40: Internal Microphone Volume / Preamp Enable
        // 0x80: Audio Control Enable
        out[1] = 0x0F | 0x10 | 0x20 | 0x40 | 0x80; 

        // valid_flag1:
        // 0x01: Mic Mute LED
        // 0x04: Lightbar Setup
        // 0x10: Player LEDs
        // 0x20: Lightbar RGB
        out[2] = 0x35; 

        // Rumble
        out[3] = rightRumble;
        out[4] = leftRumble;

        // Hardware volume scaling mapped from Windows master volume (0.0 .. 1.0f)
        uint8_t hpVol = (uint8_t)((masterVol * 127.0f < 0.0f) ? 0 : (masterVol * 127.0f > 127.0f ? 127 : (int)(masterVol * 127.0f)));
        uint8_t spkVol = (uint8_t)((masterVol * 255.0f < 0.0f) ? 0 : (masterVol * 255.0f > 255.0f ? 255 : (int)(masterVol * 255.0f)));

        if (config.speakerEnabled) {
            // Speaker Output Enabled:
            // Route Right audio channel to internal mono speaker (0x30 = X_X_R: headphone DAC muted)
            out[8] = 0x30;
            // Mute headphone DAC volume so analog headphone load does not sag the shared internal power rail
            out[5] = 0x00;
            // Calibrate gain when aux unplugged vs plugged to prevent perceived volume jump
            out[6] = hpConnected ? spkVol : (uint8_t)(spkVol * 0.82f);
        } else {
            // Headphone Output:
            // Route pristine stereo audio to 3.5mm jack (0x00 = L_R_X: stereo headphones, speaker disabled)
            out[8] = 0x00;
            out[5] = hpVol;
            out[6] = 0x00;
        }

        // Internal Dual-Mic Array Preamp Gain (0x00..0x40, boosted when micBeamforming enabled)
        out[7] = micMuted ? 0x00 : (config.micBeamforming ? 0x40 : 0x2C);

        // Mic Mute LED (0=off, 1=solid amber, 2=pulse)
        out[9] = micMuted ? 0x01 : 0x00;

        // Right Trigger
        FormatTriggerPayload(&out[11], config.rightTrigger);

        // Left Trigger
        FormatTriggerPayload(&out[22], config.leftTrigger);

        // valid_flag2 (out[39]):
        // 0x04: DS_OUTPUT_VALID_FLAG2_COMPATIBLE_VIBRATION2 (ensures rumble fires even with active USB Audio)
        out[39] = 0x04;

        // Player LEDs
        out[44] = config.playerLeds;

        // Lightbar RGB
        out[45] = config.ledR;
        out[46] = config.ledG;
        out[47] = config.ledB;
    }

    static void BuildOutputReportBT(uint8_t* out, size_t outLen, const ControllerConfig& config, uint8_t leftRumble, uint8_t rightRumble, bool micMuted, float masterVol = 1.0f, bool hpConnected = false) {
        if (outLen < 78) return;
        std::memset(out, 0, outLen);

        out[0] = 0x31; // DualSense BT Output Report ID
        out[1] = 0x02; // Tag

        out[2] = 0x0F | 0x10 | 0x20 | 0x40 | 0x80; // valid_flag0 (rumble + triggers + headphone/speaker/mic volume + audio control)
        out[3] = 0x35; // valid_flag1 (LEDs + lightbar)

        out[4] = rightRumble;
        out[5] = leftRumble;

        uint8_t hpVol = (uint8_t)((masterVol * 127.0f < 0.0f) ? 0 : (masterVol * 127.0f > 127.0f ? 127 : (int)(masterVol * 127.0f)));
        uint8_t spkVol = (uint8_t)((masterVol * 255.0f < 0.0f) ? 0 : (masterVol * 255.0f > 255.0f ? 255 : (int)(masterVol * 255.0f)));

        if (config.speakerEnabled) {
            out[9] = 0x30;
            out[6] = 0x00;
            out[7] = hpConnected ? spkVol : (uint8_t)(spkVol * 0.82f);
        } else {
            out[9] = 0x00;
            out[6] = hpVol;
            out[7] = 0x00;
        }
        out[8] = micMuted ? 0x00 : (config.micBeamforming ? 0x40 : 0x2C);

        out[10] = micMuted ? 0x01 : 0x00;

        // Right Trigger
        FormatTriggerPayload(&out[12], config.rightTrigger);

        // Left Trigger
        FormatTriggerPayload(&out[23], config.leftTrigger);

        // valid_flag2 (out[40] in Bluetooth report):
        // 0x04: DS_OUTPUT_VALID_FLAG2_COMPATIBLE_VIBRATION2
        out[40] = 0x04;

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
