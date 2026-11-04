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
            get => PlayerPrefs.GetString("lang", Application.systemLanguage == SystemLanguage.German ? "de" : "en");
            set
            {
                PlayerPrefs.SetString("lang", value);
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
