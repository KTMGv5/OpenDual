using System;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace OpenDualHub
{
    public static class ThemeManager
    {
        public static bool IsDarkMode { get; private set; } = true;
        public static Color CurrentAccentColor { get; private set; } = Color.FromRgb(0, 120, 212);

        public static void Initialize(ResourceDictionary resources)
        {
            ApplySystemTheme(resources);
        }

        public static void ApplySystemTheme(ResourceDictionary resources)
        {
            IsDarkMode = DetectIsDarkMode();
            CurrentAccentColor = DetectSystemAccentColor();

            Color headerAccent = CurrentAccentColor;
            Color accentSignature;

            if (IsDarkMode)
            {
                // Dark Mode System Palette
                accentSignature = BrightenColor(CurrentAccentColor, 1.25f);

                resources["BgPrimary"] = new SolidColorBrush(Color.FromRgb(0x0A, 0x0D, 0x14));
                resources["BgSecondary"] = new SolidColorBrush(Color.FromRgb(0x12, 0x16, 0x22));
                resources["BgCard"] = new SolidColorBrush(Color.FromRgb(0x18, 0x1F, 0x2E));
                resources["BgCardHover"] = new SolidColorBrush(Color.FromRgb(0x22, 0x2B, 0x3F));
                resources["InputBg"] = new SolidColorBrush(Color.FromRgb(0x13, 0x18, 0x24));
                resources["InputBorder"] = new SolidColorBrush(Color.FromRgb(0x28, 0x35, 0x4D));
                resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                resources["TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x8C, 0x9B, 0xB0));
                resources["BorderColor"] = new SolidColorBrush(Color.FromRgb(0x24, 0x30, 0x46));
                resources["SlotBg"] = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1A));
                resources["PillBg"] = new SolidColorBrush(Color.FromRgb(0x1F, 0x28, 0x3B));
            }
            else
            {
                // Light Mode System Palette
                headerAccent = DarkenColor(CurrentAccentColor, 0.85f);
                accentSignature = CurrentAccentColor;

                resources["BgPrimary"] = new SolidColorBrush(Color.FromRgb(0xF5, 0xF7, 0xFA));
                resources["BgSecondary"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                resources["BgCard"] = new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF));
                resources["BgCardHover"] = new SolidColorBrush(Color.FromRgb(0xED, 0xF2, 0xF7));
                resources["InputBg"] = new SolidColorBrush(Color.FromRgb(0xF0, 0xF4, 0xF8));
                resources["InputBorder"] = new SolidColorBrush(Color.FromRgb(0xCB, 0xD5, 0xE1));
                resources["TextPrimary"] = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A));
                resources["TextSecondary"] = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B));
                resources["BorderColor"] = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
                resources["SlotBg"] = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
                resources["PillBg"] = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0));
            }

            // Apply System Accent Color Brushes
            resources["HeaderAccent"] = new SolidColorBrush(headerAccent);
            resources["AccentSignature"] = new SolidColorBrush(accentSignature);
            resources["AccentBlue"] = new SolidColorBrush(headerAccent);
        }

        private static bool DetectIsDarkMode()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                if (key != null)
                {
                    object? val = key.GetValue("AppsUseLightTheme");
                    if (val is int intVal)
                    {
                        return intVal == 0;
                    }
                }
            }
            catch { }
            return true; // Default to dark mode
        }

        private static Color DetectSystemAccentColor()
        {
            try
            {
                using var dwmKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\DWM");
                if (dwmKey != null)
                {
                    object? accentVal = dwmKey.GetValue("AccentColor");
                    if (accentVal is int accentInt)
                    {
                        uint uVal = (uint)accentInt;
                        // Format is AABBGGRR
                        byte r = (byte)(uVal & 0xFF);
                        byte g = (byte)((uVal >> 8) & 0xFF);
                        byte b = (byte)((uVal >> 16) & 0xFF);
                        if (r != 0 || g != 0 || b != 0)
                        {
                            return Color.FromRgb(r, g, b);
                        }
                    }

                    object? colorizationVal = dwmKey.GetValue("ColorizationColor");
                    if (colorizationVal is int colorInt)
                    {
                        uint uVal = (uint)colorInt;
                        // Format is AARRGGBB
                        byte r = (byte)((uVal >> 16) & 0xFF);
                        byte g = (byte)((uVal >> 8) & 0xFF);
                        byte b = (byte)(uVal & 0xFF);
                        if (r != 0 || g != 0 || b != 0)
                        {
                            return Color.FromRgb(r, g, b);
                        }
                    }
                }
            }
            catch { }

            try
            {
                var glass = SystemParameters.WindowGlassColor;
                if (glass.R != 0 || glass.G != 0 || glass.B != 0)
                {
                    return glass;
                }
            }
            catch { }

            // Default Windows Accent Blue
            return Color.FromRgb(0x00, 0x78, 0xD4);
        }

        private static Color BrightenColor(Color c, float factor)
        {
            byte r = (byte)Math.Clamp((int)(c.R * factor + (255 - c.R) * 0.2f), 0, 255);
            byte g = (byte)Math.Clamp((int)(c.G * factor + (255 - c.G) * 0.2f), 0, 255);
            byte b = (byte)Math.Clamp((int)(c.B * factor + (255 - c.B) * 0.2f), 0, 255);
            return Color.FromRgb(r, g, b);
        }

        private static Color DarkenColor(Color c, float factor)
        {
            byte r = (byte)Math.Clamp((int)(c.R * factor), 0, 255);
            byte g = (byte)Math.Clamp((int)(c.G * factor), 0, 255);
            byte b = (byte)Math.Clamp((int)(c.B * factor), 0, 255);
            return Color.FromRgb(r, g, b);
        }
    }
}
