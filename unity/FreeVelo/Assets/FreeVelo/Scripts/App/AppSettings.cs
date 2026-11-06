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

        /// <summary>Address of the FreeVelo server, e.g. https://freevelo.duckdns.org or http://192.168.1.20:8080.</summary>
        public static string ServerUrl
        {
            get => PlayerPrefs.GetString("serverUrl", string.Empty);
            set
            {
                PlayerPrefs.SetString("serverUrl", (value ?? string.Empty).Trim().TrimEnd('/'));
                PlayerPrefs.Save();
            }
        }

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
