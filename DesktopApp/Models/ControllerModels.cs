using System;
using System.Text.Json.Serialization;

namespace OpenDualHub.Models
{
    public class TouchPointDto
    {
        [JsonPropertyName("on")]
        public bool On { get; set; }

        [JsonPropertyName("x")]
        public int X { get; set; }

        [JsonPropertyName("y")]
        public int Y { get; set; }
    }

    public class ConfigDto
    {
        [JsonPropertyName("r")]
        public int R { get; set; } = 0;

        [JsonPropertyName("g")]
        public int G { get; set; } = 120;

        [JsonPropertyName("b")]
        public int B { get; set; } = 255;

        [JsonPropertyName("effect")]
        public int Effect { get; set; } = 2;

        [JsonPropertyName("speed")]
        public float Speed { get; set; } = 1.0f;

        [JsonPropertyName("mouse")]
        public bool Mouse { get; set; } = true;

        [JsonPropertyName("mouseSens")]
        public float MouseSens { get; set; } = 1.2f;

        [JsonPropertyName("scrollSens")]
        public float ScrollSens { get; set; } = 1.0f;

        [JsonPropertyName("syncMic")]
        public bool SyncMic { get; set; } = true;

        [JsonPropertyName("ltMode")]
        public int LtMode { get; set; } = 0;

        [JsonPropertyName("rtMode")]
        public int RtMode { get; set; } = 0;

        [JsonPropertyName("haptic")]
        public bool Haptic { get; set; } = false;

        [JsonPropertyName("hapticIntensity")]
        public float HapticIntensity { get; set; } = 0.30f;

        [JsonPropertyName("hapticCutoff")]
        public int HapticCutoff { get; set; } = 40;

        [JsonPropertyName("hapticStereo")]
        public bool HapticStereo { get; set; } = true;

        [JsonPropertyName("audioTriggerKick")]
        public bool AudioTriggerKick { get; set; } = false;

        [JsonPropertyName("audioTriggerThreshold")]
        public float AudioTriggerThreshold { get; set; } = 0.20f;

        [JsonPropertyName("speakerEnabled")]
        public bool SpeakerEnabled { get; set; } = false;

        // 6-Axis Gyro Aiming
        [JsonPropertyName("gyro")]
        public bool Gyro { get; set; } = false;

        [JsonPropertyName("gyroMode")]
        public int GyroMode { get; set; } = 1;

        [JsonPropertyName("gyroOutputMode")]
        public int GyroOutputMode { get; set; } = 0;

        [JsonPropertyName("gyroSpace")]
        public int GyroSpace { get; set; } = 0;

        [JsonPropertyName("gyroSensX")]
        public float GyroSensX { get; set; } = 1.0f;

        [JsonPropertyName("gyroSensY")]
        public float GyroSensY { get; set; } = 1.0f;

        [JsonPropertyName("gyroInvertX")]
        public bool GyroInvertX { get; set; } = false;

        [JsonPropertyName("gyroInvertY")]
        public bool GyroInvertY { get; set; } = false;

        [JsonPropertyName("gyroDeadzone")]
        public int GyroDeadzone { get; set; } = 3;

        [JsonPropertyName("gyroSmooth")]
        public float GyroSmooth { get; set; } = 0.20f;

        // Power Management & Battery
        [JsonPropertyName("idleTimeout")]
        public int IdleTimeout { get; set; } = 10;

        [JsonPropertyName("lowBatWarn")]
        public bool LowBatWarn { get; set; } = true;
    }

    public class ControllerTelemetry
    {
        [JsonPropertyName("connected")]
        public bool Connected { get; set; }

        [JsonPropertyName("model")]
        public int Model { get; set; } // 1=DS4v1, 2=DS4v2, 3=DualSense, 4=DualSenseEdge

        [JsonPropertyName("conn")]
        public int Connection { get; set; } // 1=USB, 2=BT

        [JsonPropertyName("battery")]
        public int Battery { get; set; }

        [JsonPropertyName("charging")]
        public bool Charging { get; set; }

        [JsonPropertyName("hz")]
        public int PollingRateHz { get; set; }

        [JsonPropertyName("latency")]
        public float LatencyMs { get; set; }

        [JsonPropertyName("reports")]
        public ulong TotalReports { get; set; }

        // Analog Sticks (-1.0 to 1.0)
        [JsonPropertyName("lx")]
        public float LX { get; set; }

        [JsonPropertyName("ly")]
        public float LY { get; set; }

