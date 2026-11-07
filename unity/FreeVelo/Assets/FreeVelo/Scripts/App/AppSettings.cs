using System.Collections.Generic;
using System.Linq;
using FreeVelo.Devices.Session;
using UnityEngine;

namespace FreeVelo.App
{
    /// <summary>User settings, stored in PlayerPrefs (local to the device).</summary>
    public static class AppSettings
    {
        public static int FtpW
        {
            get => PlayerPrefs.GetInt("ftp", 200);
            set => SetInt("ftp", Mathf.Clamp(value, 50, 600));
        }

        public static float RiderWeightKg
        {
            get => PlayerPrefs.GetFloat("weight", 75f);
            set => SetFloat("weight", Mathf.Clamp(value, 30f, 200f));
        }

        /// <summary>0–1: how steep climbs feel on the trainer (50 % is a common default).</summary>
        public static float TrainerDifficulty
        {
            get => PlayerPrefs.GetFloat("difficulty", 0.5f);
            set => SetFloat("difficulty", Mathf.Clamp01(value));
        }

        // Settings the ride logic reads – also in the background, where PlayerPrefs can't be used: they are cached
        // (loaded by Preload on the main thread at start and after a restore).
        private static int _gearMode = -1, _gearCount = -1, _ergStep = -1;
        private static Dictionary<FreeVelo.Devices.Zwift.ZwiftButton, int> _buttonMap;

        /// <summary>Loads the cached settings (main thread).</summary>
        public static void Preload()
        {
            _gearMode = PlayerPrefs.GetInt("gearMode", 0);
            _gearCount = PlayerPrefs.GetInt("gearCount", FreeVelo.Core.Physics.VirtualGears.DefaultCount);
            _ergStep = PlayerPrefs.GetInt("ergStep", 1);
            _buttonMap = ParseButtonMap(PlayerPrefs.GetString("zwiftMap", string.Empty));
        }

        /// <summary>
        /// Virtual gears on/off. How they reach the trainer is chosen automatically: a trainer with built-in virtual
        /// shifting gets the gear itself, others a matching grade. (Stored as "gearMode": 0 = off; the former
        /// "grade" 1 and "ERG" 2 both mean on.)
        /// </summary>
        public static bool VirtualGears
        {
            get => _gearMode > 0;
            set
            {
                _gearMode = value ? 1 : 0;
                SetInt("gearMode", _gearMode);
            }
        }

        public const int MinGears = 12, MaxGears = 30;

        /// <summary>Number of virtual gears (12–30).</summary>
        public static int GearCount
        {
            get => _gearCount > 0 ? _gearCount : FreeVelo.Core.Physics.VirtualGears.DefaultCount;
            set
            {
                _gearCount = Mathf.Clamp(value, MinGears, MaxGears);
                SetInt("gearCount", _gearCount);
            }
        }

        /// <summary>ERG blocks: intensity change per press of + / − (1–5 %).</summary>
        public static int ErgStepPercent
        {
            get => _ergStep > 0 ? _ergStep : 1;
            set
            {
                _ergStep = Mathf.Clamp(value, 1, 5);
                SetInt("ergStep", _ergStep);
            }
        }

        // ------------------------------------------------------------------ Zwift controller buttons

        /// <summary>The buttons of a Click V2 (both units) that can be assigned; others follow these.</summary>
        public static readonly FreeVelo.Devices.Zwift.ZwiftButton[] MappableButtons =
        {
            FreeVelo.Devices.Zwift.ZwiftButton.ShiftUpRight, FreeVelo.Devices.Zwift.ZwiftButton.ShiftUpLeft,
            FreeVelo.Devices.Zwift.ZwiftButton.A, FreeVelo.Devices.Zwift.ZwiftButton.B,
            FreeVelo.Devices.Zwift.ZwiftButton.Y, FreeVelo.Devices.Zwift.ZwiftButton.Z,
            FreeVelo.Devices.Zwift.ZwiftButton.Up, FreeVelo.Devices.Zwift.ZwiftButton.Down,
            FreeVelo.Devices.Zwift.ZwiftButton.Left, FreeVelo.Devices.Zwift.ZwiftButton.Right,
        };

