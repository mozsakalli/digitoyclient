// Unity SystemInfo/Application/DevicePlatformHelper yerine tek shim. Motorun platform API'si yok; .NET OperatingSystem
// ile çözülür. Editör ayrımı yok (masaüstü = Windows/Macos). AppVersion bootstrap'ta PlayerSettings'ten yazılır.
using System;
using M;

namespace Digitoy
{
    public static class Platform
    {
        public static string AppVersion = "0.0.0";

        public static DevicePlatform Current
        {
            get
            {
                if (OperatingSystem.IsAndroid()) return DevicePlatform.Android;
                if (OperatingSystem.IsIOS()) return DevicePlatform.Ios;
                if (OperatingSystem.IsBrowser()) return DevicePlatform.Web;
                if (OperatingSystem.IsWindows()) return DevicePlatform.Windows;
                if (OperatingSystem.IsMacOS()) return DevicePlatform.Macos;
                if (OperatingSystem.IsLinux()) return DevicePlatform.Linux;
                return DevicePlatform.Unknown;
            }
        }

        public static string OS => Current.ToString(); // corelib'de OSVersion yok; platform adı yeter

        // Faz 5: native köprüden gerçek model; şimdilik platform adı.
        public static string DeviceModel => Current.ToString();
    }
}