        [JsonPropertyName("rx")]
        public float RX { get; set; }

        [JsonPropertyName("ry")]
        public float RY { get; set; }

        // Triggers (0.0 to 1.0)
        [JsonPropertyName("l2")]
        public float L2 { get; set; }

        [JsonPropertyName("r2")]
        public float R2 { get; set; }

        [JsonPropertyName("dpad")]
        public int DPad { get; set; }

        [JsonIgnore]
        public bool DPadUp => DPad == 0 || DPad == 1 || DPad == 7;

        [JsonIgnore]
        public bool DPadRight => DPad == 1 || DPad == 2 || DPad == 3;

        [JsonIgnore]
        public bool DPadDown => DPad == 3 || DPad == 4 || DPad == 5;

        [JsonIgnore]
        public bool DPadLeft => DPad == 5 || DPad == 6 || DPad == 7;

        // Buttons
        [JsonPropertyName("sq")]
        public bool Square { get; set; }

        [JsonPropertyName("cr")]
        public bool Cross { get; set; }

        [JsonPropertyName("ci")]
        public bool Circle { get; set; }

        [JsonPropertyName("tr")]
        public bool Triangle { get; set; }

        [JsonPropertyName("l1")]
        public bool L1 { get; set; }

        [JsonPropertyName("r1")]
        public bool R1 { get; set; }

        [JsonPropertyName("l2b")]
        public bool L2Btn { get; set; }

        [JsonPropertyName("r2b")]
        public bool R2Btn { get; set; }

        [JsonPropertyName("share")]
        public bool Share { get; set; }

        [JsonPropertyName("options")]
        public bool Options { get; set; }

        [JsonPropertyName("l3")]
        public bool L3 { get; set; }

        [JsonPropertyName("r3")]
        public bool R3 { get; set; }

        [JsonPropertyName("ps")]
        public bool PS { get; set; }

        [JsonPropertyName("padBtn")]
        public bool TouchpadBtn { get; set; }

        [JsonPropertyName("micBtn")]
        public bool MicBtn { get; set; }

        [JsonPropertyName("micMuted")]
        public bool MicMuted { get; set; }

        [JsonPropertyName("hpConnected")]
        public bool HeadphonesConnected { get; set; }

        [JsonPropertyName("masterVol")]
        public float MasterVolume { get; set; } = 1.0f;

        [JsonPropertyName("fnL")]
        public bool FnLeft { get; set; }

        [JsonPropertyName("fnR")]
        public bool FnRight { get; set; }

        [JsonPropertyName("padL")]
        public bool PaddleLeft { get; set; }

        [JsonPropertyName("padR")]
        public bool PaddleRight { get; set; }

        [JsonPropertyName("gyroActive")]
        public bool GyroActive { get; set; }

        [JsonPropertyName("isIdle")]
        public bool IsIdle { get; set; }

        [JsonPropertyName("gyroX")]
        public short GyroX { get; set; }

        [JsonPropertyName("gyroY")]
        public short GyroY { get; set; }

        [JsonPropertyName("gyroZ")]
        public short GyroZ { get; set; }

        [JsonPropertyName("accelX")]
        public short AccelX { get; set; }

        [JsonPropertyName("accelY")]
        public short AccelY { get; set; }

        [JsonPropertyName("accelZ")]
        public short AccelZ { get; set; }

        [JsonPropertyName("hapticL")]
        public int HapticL { get; set; }

        [JsonPropertyName("hapticR")]
        public int HapticR { get; set; }

        [JsonPropertyName("renderDev")]
        public string? RenderDev { get; set; }

        [JsonPropertyName("captureDev")]
        public string? CaptureDev { get; set; }

        // Touchpad
        [JsonPropertyName("t1")]
        public TouchPointDto Touch1 { get; set; } = new();

        [JsonPropertyName("t2")]
        public TouchPointDto Touch2 { get; set; } = new();

        [JsonPropertyName("cfg")]
        public ConfigDto Config { get; set; } = new();

        public string ModelName => Model switch
        {
            1 => "PlayStation 4 DualShock (v1)",
            2 => "PlayStation 4 DualShock (v2)",
            3 => "PlayStation 5 DualSense",
            4 => "PlayStation 5 DualSense Edge",
            _ => "Unknown Controller"
        };

        public string ConnectionName => Connection switch
        {
            1 => "USB (Wired)",
            2 => "Bluetooth (Wireless)",
            _ => "Unknown"
        };
    }
}