        /// <summary>Default: + / A / ↑ harder, − / B / ↓ easier, the rest unused.</summary>
        public static int DefaultAction(FreeVelo.Devices.Zwift.ZwiftButton b) => b switch
        {
            FreeVelo.Devices.Zwift.ZwiftButton.ShiftUpRight or FreeVelo.Devices.Zwift.ZwiftButton.A or FreeVelo.Devices.Zwift.ZwiftButton.Up => +1,
            FreeVelo.Devices.Zwift.ZwiftButton.ShiftUpLeft or FreeVelo.Devices.Zwift.ZwiftButton.B or FreeVelo.Devices.Zwift.ZwiftButton.Down => -1,
            _ => 0,
        };

        /// <summary>What a button does: +1 harder, −1 easier, 0 nothing. Thread-safe (cached).</summary>
        public static int ButtonAction(FreeVelo.Devices.Zwift.ZwiftButton button)
        {
            // Buttons of other controllers follow the Click V2 buttons they correspond to.
            var key = button switch
            {
                FreeVelo.Devices.Zwift.ZwiftButton.ClickPlus or FreeVelo.Devices.Zwift.ZwiftButton.ShiftDownRight => FreeVelo.Devices.Zwift.ZwiftButton.ShiftUpRight,
                FreeVelo.Devices.Zwift.ZwiftButton.ClickMinus or FreeVelo.Devices.Zwift.ZwiftButton.ShiftDownLeft => FreeVelo.Devices.Zwift.ZwiftButton.ShiftUpLeft,
                _ => button,
            };
            var map = _buttonMap;
            return map != null && map.TryGetValue(key, out int action) ? action : DefaultAction(key);
        }

        public static void SetButtonAction(FreeVelo.Devices.Zwift.ZwiftButton button, int action)
        {
            var map = new Dictionary<FreeVelo.Devices.Zwift.ZwiftButton, int>(_buttonMap ?? new Dictionary<FreeVelo.Devices.Zwift.ZwiftButton, int>())
            {
                [button] = Mathf.Clamp(action, -1, 1),
            };
            _buttonMap = map;
            PlayerPrefs.SetString("zwiftMap", string.Join(";", map.Select(e => $"{e.Key}={e.Value}")));
            PlayerPrefs.Save();
        }

        public static void ResetButtonMap()
        {
            _buttonMap = new Dictionary<FreeVelo.Devices.Zwift.ZwiftButton, int>();
            PlayerPrefs.DeleteKey("zwiftMap");
            PlayerPrefs.Save();
        }

        private static Dictionary<FreeVelo.Devices.Zwift.ZwiftButton, int> ParseButtonMap(string text)
        {
            var map = new Dictionary<FreeVelo.Devices.Zwift.ZwiftButton, int>();
            foreach (var part in text.Split(';'))
            {
                var kv = part.Split('=');
                if (kv.Length == 2 && System.Enum.TryParse(kv[0], out FreeVelo.Devices.Zwift.ZwiftButton b) && int.TryParse(kv[1], out int a)) map[b] = Mathf.Clamp(a, -1, 1);
            }

            return map;
        }

        // ------------------------------------------------------------------ heart rate zones

        public static int MaxHr
        {
            get => PlayerPrefs.GetInt("maxHr", FreeVelo.Core.Training.HeartRateZoneModel.DefaultMaxHr);
            set => SetInt("maxHr", Mathf.Clamp(value, 120, 230));
        }

        /// <summary>Lactate threshold heart rate.</summary>
        public static int Lthr
        {
            get => PlayerPrefs.GetInt("lthr", FreeVelo.Core.Training.HeartRateZoneModel.DefaultLthr);
            set => SetInt("lthr", Mathf.Clamp(value, 100, 220));
        }

