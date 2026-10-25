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
        public int Effect { get; set; } = 0;

        [JsonPropertyName("speed")]
        public float Speed { get; set; } = 1.0f;

        [JsonPropertyName("mouse")]
        public bool Mouse { get; set; } = true;

        [JsonPropertyName("syncMic")]
        public bool SyncMic { get; set; } = true;

        [JsonPropertyName("ltMode")]
        public int LtMode { get; set; } = 0;

        [JsonPropertyName("rtMode")]
        public int RtMode { get; set; } = 0;

        [JsonPropertyName("haptic")]
        public bool Haptic { get; set; } = false;

        [JsonPropertyName("hapticIntensity")]
        public float HapticIntensity { get; set; } = 1.0f;

        [JsonPropertyName("hapticCutoff")]
        public int HapticCutoff { get; set; } = 100;

        [JsonPropertyName("hapticStereo")]
        public bool HapticStereo { get; set; } = true;
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

        [JsonPropertyName("hapticL")]
        public int HapticL { get; set; }

        [JsonPropertyName("hapticR")]
        public int HapticR { get; set; }

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
