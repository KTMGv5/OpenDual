using System;
using System.Collections.Generic;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public class XmbCategory
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Icon { get; set; } = "";
        public List<XmbItem> Items { get; set; } = new();
    }

    public class XmbItem
    {
        public string Id { get; set; } = "";
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string Icon { get; set; } = "";
        public string ValueBadge { get; set; } = "";
        public Action? OnActivate { get; set; }
    }

    public class XmbManager
    {
        public List<XmbCategory> Categories { get; } = new();
        public int ActiveCategoryIndex { get; private set; } = 2; // Default to Controller
        public int ActiveItemIndex { get; private set; } = 0;

        public event Action<int>? CategoryChanged;
        public event Action<int, int>? ItemChanged;
        public event Action<XmbItem>? ItemActivated;

        public XmbManager()
        {
            InitializeDefaultCategories();
        }

        private void InitializeDefaultCategories()
        {
            // 0. USERS & PROFILES
            var catUsers = new XmbCategory { Id = "users", Title = "PROFILES", Icon = "👤" };
            catUsers.Items.Add(new XmbItem { Id = "prof_default", Title = "Standard Balanced", Subtitle = "Standard haptics & adaptive triggers", Icon = "🎮", ValueBadge = "Active" });
            catUsers.Items.Add(new XmbItem { Id = "prof_esports", Title = "Esports Ultra 1000Hz", Subtitle = "1ms polling, Hall-stabilizer & instant click", Icon = "🏆", ValueBadge = "Esports" });
            catUsers.Items.Add(new XmbItem { Id = "prof_fps", Title = "FPS Gyro Specialist", Subtitle = "6-axis motion aim with spatial footstep radar", Icon = "🎯", ValueBadge = "Gyro Aim" });
            catUsers.Items.Add(new XmbItem { Id = "prof_racing", Title = "Racing Force Feedback", Subtitle = "Progressive brake wall & throttle resistance", Icon = "🏎️", ValueBadge = "Dynamic" });
            catUsers.Items.Add(new XmbItem { Id = "prof_new", Title = "Create New Profile", Subtitle = "Save custom settings for specific games", Icon = "➕", ValueBadge = "New" });
            Categories.Add(catUsers);

            // 1. SETTINGS & SYSTEM
            var catSettings = new XmbCategory { Id = "settings", Title = "SETTINGS", Icon = "⚙️" };
            catSettings.Items.Add(new XmbItem { Id = "set_driver", Title = "Direct HID Driver Service", Subtitle = "Win32 overlapped 1000Hz kernel service", Icon = "⚡", ValueBadge = "1000Hz Active" });
            catSettings.Items.Add(new XmbItem { Id = "set_autostart", Title = "Launch on Windows Startup", Subtitle = "Start minimized in system tray on boot", Icon = "🚀", ValueBadge = "Enabled" });
            catSettings.Items.Add(new XmbItem { Id = "set_idle", Title = "Battery Idle Sleep Timer", Subtitle = "Auto turn off controller when idle", Icon = "💤", ValueBadge = "10 Mins" });
            catSettings.Items.Add(new XmbItem { Id = "set_update", Title = "OpenDual Software Update", Subtitle = "Check for latest driver and hub releases", Icon = "🔄", ValueBadge = "v1.6.6 Latest" });
            Categories.Add(catSettings);

            // 2. CONTROLLER (DEFAULT)
            var catController = new XmbCategory { Id = "controller", Title = "CONTROLLER", Icon = "🎮" };
            catController.Items.Add(new XmbItem { Id = "ctrl_status", Title = "Controller Overview", Subtitle = "Controller Wireless (USB 1000Hz)", Icon = "🎮", ValueBadge = "Connected" });
            catController.Items.Add(new XmbItem { Id = "ctrl_input", Title = "Live Input Visualizer", Subtitle = "Real-time sticks, triggers, D-Pad & buttons", Icon = "🕹️", ValueBadge = "Live Test" });
            catController.Items.Add(new XmbItem { Id = "ctrl_overclock", Title = "1000 Hz USB Overclock Assistant", Subtitle = "SweetLow HIDUSBF kernel filter booster", Icon = "⚡", ValueBadge = "1.0 ms" });
            catController.Items.Add(new XmbItem { Id = "ctrl_battery_sentinel", Title = "Battery Longevity Sentinel", Subtitle = "80% Esports smart charge limit & health", Icon = "🔋", ValueBadge = "98% Pro" });
            catController.Items.Add(new XmbItem { Id = "ctrl_hall_stabilizer", Title = "Virtual Hall-Effect Stabilizer", Subtitle = "Anti-jitter thumbstick noise suppressor", Icon = "🧠", ValueBadge = "Active" });
            catController.Items.Add(new XmbItem { Id = "ctrl_deep_suite", Title = "10 Deep Hardware Modifiers", Subtitle = "Dual-stage triggers, rapid-reset, sprint shelf & IMU guard", Icon = "🔬", ValueBadge = "10 Mods" });
            Categories.Add(catController);

            // 3. LIGHTBAR
            var catLightbar = new XmbCategory { Id = "lightbar", Title = "LIGHTBAR", Icon = "🌈" };
            catLightbar.Items.Add(new XmbItem { Id = "led_effect", Title = "RGB Lightbar Mode", Subtitle = "Rainbow wave, solid color, pulse, game sync", Icon = "🌈", ValueBadge = "Rainbow Wave" });
            catLightbar.Items.Add(new XmbItem { Id = "led_color", Title = "Custom RGB Color Picker", Subtitle = "Personalize lightbar glow hues and saturation", Icon = "🎨", ValueBadge = "Signature Cyan" });
            catLightbar.Items.Add(new XmbItem { Id = "led_players", Title = "Player LED Indicators", Subtitle = "Configure 5-LED player status under touchpad", Icon = "💡", ValueBadge = "Player 1" });
            catLightbar.Items.Add(new XmbItem { Id = "led_brightness", Title = "Lightbar Brightness", Subtitle = "Adjust brightness level to preserve battery", Icon = "🔆", ValueBadge = "100%" });
            Categories.Add(catLightbar);

            // 4. TRIGGERS
            var catTriggers = new XmbCategory { Id = "triggers", Title = "TRIGGERS", Icon = "⚡" };
            catTriggers.Items.Add(new XmbItem { Id = "trig_l2", Title = "Left Trigger (L2) Mode", Subtitle = "Section barrier, bow tension, or spring", Icon = "⚡", ValueBadge = "Rigid Snap" });
            catTriggers.Items.Add(new XmbItem { Id = "trig_r2", Title = "Right Trigger (R2) Mode", Subtitle = "Rapid machine gun recoil, bow, or game SDK", Icon = "⚡", ValueBadge = "Game Profile" });
            catTriggers.Items.Add(new XmbItem { Id = "trig_oscilloscope", Title = "Force Resistance Oscilloscope", Subtitle = "Real-time mechanical motor torque curve", Icon = "📈", ValueBadge = "Oscilloscope" });
            catTriggers.Items.Add(new XmbItem { Id = "trig_snap", Title = "Click-Snap Barrier Tension", Subtitle = "Tactile wall threshold & breakthrough force", Icon = "🎯", ValueBadge = "85% Force" });
            Categories.Add(catTriggers);

            // 5. GYRO AIM
            var catGyro = new XmbCategory { Id = "gyro", Title = "GYRO AIM", Icon = "🎯" };
            catGyro.Items.Add(new XmbItem { Id = "gyro_enable", Title = "6-Axis Gyroscope Aiming", Subtitle = "Natural motion-assisted crosshair tracking", Icon = "🎯", ValueBadge = "Disabled" });
            catGyro.Items.Add(new XmbItem { Id = "gyro_output", Title = "Output Emulation Mode", Subtitle = "Direct Windows Mouse or XInput Right Thumbstick", Icon = "🖱️", ValueBadge = "Mouse Aim" });
            catGyro.Items.Add(new XmbItem { Id = "gyro_click", Title = "Mouse Click Keybindings", Subtitle = "Cross (A) / Square (X) or Triggers mouse clicks", Icon = "🖱️", ValueBadge = "Cross / Square" });
            catGyro.Items.Add(new XmbItem { Id = "gyro_tuning", Title = "Sensitivity & Response Curve", Subtitle = "X/Y sensitivity, smoothing filter, and deadzone", Icon = "🎚️", ValueBadge = "Sens 1.2x" });
            catGyro.Items.Add(new XmbItem { Id = "gyro_wizard", Title = "Interactive Calibrator Wizard", Subtitle = "Crosshair point-and-click IMU calibration", Icon = "🪄", ValueBadge = "Calibrate" });
            Categories.Add(catGyro);

            // 6. TOUCHPAD
            var catTouchpad = new XmbCategory { Id = "touchpad", Title = "TOUCHPAD", Icon = "🖱️" };
            catTouchpad.Items.Add(new XmbItem { Id = "touch_mouse", Title = "Touchpad Mouse Navigation", Subtitle = "Dual-point capacitive desktop cursor navigation", Icon = "🖱️", ValueBadge = "Active" });
            catTouchpad.Items.Add(new XmbItem { Id = "touch_vol", Title = "Right-Edge Volume Slider", Subtitle = "Vertical edge slide gesture for audio volume", Icon = "📜", ValueBadge = "Off (Default)" });
            catTouchpad.Items.Add(new XmbItem { Id = "touch_tap", Title = "Tap-to-Click & Multi-Touch", Subtitle = "Tap surface to click without depressing button", Icon = "🤏", ValueBadge = "Enabled" });
            Categories.Add(catTouchpad);

            // 7. AUDIO & HAPTICS
            var catAudio = new XmbCategory { Id = "audio", Title = "AUDIO", Icon = "🔊" };
            catAudio.Items.Add(new XmbItem { Id = "aud_haptic", Title = "Tactile Voice-Coil Haptics", Subtitle = "Dual wideband voice-coil tactile actuators", Icon = "🔊", ValueBadge = "Active" });
            catAudio.Items.Add(new XmbItem { Id = "aud_spatial_radar", Title = "Directional Spatial Audio Radar", Subtitle = "Acoustic footstep tactile pulse virtualizer", Icon = "🎧", ValueBadge = "Spatial On" });
            catAudio.Items.Add(new XmbItem { Id = "aud_reactive", Title = "Audio-to-Haptics Reactive Bass", Subtitle = "Real-time 40Hz sub-bass recoil haptic rumble", Icon = "🌊", ValueBadge = "30% Smooth" });
            catAudio.Items.Add(new XmbItem { Id = "aud_speaker", Title = "Controller Speaker Output", Subtitle = "Play sound directly through controller DAC", Icon = "📢", ValueBadge = "Toggle Off" });
            catAudio.Items.Add(new XmbItem { Id = "aud_mic", Title = "Microphone Mute Synchronization", Subtitle = "Hardware LED sync and on-screen HUD toast", Icon = "🎙️", ValueBadge = "Synced" });
            Categories.Add(catAudio);

            // 8. HARDWARE LAB
            var catLab = new XmbCategory { Id = "lab", Title = "HARDWARE LAB", Icon = "🔬" };
            catLab.Items.Add(new XmbItem { Id = "lab_gimbal", Title = "3D Spatial Gimbal & IMU", Subtitle = "Real-time 3D controller rotation & G-Forces", Icon = "🔬", ValueBadge = "Live 3D" });
            catLab.Items.Add(new XmbItem { Id = "lab_circ", Title = "Stick Circularity Benchmarker", Subtitle = "32-bin angular radar & boundary error metric", Icon = "⭕", ValueBadge = "3.8% Pro" });
            catLab.Items.Add(new XmbItem { Id = "lab_zeroshift", Title = "Dynamic Zero-Shift Drift Calibrator", Subtitle = "Driver-level potentiometer center offset", Icon = "⚖️", ValueBadge = "0.00 Drift" });
            catLab.Items.Add(new XmbItem { Id = "lab_jitter", Title = "1000Hz Polling Latency Jitter", Subtitle = "Real-time frame sparkline oscilloscope & σ", Icon = "📊", ValueBadge = "±0.03 ms" });
            Categories.Add(catLab);
        }

        public XmbCategory ActiveCategory => Categories[Math.Clamp(ActiveCategoryIndex, 0, Categories.Count - 1)];

        public XmbItem? ActiveItem
        {
            get
            {
                var cat = ActiveCategory;
                if (cat.Items.Count == 0) return null;
                return cat.Items[Math.Clamp(ActiveItemIndex, 0, cat.Items.Count - 1)];
            }
        }

        #region Mouse & Keyboard Navigation
        public void NavigateCategory(int delta)
        {
            int newIdx = (ActiveCategoryIndex + delta) % Categories.Count;
            if (newIdx < 0) newIdx += Categories.Count;
            ActiveCategoryIndex = newIdx;
            ActiveItemIndex = 0; // Reset item selection to top of new category
            CategoryChanged?.Invoke(ActiveCategoryIndex);
            ItemChanged?.Invoke(ActiveCategoryIndex, ActiveItemIndex);
        }

        public void NavigateItem(int delta)
        {
            var cat = ActiveCategory;
            if (cat.Items.Count == 0) return;

            int newIdx = ActiveItemIndex + delta;
            if (newIdx < 0) newIdx = 0;
            if (newIdx >= cat.Items.Count) newIdx = cat.Items.Count - 1;

            if (newIdx != ActiveItemIndex)
            {
                ActiveItemIndex = newIdx;
                ItemChanged?.Invoke(ActiveCategoryIndex, ActiveItemIndex);
            }
        }

        public void SelectCategory(int index)
        {
            if (index >= 0 && index < Categories.Count && index != ActiveCategoryIndex)
            {
                ActiveCategoryIndex = index;
                ActiveItemIndex = 0;
                CategoryChanged?.Invoke(ActiveCategoryIndex);
                ItemChanged?.Invoke(ActiveCategoryIndex, ActiveItemIndex);
            }
        }

        public void SelectItem(int index)
        {
            var cat = ActiveCategory;
            if (index >= 0 && index < cat.Items.Count)
            {
                ActiveItemIndex = index;
                ItemChanged?.Invoke(ActiveCategoryIndex, ActiveItemIndex);
            }
        }

        public void ActivateCurrentItem()
        {
            var item = ActiveItem;
            if (item != null)
            {
                ItemActivated?.Invoke(item);
            }
        }
        #endregion
    }
}