        public static FreeVelo.Core.Training.HeartRateZoneBasis HrZoneBasis
        {
            get => (FreeVelo.Core.Training.HeartRateZoneBasis)PlayerPrefs.GetInt("hrBasis", 0);
            set => SetInt("hrBasis", (int)value);
        }

        public static FreeVelo.Core.Training.HeartRateZoneModel HrZones => FreeVelo.Core.Training.HeartRateZoneModel.Create(HrZoneBasis, MaxHr, Lthr);

        /// <summary>
        /// Settings that go into a backup. Not included: the rider's server identity (riderId / riderSecret – the server
        /// registers the name again) and the Strava login (connect again on the new phone).
        /// </summary>
        public static readonly string[] BackupIntKeys = { "ftp", "powerSource", "gearMode", "gearCount", "ergStep", "maxHr", "lthr", "hrBasis" };
        public static readonly string[] BackupFloatKeys = { "weight", "difficulty" };
        public static readonly string[] BackupStringKeys = { "lang", "zones", "friends", "friendCode", "serverUrl", "zwiftMap" };

        /// <summary>After a backup was restored: forget cached values.</summary>
        public static void ReloadAfterRestore()
        {
            _language = null;
            _zones = null;
            Preload();
        }

        public static PowerSource PowerSource
        {
            get => (PowerSource)PlayerPrefs.GetInt("powerSource", (int)PowerSource.Trainer);
            set => SetInt("powerSource", (int)value);
        }

        /// <summary>"de" or "en".</summary>
        public static string Language
        {
            // Cached: also read by the ride logic when it runs in the background (off the Unity main thread).
            get => _language ??= PlayerPrefs.GetString("lang", Application.systemLanguage == SystemLanguage.German ? "de" : "en");
            set
            {
                _language = value;
                PlayerPrefs.SetString("lang", value);
                PlayerPrefs.Save();
            }
        }

        private static string _language;

        private static FreeVelo.Core.Training.PowerZoneModel _zones;

        /// <summary>The rider's power zones (Coggan by default; bounds in % FTP or watts).</summary>
        public static FreeVelo.Core.Training.PowerZoneModel Zones => _zones ??= FreeVelo.Core.Training.PowerZoneModel.Parse(PlayerPrefs.GetString("zones", string.Empty));

        public static void SaveZones()
        {
            PlayerPrefs.SetString("zones", Zones.Serialize());
            PlayerPrefs.Save();
        }

        public static void ResetZones()
        {
            PlayerPrefs.DeleteKey("zones");
            PlayerPrefs.Save();
            _zones = null;
        }

        // ------------------------------------------------------------------ group rides

        /// <summary>The public FreeVelo server (Render); used unless the rider enters their own.</summary>
        public const string DefaultServerUrl = "https://freevelo.onrender.com";

        /// <summary>
        /// Address of the FreeVelo server: <see cref="DefaultServerUrl"/>, or an own one such as
        /// https://freevelo.duckdns.org or http://192.168.1.20:8080. Setting it empty (or to the default) goes back
        /// to the default.
        /// </summary>
        public static string ServerUrl
        {
            get
            {
                string own = PlayerPrefs.GetString("serverUrl", string.Empty);
                return own.Length > 0 ? own : DefaultServerUrl;
            }

            set
            {
                string url = (value ?? string.Empty).Trim().TrimEnd('/');
                if (url == DefaultServerUrl) url = string.Empty;
                PlayerPrefs.SetString("serverUrl", url);
                PlayerPrefs.Save();
            }
        }

        /// <summary>True when the rider uses an own server instead of the default.</summary>
        public static bool HasOwnServer => PlayerPrefs.GetString("serverUrl", string.Empty).Length > 0;

