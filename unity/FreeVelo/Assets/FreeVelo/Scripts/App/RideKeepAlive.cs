using System;
using UnityEngine;
#if UNITY_ANDROID
using UnityEngine.Android;
#endif

namespace FreeVelo.App
{
    /// <summary>
    /// Keeps a ride alive on Android: starts the RideService foreground service (ongoing notification with ride
    /// clock, Bluetooth and CPU stay awake) and keeps the screen on. No-op in the editor.
    /// </summary>
    public static class RideKeepAlive
    {
        private const string ServiceClass = "io.github.stritzingers.freevelo.ride.RideService";
        private static long _startedAtMillis;
        private static string _title;
        private static string _lastText;
        private static bool _missingPlugin;

        public static void Start(string title, string text)
        {
            Screen.sleepTimeout = SleepTimeout.NeverSleep;
            _title = title;
            _lastText = text;
            _startedAtMillis = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            RequestNotificationPermission();
            Call("start", _title, text, _startedAtMillis);
        }

        /// <summary>Updates the notification text (only when it changed).</summary>
        public static void Update(string text)
        {
            if (_title == null || text == _lastText) return;
            _lastText = text;
            Call("update", _title, text, _startedAtMillis);
        }

        public static void Stop()
        {
            Screen.sleepTimeout = SleepTimeout.SystemSetting;
            if (_title == null) return;
            _title = null;
            Call("stop");
        }

        /// <summary>Alert with sound and vibration (e.g. workout finished while the app is in the background).</summary>
        public static void Alert(string title, string text) => Call("alert", optional: true, title, text);

        public static void ClearAlert() => Call("clearAlert", optional: true);

        private static void Call(string method, params object[] args) => Call(method, false, args);

        /// <param name="optional">Missing in an older plugin: skip it, but keep using the service.</param>
        private static void Call(string method, bool optional, params object[] args)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (_missingPlugin) return;
            try
            {
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var service = new AndroidJavaClass(ServiceClass))
                {
                    var all = new object[args.Length + 1];
                    all[0] = activity;
                    Array.Copy(args, 0, all, 1, args.Length);
                    service.CallStatic(method, all);
                }
            }
            catch (Exception e)
            {
                // An outdated freevelo-ble.aar has no RideService: ride without it, report once.
                _missingPlugin = !optional && (e.Message.Contains("ClassNotFound") || e.Message.Contains("NoSuchMethod"));
                Debug.LogWarning("FreeVelo: ride service unavailable – " + e.Message);
            }
#endif
        }

        private static void RequestNotificationPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            const string permission = "android.permission.POST_NOTIFICATIONS";
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
            {
                if (version.GetStatic<int>("SDK_INT") < 33) return;
            }

            if (!Permission.HasUserAuthorizedPermission(permission) && PlayerPrefs.GetInt("askedNotifications", 0) == 0)
            {
                PlayerPrefs.SetInt("askedNotifications", 1);
                PlayerPrefs.Save();
                Permission.RequestUserPermission(permission);
            }
#endif
        }
    }
}
