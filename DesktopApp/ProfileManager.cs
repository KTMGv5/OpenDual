using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using OpenDualHub.Models;

namespace OpenDualHub
{
    public class GameProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "New Profile";
        public string ProcessNames { get; set; } = ""; // Comma-separated exe names (e.g. Cyberpunk2077.exe, cod.exe)
        public string Description { get; set; } = "";
        public bool IsBuiltIn { get; set; } = false;
        public ConfigDto Config { get; set; } = new();

        public bool MatchesProcess(string exeName)
        {
            if (string.IsNullOrWhiteSpace(ProcessNames) || string.IsNullOrWhiteSpace(exeName))
                return false;

            var targets = ProcessNames.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return targets.Any(t => t.Equals(exeName, StringComparison.OrdinalIgnoreCase));
        }

        public GameProfile Clone(string newName)
        {
            var json = JsonSerializer.Serialize(this);
            var copy = JsonSerializer.Deserialize<GameProfile>(json)!;
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = newName;
            copy.IsBuiltIn = false;
            return copy;
        }
    }

    public class ProfileManager
    {
        private static readonly string ProfilesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenDual", "Profiles");

        private readonly List<GameProfile> _profiles = new();

        public IReadOnlyList<GameProfile> Profiles => _profiles.AsReadOnly();

        public ProfileManager()
        {
            Directory.CreateDirectory(ProfilesDir);
            LoadAllProfiles();
        }

        public void LoadAllProfiles()
        {
            _profiles.Clear();

            // 1. Built-in Curated Presets
            _profiles.AddRange(GetBuiltInProfiles());

            // 2. Load User Profiles from AppData
            try
            {
                var files = Directory.GetFiles(ProfilesDir, "*.json");
                foreach (var file in files)
                {
                    try
                    {
                        var json = File.ReadAllText(file);
                        var prof = JsonSerializer.Deserialize<GameProfile>(json);
                        if (prof != null)
                        {
                            prof.IsBuiltIn = false;
                            // Replace if id collision, else add
                            _profiles.RemoveAll(p => p.Id == prof.Id);
                            _profiles.Add(prof);
                        }
                    }
                    catch { }
                }
            }
            catch { }
        }

        public void SaveProfile(GameProfile profile)
        {
            if (profile.IsBuiltIn) return; // Cannot edit built-in directly; users duplicate it

            var path = Path.Combine(ProfilesDir, $"{profile.Id}.json");
            var json = JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);

            var existing = _profiles.FirstOrDefault(p => p.Id == profile.Id);
            if (existing != null)
            {
                int idx = _profiles.IndexOf(existing);
                _profiles[idx] = profile;
            }
            else
            {
                _profiles.Add(profile);
            }
        }

        public void DeleteProfile(string id)
        {
            var target = _profiles.FirstOrDefault(p => p.Id == id && !p.IsBuiltIn);
            if (target != null)
            {
                _profiles.Remove(target);
                var path = Path.Combine(ProfilesDir, $"{id}.json");
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }

        public GameProfile? FindProfileForProcess(string exeName)
        {
            if (string.IsNullOrWhiteSpace(exeName)) return null;

            // Prioritize custom user profiles over built-ins
            return _profiles.FirstOrDefault(p => !p.IsBuiltIn && p.MatchesProcess(exeName))
                ?? _profiles.FirstOrDefault(p => p.IsBuiltIn && p.MatchesProcess(exeName));
        }

        public string ExportProfile(GameProfile profile)
        {
            return JsonSerializer.Serialize(profile, new JsonSerializerOptions { WriteIndented = true });
        }

        public GameProfile ImportProfile(string json)
        {
            var profile = JsonSerializer.Deserialize<GameProfile>(json);
            if (profile == null) throw new InvalidOperationException("Invalid profile data");

            profile.Id = Guid.NewGuid().ToString("N");
            profile.IsBuiltIn = false;
            SaveProfile(profile);
            return profile;
        }

        private static List<GameProfile> GetBuiltInProfiles()
        {
            return new List<GameProfile>
            {
                new GameProfile
                {
                    Id = "builtin-default",
                    Name = "Standard Balanced",
                    ProcessNames = "",
                    Description = "Standard controller experience with Rainbow RGB, standard game rumble haptics, and default resistance.",
                    IsBuiltIn = true,
                    Config = new ConfigDto
                    {
                        R = 0, G = 120, B = 255, Effect = 2, Speed = 1.0f,
                        Mouse = true, SyncMic = true,
                        LtMode = 0, RtMode = 0,
                        Haptic = true, HapticIntensity = 0.30f, HapticCutoff = 40, HapticStereo = true,
                        AudioTriggerKick = false, AudioTriggerThreshold = 0.20f,
                        SpeakerEnabled = false,
                        Gyro = false, GyroMode = 0, GyroOutputMode = 0, GyroSpace = 0, GyroSensX = 1.0f, GyroSensY = 1.0f,
                        IdleTimeout = 10, LowBatWarn = true
                    }
                },
                new GameProfile
                {
                    Id = "builtin-esports",
                    Name = "Esports Ultra 1000Hz",
                    ProcessNames = "VALORANT-Win64-Shipping.exe,cs2.exe,r5apex.exe,FortniteClient-Win64-Shipping.exe,Overwatch.exe",
                    Description = "1000Hz direct kernel HID polling rate, Hall-stabilizer filtering, hair triggers & zero drift.",
                    IsBuiltIn = true,
                    Config = new ConfigDto
                    {
                        R = 0, G = 245, B = 212, Effect = 0, Speed = 1.0f,
                        Mouse = true, SyncMic = true,
                        LtMode = 6, RtMode = 6, // TriggerStop (Hair trigger / instant snap)
                        Haptic = true, HapticIntensity = 1.2f, HapticCutoff = 120, HapticStereo = true,
                        AudioTriggerKick = true, AudioTriggerThreshold = 0.38f,
                        Gyro = false, GyroMode = 1, GyroOutputMode = 0, GyroSpace = 0, GyroSensX = 1.4f, GyroSensY = 1.4f, GyroDeadzone = 2, GyroSmooth = 0.15f,
                        IdleTimeout = 15, LowBatWarn = true
                    }
                },
                new GameProfile
                {
                    Id = "builtin-fps",
                    Name = "FPS Gyro Specialist",
                    ProcessNames = "Cyberpunk2077.exe,cod.exe,Destiny2.exe,Battlefield2042.exe,halo.exe",
                    Description = "6-axis motion aim on L2 ADS hold, spatial footstep radar audio, and trigger recoil kick.",
                    IsBuiltIn = true,
                    Config = new ConfigDto
                    {
                        R = 0, G = 210, B = 255, Effect = 0, Speed = 1.0f,
                        Mouse = true, SyncMic = true,
                        LtMode = 1, RtMode = 2, // L2: Rigid Aim Wall, R2: Recoil Pulse
                        Haptic = true, HapticIntensity = 1.3f, HapticCutoff = 110, HapticStereo = true,
                        AudioTriggerKick = true, AudioTriggerThreshold = 0.35f,
                        Gyro = true, GyroMode = 1, GyroOutputMode = 0, GyroSpace = 0, GyroSensX = 1.3f, GyroSensY = 1.3f, GyroDeadzone = 3, GyroSmooth = 0.20f,
                        IdleTimeout = 15, LowBatWarn = true
                    }
                },
                new GameProfile
                {
                    Id = "builtin-racing",
                    Name = "Racing Force Feedback",
                    ProcessNames = "ForzaHorizon5.exe,F1_24.exe,F1_23.exe,acc.exe,iRacingSim64DX11.exe,assettocorsa.exe,DirtRally2.exe,ForzaMotorsport.exe",
                    Description = "Progressive throttle pedal tension, ABS brake pulse vibration, and deep motor engine rumble.",
                    IsBuiltIn = true,
                    Config = new ConfigDto
                    {
                        R = 255, G = 25, B = 0, Effect = 0, Speed = 1.0f,
                        Mouse = false, SyncMic = true,
                        LtMode = 2, RtMode = 5, // L2: ABS Pulse, R2: Bow Progressive Tension
                        Haptic = true, HapticIntensity = 1.5f, HapticCutoff = 80, HapticStereo = true,
                        AudioTriggerKick = false, AudioTriggerThreshold = 0.45f,
                        Gyro = false, GyroMode = 0, GyroSensX = 1.0f, GyroSensY = 1.0f,
                        IdleTimeout = 20, LowBatWarn = true
                    }
                },
                new GameProfile
                {
                    Id = "builtin-action-rpg",
                    Name = "Action / RPG Bow & Combat",
                    ProcessNames = "HorizonForbiddenWest.exe,HorizonZeroDawn.exe,EldenRing.exe,GoW.exe,Witcher3.exe,StarWarsJediSurvivor.exe,GhostOfTsushima.exe",
                    Description = "Realistic bow drawstring resistance curve on L2, heavy melee attack recoil on R2, and immersive haptics.",
                    IsBuiltIn = true,
                    Config = new ConfigDto
                    {
                        R = 0, G = 255, B = 130, Effect = 1, Speed = 0.8f, // Breathing Emerald
                        Mouse = true, SyncMic = true,
                        LtMode = 5, RtMode = 1, // L2: Bow, R2: Rigid Resistance
                        Haptic = true, HapticIntensity = 1.15f, HapticCutoff = 110, HapticStereo = true,
                        AudioTriggerKick = true, AudioTriggerThreshold = 0.42f,
                        Gyro = true, GyroMode = 1, GyroOutputMode = 0, GyroSpace = 0, GyroSensX = 1.1f, GyroSensY = 1.1f, GyroDeadzone = 3, GyroSmooth = 0.20f,
                        IdleTimeout = 10, LowBatWarn = true
                    }
                },
                new GameProfile
                {
                    Id = "builtin-flight-sim",
                    Name = "Flight & Space Simulator",
                    ProcessNames = "FlightSimulator.exe,StarCitizen.exe,EliteDangerous64.exe,X-Plane.exe,DCS.exe",
                    Description = "Always-on 6-axis gyro motion yoke for intuitive roll and pitch control in flight simulations.",
                    IsBuiltIn = true,
                    Config = new ConfigDto
                    {
                        R = 150, G = 40, B = 255, Effect = 2, Speed = 0.5f, // Rainbow / Cosmic
                        Mouse = false, SyncMic = true,
                        LtMode = 1, RtMode = 1, // Rigid flight throttle resistance
                        Haptic = true, HapticIntensity = 1.0f, HapticCutoff = 90, HapticStereo = true,
                        AudioTriggerKick = false, AudioTriggerThreshold = 0.45f,
                        Gyro = true, GyroMode = 0, GyroOutputMode = 1, GyroSpace = 0, GyroSensX = 1.0f, GyroSensY = 1.0f, GyroDeadzone = 2, GyroSmooth = 0.25f,
                        IdleTimeout = 30, LowBatWarn = true
                    }
                }
            };
        }
    }
}
