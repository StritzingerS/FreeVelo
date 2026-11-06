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

        /// <summary>Virtual gears (Off / Grade / Erg, see FreeVelo.Core.Physics.VirtualGearMode).</summary>
        public static FreeVelo.Core.Physics.VirtualGearMode GearMode
        {
            get => (FreeVelo.Core.Physics.VirtualGearMode)PlayerPrefs.GetInt("gearMode", 0);
            set => SetInt("gearMode", (int)value);
        }

        /// <summary>
        /// Settings that go into a backup. Not included: the rider's server identity (riderId / riderSecret – the server
        /// registers the name again) and the Strava login (connect again on the new phone).
        /// </summary>
        public static readonly string[] BackupIntKeys = { "ftp", "powerSource", "gearMode" };
        public static readonly string[] BackupFloatKeys = { "weight", "difficulty" };
        public static readonly string[] BackupStringKeys = { "lang", "zones", "friends", "friendCode", "serverUrl" };

        /// <summary>After a backup was restored: forget cached values.</summary>
        public static void ReloadAfterRestore()
        {
            _language = null;
            _zones = null;
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
