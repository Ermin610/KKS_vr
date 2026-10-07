using System;
using System.Runtime.InteropServices;

namespace KKCharaStudioVR
{
    internal static class KeyboradSimulatorUtil
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

        public const byte VK_PRIOR = 0x21; // Page Up
        public const byte VK_NEXT = 0x22;  // Page Down
        public const byte VK_END = 0x23;   // END key code
        public const byte VK_HOME = 0x24;  // HOME key code

        private const uint KEYEVENTF_KEYUP = 0x0002;
        private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

        public static void PressKey(byte vkCode, string description)
        {
            try
            {
                keybd_event(vkCode, 0, KEYEVENTF_EXTENDEDKEY, UIntPtr.Zero);
                keybd_event(vkCode, 0, KEYEVENTF_EXTENDEDKEY | KEYEVENTF_KEYUP, UIntPtr.Zero);
                VRGIN.Core.VRLog.Info("KeyboardSimulator: Successfully simulated " + description + " (" + vkCode + ")");
            }
            catch (Exception ex)
            {
                VRGIN.Core.VRLog.Error("KeyboardSimulator: PressKey " + description + " failed: " + ex.Message);
            }
        }

        public static void PressEndKey()
        {
            PressKey(VK_END, "End key (ReShade Toggle)");
        }

        public static void PressPageDown()
        {
            PressKey(VK_NEXT, "PageDown key (ReShade Next Preset)");
        }

        public static void PressPageUp()
        {
            PressKey(VK_PRIOR, "PageUp key (ReShade Prev Preset)");
        }

        public static void PressHomeKey()
        {
            PressKey(VK_HOME, "Home key (ReShade Overlay)");
        }
    }
}
