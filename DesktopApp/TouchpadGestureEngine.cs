using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public enum TouchGestureType
    {
        None,
        SwipeLeft,
        SwipeRight,
        SwipeUp,
        SwipeDown,
        LetterC,
        LetterV,
        LetterS,
        LetterZ,
        LetterO,
        TwoFingerTap,
        ThreeFingerTap,
        ZoneTopLeft,
        ZoneTopRight,
        ZoneBottomLeft,
        ZoneBottomRight
    }

    public enum GestureAction
    {
        None,
        TakeScreenshot,     // Win + Shift + S
        SaveReplayClip,     // Win + Alt + G
        ToggleMicMute,      // System Mic
        PlayPauseMedia,     // VK_MEDIA_PLAY_PAUSE
        NextTrack,          // VK_MEDIA_NEXT_TRACK
        PreviousTrack,      // VK_MEDIA_PREV_TRACK
        OpenQuickHud,       // Open PS Control Center
        OpenDualHub,        // Bring Hub window to front
        OpenGameBar,        // Win + G
        LaunchCalculator,   // calc.exe
        DiscordMute,        // Ctrl + Shift + M
        DiscordDeafen,      // Ctrl + Shift + D
        ToggleRadialMenu    // On-screen 8-slot radial wheel
    }

    public class GestureMapping
    {
        public TouchGestureType Gesture { get; set; }
        public GestureAction Action { get; set; }
        public string Name { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }

    public class TouchpadGestureEngine
    {
        [DllImport("user32.dll")]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const byte VK_LWIN = 0x5B;
        private const byte VK_SHIFT = 0x10;
        private const byte VK_MENU = 0x12; // Alt
        private const byte VK_CONTROL = 0x11;
        private const byte VK_MEDIA_NEXT_TRACK = 0xB0;
        private const byte VK_MEDIA_PREV_TRACK = 0xB1;
        private const byte VK_MEDIA_PLAY_PAUSE = 0xB3;

        private readonly List<Point> _rawPoints = new();
        private DateTime _strokeStartTime = DateTime.MinValue;
        private bool _wasTouching = false;
        private bool _wasPadPressed = false;
        private int _maxFingers = 0;

        public bool GesturesEnabled { get; set; } = false;
        public bool ZoneMacroPadEnabled { get; set; } = false;
        public List<GestureMapping> Mappings { get; } = new();

        public event Action<TouchGestureType, GestureAction>? OnGestureRecognized;
        public event Action<List<Point>>? OnStrokeUpdated;
        public event Action? OnToggleRadialMenuRequested;

        public TouchpadGestureEngine()
        {
            InitializeDefaultMappings();
        }

        private void InitializeDefaultMappings()
        {
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.LetterC, Action = GestureAction.TakeScreenshot, Name = "Draw 'C' -> Screenshot (Win+Shift+S)" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.LetterV, Action = GestureAction.SaveReplayClip, Name = "Draw 'V' -> Save 30s Game Clip (Win+Alt+G)" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.LetterO, Action = GestureAction.OpenDualHub, Name = "Draw 'O' -> Open OpenDual Hub" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.SwipeLeft, Action = GestureAction.PreviousTrack, Name = "Swipe Left -> Previous Music Track" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.SwipeRight, Action = GestureAction.NextTrack, Name = "Swipe Right -> Next Music Track" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.SwipeUp, Action = GestureAction.PlayPauseMedia, Name = "Swipe Up -> Play / Pause Media" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.SwipeDown, Action = GestureAction.ToggleMicMute, Name = "Swipe Down -> Mute / Unmute Mic" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.LetterZ, Action = GestureAction.OpenGameBar, Name = "Draw 'Z' -> Open Xbox Game Bar" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.ZoneTopLeft, Action = GestureAction.DiscordMute, Name = "Top-Left Quadrant Click -> Discord Mute" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.ZoneTopRight, Action = GestureAction.DiscordDeafen, Name = "Top-Right Quadrant Click -> Discord Deafen" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.ZoneBottomLeft, Action = GestureAction.PlayPauseMedia, Name = "Bottom-Left Quadrant Click -> Play/Pause Media" });
            Mappings.Add(new GestureMapping { Gesture = TouchGestureType.ZoneBottomRight, Action = GestureAction.TakeScreenshot, Name = "Bottom-Right Quadrant Click -> Screenshot" });
        }

        public void LoadSavedMappings(Dictionary<string, string>? saved)
        {
            if (saved == null || saved.Count == 0) return;
            foreach (var kv in saved)
            {
                if (Enum.TryParse<TouchGestureType>(kv.Key, true, out var gType) &&
                    Enum.TryParse<GestureAction>(kv.Value, true, out var gAct))
                {
                    var existing = Mappings.FirstOrDefault(m => m.Gesture == gType);
                    if (existing != null)
                    {
                        existing.Action = gAct;
                    }
                    else
                    {
                        Mappings.Add(new GestureMapping { Gesture = gType, Action = gAct, Name = $"{gType} -> {gAct}" });
                    }
                }
            }
        }

        public void SetMapping(TouchGestureType gesture, GestureAction action)
        {
            var existing = Mappings.FirstOrDefault(m => m.Gesture == gesture);
            if (existing != null)
            {
                existing.Action = action;
            }
            else
            {
                Mappings.Add(new GestureMapping { Gesture = gesture, Action = action, Name = $"{gesture} -> {action}" });
            }

            AppSettingsManager.Current.GestureMappings[gesture.ToString()] = action.ToString();
            AppSettingsManager.Save();
        }

        public GestureAction GetMapping(TouchGestureType gesture)
        {
            var existing = Mappings.FirstOrDefault(m => m.Gesture == gesture);
            return existing?.Action ?? GestureAction.None;
        }

        public void ProcessTelemetry(ControllerTelemetry t)
        {
            if (t == null) return;

            // 4-Zone Touchpad Haptic Macro Pad (triggers on physical touchpad button click in quadrant)
            if (ZoneMacroPadEnabled && t.Touch1.On && t.TouchpadBtn && !_wasPadPressed)
            {
                bool leftHalf = t.Touch1.X < 960;
                bool topHalf = t.Touch1.Y < 470;
                TouchGestureType zone = (topHalf, leftHalf) switch
                {
                    (true, true) => TouchGestureType.ZoneTopLeft,
                    (true, false) => TouchGestureType.ZoneTopRight,
                    (false, true) => TouchGestureType.ZoneBottomLeft,
                    _ => TouchGestureType.ZoneBottomRight
                };
                ExecuteGesture(zone);
            }
            _wasPadPressed = t.TouchpadBtn;

            if (!GesturesEnabled) return;

            bool isTouching = t.Touch1.On;
            int fingers = (t.Touch1.On ? 1 : 0) + (t.Touch2.On ? 1 : 0);
            if (fingers > _maxFingers) _maxFingers = fingers;

            var now = DateTime.UtcNow;

            if (isTouching)
            {
                if (!_wasTouching)
                {
                    // Stroke start
                    _rawPoints.Clear();
                    _strokeStartTime = now;
                    _maxFingers = fingers;
                }

                _rawPoints.Add(new Point(t.Touch1.X, t.Touch1.Y));
                OnStrokeUpdated?.Invoke(new List<Point>(_rawPoints));
            }
            else if (_wasTouching)
            {
                // Stroke ended (finger lifted)
                double durationMs = (now - _strokeStartTime).TotalMilliseconds;
                if (_rawPoints.Count >= 4 && durationMs >= 70 && durationMs <= 2500)
                {
                    var recognized = ClassifyStroke(_rawPoints, durationMs, _maxFingers);
                    if (recognized != TouchGestureType.None)
                    {
                        ExecuteGesture(recognized);
                    }
                }
                _rawPoints.Clear();
                _maxFingers = 0;
            }

            _wasTouching = isTouching;
        }

        private TouchGestureType ClassifyStroke(List<Point> points, double durationMs, int fingerCount)
        {
            if (points.Count < 4) return TouchGestureType.None;

            Point start = points.First();
            Point end = points.Last();
            double dx = end.X - start.X;
            double dy = end.Y - start.Y;
            double totalDist = Math.Sqrt(dx * dx + dy * dy);

            // Compute Bounding Box
            double minX = points.Min(p => p.X);
            double maxX = points.Max(p => p.X);
            double minY = points.Min(p => p.Y);
            double maxY = points.Max(p => p.Y);
            double width = maxX - minX;
            double height = maxY - minY;

            // 1. Swipes (Rapid linear strokes with minimal orthogonal deviation)
            if (durationMs < 450 && totalDist > 350)
            {
                if (Math.Abs(dx) > Math.Abs(dy) * 1.8)
                {
                    return dx > 0 ? TouchGestureType.SwipeRight : TouchGestureType.SwipeLeft;
                }
                if (Math.Abs(dy) > Math.Abs(dx) * 1.8)
                {
                    return dy > 0 ? TouchGestureType.SwipeDown : TouchGestureType.SwipeUp;
                }
            }

            // 2. Multi-point Path Analysis for Unistroke Letters
            if (width > 120 && height > 120)
            {
                // Check if loop / circle 'O' (start and end close together, circular trajectory)
                double distStartEnd = Math.Sqrt(Math.Pow(end.X - start.X, 2) + Math.Pow(end.Y - start.Y, 2));
                if (distStartEnd < Math.Max(width, height) * 0.35 && points.Count > 15)
                {
                    return TouchGestureType.LetterO;
                }

                // Resample stroke into 16 points for uniform feature extraction
                var resampled = ResamplePath(points, 16);

                // Letter 'V': Starts top-left, goes down-right, sharp turn up-right
                if (IsLetterV(resampled, width, height)) return TouchGestureType.LetterV;

                // Letter 'C': Starts top-right, curves left, then curves bottom-right
                if (IsLetterC(resampled, width, height)) return TouchGestureType.LetterC;

                // Letter 'S': Double curve
                if (IsLetterS(resampled, width, height)) return TouchGestureType.LetterS;

                // Letter 'Z': Right, diagonal down-left, right
                if (IsLetterZ(resampled, width, height)) return TouchGestureType.LetterZ;
            }

            return TouchGestureType.None;
        }

        private static List<Point> ResamplePath(List<Point> points, int targetCount)
        {
            var result = new List<Point> { points[0] };
            double totalLen = 0;
            for (int i = 0; i < points.Count - 1; i++)
            {
                totalLen += Math.Sqrt(Math.Pow(points[i + 1].X - points[i].X, 2) + Math.Pow(points[i + 1].Y - points[i].Y, 2));
            }

            if (totalLen <= 0.001) return points;

            double segmentLen = totalLen / (targetCount - 1);
            double currentDist = 0;

            for (int i = 0; i < points.Count - 1 && result.Count < targetCount; i++)
            {
                Point p1 = points[i];
                Point p2 = points[i + 1];
                double d = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));

                while (currentDist + d >= segmentLen && result.Count < targetCount)
                {
                    double t = (segmentLen - currentDist) / d;
                    Point interp = new(p1.X + t * (p2.X - p1.X), p1.Y + t * (p2.Y - p1.Y));
                    result.Add(interp);
                    p1 = interp;
                    d = Math.Sqrt(Math.Pow(p2.X - p1.X, 2) + Math.Pow(p2.Y - p1.Y, 2));
                    currentDist = 0;
                }
                currentDist += d;
            }

            while (result.Count < targetCount) result.Add(points.Last());
            return result;
        }

        private static bool IsLetterV(List<Point> pts, double w, double h)
        {
            // Find lowest point (max Y in screen coords where Y increases downwards)
            int lowestIdx = 0;
            double maxY = pts[0].Y;
            for (int i = 1; i < pts.Count; i++)
            {
                if (pts[i].Y > maxY)
                {
                    maxY = pts[i].Y;
                    lowestIdx = i;
                }
            }

            // Lowest point should be in the middle third (indices 5..11 of 16)
            if (lowestIdx < 4 || lowestIdx > 12) return false;

            // Start should be top-left, end should be top-right
            bool startTopLeft = pts[0].X < pts[lowestIdx].X && pts[0].Y < pts[lowestIdx].Y - h * 0.4;
            bool endTopRight = pts[^1].X > pts[lowestIdx].X && pts[^1].Y < pts[lowestIdx].Y - h * 0.4;

            return startTopLeft && endTopRight;
        }

        private static bool IsLetterC(List<Point> pts, double w, double h)
        {
            // 'C' opens to the right: Start X is in right half, middle X is far left, End X is in right half
            Point start = pts[0];
            Point mid = pts[pts.Count / 2];
            Point end = pts[^1];

            bool curvesLeft = mid.X < start.X - w * 0.40 && mid.X < end.X - w * 0.40;
            bool topToBottom = start.Y < mid.Y && mid.Y < end.Y;

            return curvesLeft && topToBottom;
        }

        private static bool IsLetterS(List<Point> pts, double w, double h)
        {
            // 'S': starts top-right, curves left, goes right in middle, curves left at bottom
            Point p0 = pts[0];
            Point p4 = pts[4];
            Point p8 = pts[8];
            Point p12 = pts[12];
            Point p15 = pts[15];

            bool movingDown = p0.Y < p8.Y && p8.Y < p15.Y;
            bool sCurves = p4.X < p0.X && p8.X > p4.X && p12.X < p8.X;

            return movingDown && sCurves;
        }

        private static bool IsLetterZ(List<Point> pts, double w, double h)
        {
            // 'Z': Right horizontal, diagonal down-left, right horizontal
            Point p0 = pts[0];
            Point p5 = pts[5];
            Point p10 = pts[10];
            Point p15 = pts[15];

            bool topStrokeRight = p5.X > p0.X + w * 0.35 && Math.Abs(p5.Y - p0.Y) < h * 0.35;
            bool diagLeft = p10.X < p5.X - w * 0.35 && p10.Y > p5.Y + h * 0.35;
            bool bottomStrokeRight = p15.X > p10.X + w * 0.35;

            return topStrokeRight && diagLeft && bottomStrokeRight;
        }

        public void ExecuteGesture(TouchGestureType gesture)
        {
            var map = Mappings.FirstOrDefault(m => m.Gesture == gesture && m.Enabled);
            if (map == null || map.Action == GestureAction.None) return;

            OnGestureRecognized?.Invoke(gesture, map.Action);

            switch (map.Action)
            {
                case GestureAction.TakeScreenshot:
                    // Win + Shift + S (Snipping Tool overlay)
                    keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
                    keybd_event(0x53, 0, 0, UIntPtr.Zero); // 'S'
                    keybd_event(0x53, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    break;

                case GestureAction.SaveReplayClip:
                    // Win + Alt + G (Save instant game replay)
                    keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_MENU, 0, 0, UIntPtr.Zero);
                    keybd_event(0x47, 0, 0, UIntPtr.Zero); // 'G'
                    keybd_event(0x47, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_MENU, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    break;

                case GestureAction.ToggleMicMute:
                    SystemHardwareMonitor.ToggleMicrophoneMute();
                    break;

                case GestureAction.PlayPauseMedia:
                    keybd_event(VK_MEDIA_PLAY_PAUSE, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_MEDIA_PLAY_PAUSE, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    break;

                case GestureAction.NextTrack:
                    keybd_event(VK_MEDIA_NEXT_TRACK, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_MEDIA_NEXT_TRACK, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    break;

                case GestureAction.PreviousTrack:
                    keybd_event(VK_MEDIA_PREV_TRACK, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_MEDIA_PREV_TRACK, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    break;

                case GestureAction.OpenGameBar:
                    keybd_event(VK_LWIN, 0, 0, UIntPtr.Zero);
                    keybd_event(0x47, 0, 0, UIntPtr.Zero); // 'G'
                    keybd_event(0x47, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_LWIN, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    break;

                case GestureAction.LaunchCalculator:
                    try { Process.Start(new ProcessStartInfo { FileName = "calc.exe", UseShellExecute = true }); } catch { }
                    break;

                case GestureAction.DiscordMute:
                    // Ctrl + Shift + M
                    keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
                    keybd_event(0x4D, 0, 0, UIntPtr.Zero); // 'M'
                    keybd_event(0x4D, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    break;

                case GestureAction.DiscordDeafen:
                    // Ctrl + Shift + D
                    keybd_event(VK_CONTROL, 0, 0, UIntPtr.Zero);
                    keybd_event(VK_SHIFT, 0, 0, UIntPtr.Zero);
                    keybd_event(0x44, 0, 0, UIntPtr.Zero); // 'D'
                    keybd_event(0x44, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_SHIFT, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    keybd_event(VK_CONTROL, 0, KEYEVENTF_KEYUP, UIntPtr.Zero);
                    break;

                case GestureAction.ToggleRadialMenu:
                    OnToggleRadialMenuRequested?.Invoke();
                    break;
            }
        }

        public void ExecuteDirectAction(GestureAction action)
        {
            if (action == GestureAction.None) return;
            var temp = new GestureMapping { Gesture = TouchGestureType.None, Action = action, Enabled = true };
            OnGestureRecognized?.Invoke(TouchGestureType.None, action);
            switch (action)
            {
                case GestureAction.TakeScreenshot:
                case GestureAction.SaveReplayClip:
                case GestureAction.ToggleMicMute:
                case GestureAction.PlayPauseMedia:
                case GestureAction.NextTrack:
                case GestureAction.PreviousTrack:
                case GestureAction.OpenGameBar:
                case GestureAction.LaunchCalculator:
                case GestureAction.DiscordMute:
                case GestureAction.DiscordDeafen:
                case GestureAction.ToggleRadialMenu:
                    var prev = Mappings.FirstOrDefault(m => m.Gesture == TouchGestureType.TwoFingerTap);
                    if (prev != null)
                    {
                        var saved = prev.Action;
                        prev.Action = action;
                        ExecuteGesture(TouchGestureType.TwoFingerTap);
                        prev.Action = saved;
                    }
                    break;
            }
        }
    }
}
