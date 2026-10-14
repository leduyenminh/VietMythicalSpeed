using UnityEngine;

namespace VMS
{
    /// <summary>Short vibration ticks. Handheld.Vibrate is far too long for per-note feedback, so Android uses VibrationEffect.</summary>
    public static class Haptics
    {
#if UNITY_ANDROID && !UNITY_EDITOR
        static AndroidJavaObject _vibrator;
        static bool _initialised;
        static int _sdk;

        static void Init()
        {
            if (_initialised) return;
            _initialised = true;
            try
            {
                using (var version = new AndroidJavaClass("android.os.Build$VERSION")) _sdk = version.GetStatic<int>("SDK_INT");
                using (var player = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = player.GetStatic<AndroidJavaObject>("currentActivity"))
                    _vibrator = activity.Call<AndroidJavaObject>("getSystemService", "vibrator");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning("[Haptics] unavailable: " + e.Message);
                _vibrator = null;
            }
        }
#endif

        public static void Tick(int milliseconds = 12, int amplitude = 80)
        {
            if (GameApp.Instance == null || !GameApp.Instance.Settings.Haptics) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            Init();
            if (_vibrator == null) return;
            try
            {
                if (_sdk >= 26)
                {
                    using (var effectClass = new AndroidJavaClass("android.os.VibrationEffect"))
                    using (var effect = effectClass.CallStatic<AndroidJavaObject>("createOneShot", (long)milliseconds, Mathf.Clamp(amplitude, 1, 255)))
                        _vibrator.Call("vibrate", effect);
                }
                else
                {
                    _vibrator.Call("vibrate", (long)milliseconds);
                }
            }
            catch (System.Exception)
            {
                _vibrator = null;
            }
#endif
        }
    }
}