        /// <summary>
        /// The rider's identity on the server (id + secret from the first start; no email, no password).
        /// Null until a display name was entered.
        /// </summary>
        public static FreeVelo.Shared.Api.RiderCredentials Rider
        {
            get
            {
                string id = PlayerPrefs.GetString("riderId", string.Empty);
                if (id.Length == 0) return null;
                return new FreeVelo.Shared.Api.RiderCredentials
                {
                    riderId = id,
                    secret = PlayerPrefs.GetString("riderSecret", string.Empty),
                    displayName = PlayerPrefs.GetString("riderName", string.Empty),
                };
            }

            set
            {
                if (value == null)
                {
                    PlayerPrefs.DeleteKey("riderId");
                    PlayerPrefs.DeleteKey("riderSecret");
                    PlayerPrefs.DeleteKey("riderName");
                }
                else
                {
                    PlayerPrefs.SetString("riderId", value.riderId);
                    PlayerPrefs.SetString("riderSecret", value.secret);
                    PlayerPrefs.SetString("riderName", value.displayName);
                }

                PlayerPrefs.Save();
            }
        }

        // ------------------------------------------------------------------ friends (kept on this phone only)

        /// <summary>This phone's secret friend code (made once; friends need it to see private rides).</summary>
        public static string FriendCode
        {
            get
            {
                string code = PlayerPrefs.GetString("friendCode", string.Empty);
                if (code.Length == 0)
                {
                    code = FreeVelo.Shared.Api.FreeVeloApi.NewFriendCode();
                    PlayerPrefs.SetString("friendCode", code);
                    PlayerPrefs.Save();
                }

                return code;
            }
        }

        /// <summary>A new friend code: everyone who had the old one no longer sees this rider's private rides.</summary>
        public static void ResetFriendCode()
        {
            PlayerPrefs.DeleteKey("friendCode");
            PlayerPrefs.Save();
        }

        [System.Serializable]
        private class FriendList
        {
            public FreeVelo.Shared.Api.FriendInfo[] friends = new FreeVelo.Shared.Api.FriendInfo[0];
        }

        public static List<FreeVelo.Shared.Api.FriendInfo> Friends
        {
            get
            {
                string json = PlayerPrefs.GetString("friends", string.Empty);
                if (json.Length == 0) return new List<FreeVelo.Shared.Api.FriendInfo>();
                var list = JsonUtility.FromJson<FriendList>(json);
                return list?.friends != null ? list.friends.ToList() : new List<FreeVelo.Shared.Api.FriendInfo>();
            }
        }

        /// <summary>Adds a friend (or updates the name of one with the same code).</summary>
        public static void AddFriend(FreeVelo.Shared.Api.FriendInfo friend)
        {
            if (friend == null || string.IsNullOrEmpty(friend.code)) return;
            var list = Friends.Where(f => f.code != friend.code).ToList();
            list.Add(friend);
            SaveFriends(list);
        }

        public static void RemoveFriend(string code) => SaveFriends(Friends.Where(f => f.code != code).ToList());

        private static void SaveFriends(List<FreeVelo.Shared.Api.FriendInfo> list)
        {
            PlayerPrefs.SetString("friends", JsonUtility.ToJson(new FriendList { friends = list.OrderBy(f => f.name).ToArray() }));
            PlayerPrefs.Save();
        }

        /// <summary>Devices that are connected automatically when found.</summary>
        public static IReadOnlyCollection<string> RememberedDevices =>
            PlayerPrefs.GetString("devices", string.Empty).Split(',').Where(s => s.Length > 0).ToList();

        public static void RememberDevice(string address, bool remember)
        {
            var set = new HashSet<string>(RememberedDevices);
            if (remember) set.Add(address);
            else set.Remove(address);
            PlayerPrefs.SetString("devices", string.Join(",", set));
            PlayerPrefs.Save();
        }

        private static void SetInt(string key, int value)
        {
            PlayerPrefs.SetInt(key, value);
            PlayerPrefs.Save();
        }

        private static void SetFloat(string key, float value)
        {
            PlayerPrefs.SetFloat(key, value);
            PlayerPrefs.Save();
        }
    }
}
