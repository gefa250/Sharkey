using System;
using System.Globalization;
using System.Windows.Input;

namespace GlobalTranslator
{
    internal sealed class HotkeyGesture
    {
        public uint Modifiers;
        public uint VirtualKey;
        public string Display = "";

        public static bool TryParse(
            string value,
            out HotkeyGesture gesture,
            out string error)
        {
            gesture = null;
            error = "";
            string[] parts = (value ?? "").Split(
                new[] { '+' },
                StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
            {
                error = "快捷键不能为空。";
                return false;
            }

            uint modifiers = 0;
            string keyName = "";
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Equals(
                    "Ctrl", StringComparison.OrdinalIgnoreCase))
                    modifiers |= NativeMethods.MOD_CONTROL;
                else if (part.Equals(
                    "Alt", StringComparison.OrdinalIgnoreCase))
                    modifiers |= NativeMethods.MOD_ALT;
                else if (part.Equals(
                    "Shift", StringComparison.OrdinalIgnoreCase))
                    modifiers |= NativeMethods.MOD_SHIFT;
                else if (keyName.Length == 0)
                    keyName = part;
                else
                {
                    error = "快捷键只能包含一个主按键。";
                    return false;
                }
            }

            uint virtualKey;
            bool functionKey;
            if (!TryParseKey(keyName, out virtualKey, out functionKey))
            {
                error = "请使用 F1–F24，或带 Ctrl / Alt / Shift 的字母、数字组合。";
                return false;
            }
            if (modifiers == 0 && !functionKey)
            {
                error = "字母或数字必须搭配 Ctrl、Alt 或 Shift。";
                return false;
            }
            gesture = Create(modifiers, virtualKey, KeyDisplay(virtualKey));
            return true;
        }

        public static bool TryFromKeyEvent(
            Key key,
            ModifierKeys inputModifiers,
            out HotkeyGesture gesture,
            out string error)
        {
            gesture = null;
            error = "";
            if ((inputModifiers & ModifierKeys.Windows) != 0)
            {
                error = "为避免覆盖系统快捷键，不支持 Windows 键。";
                return false;
            }
            if (key == Key.LeftCtrl || key == Key.RightCtrl ||
                key == Key.LeftAlt || key == Key.RightAlt ||
                key == Key.LeftShift || key == Key.RightShift ||
                key == Key.LWin || key == Key.RWin)
            {
                error = "请继续按下一个字母、数字或功能键。";
                return false;
            }

            bool functionKey =
                key >= Key.F1 && key <= Key.F24;
            bool letter = key >= Key.A && key <= Key.Z;
            bool digit = key >= Key.D0 && key <= Key.D9;
            bool numberPad =
                key >= Key.NumPad0 && key <= Key.NumPad9;
            if (!functionKey && !letter && !digit && !numberPad)
            {
                error = "请使用 F1–F24，或带修饰键的字母、数字。";
                return false;
            }

            uint modifiers = 0;
            if ((inputModifiers & ModifierKeys.Control) != 0)
                modifiers |= NativeMethods.MOD_CONTROL;
            if ((inputModifiers & ModifierKeys.Alt) != 0)
                modifiers |= NativeMethods.MOD_ALT;
            if ((inputModifiers & ModifierKeys.Shift) != 0)
                modifiers |= NativeMethods.MOD_SHIFT;
            if (modifiers == 0 && !functionKey)
            {
                error = "字母或数字必须搭配 Ctrl、Alt 或 Shift。";
                return false;
            }

            uint virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            gesture = Create(
                modifiers,
                virtualKey,
                KeyDisplay(virtualKey));
            return true;
        }

        public bool SameAs(HotkeyGesture other)
        {
            return other != null &&
                   Modifiers == other.Modifiers &&
                   VirtualKey == other.VirtualKey;
        }

        private static HotkeyGesture Create(
            uint modifiers, uint virtualKey, string keyName)
        {
            string display = "";
            if ((modifiers & NativeMethods.MOD_CONTROL) != 0)
                display += "Ctrl+";
            if ((modifiers & NativeMethods.MOD_ALT) != 0)
                display += "Alt+";
            if ((modifiers & NativeMethods.MOD_SHIFT) != 0)
                display += "Shift+";
            display += keyName;
            return new HotkeyGesture
            {
                Modifiers = modifiers,
                VirtualKey = virtualKey,
                Display = display
            };
        }

        private static bool TryParseKey(
            string value, out uint virtualKey, out bool functionKey)
        {
            virtualKey = 0;
            functionKey = false;
            if (string.IsNullOrWhiteSpace(value)) return false;
            string key = value.Trim().ToUpperInvariant();
            if (key.Length >= 2 && key[0] == 'F')
            {
                int number;
                if (int.TryParse(
                    key.Substring(1),
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out number) &&
                    number >= 1 && number <= 24)
                {
                    virtualKey = (uint)(0x70 + number - 1);
                    functionKey = true;
                    return true;
                }
            }
            if (key.Length == 1 &&
                ((key[0] >= 'A' && key[0] <= 'Z') ||
                 (key[0] >= '0' && key[0] <= '9')))
            {
                virtualKey = key[0];
                return true;
            }
            if (key.StartsWith("NUM", StringComparison.Ordinal) &&
                key.Length == 4 &&
                key[3] >= '0' && key[3] <= '9')
            {
                virtualKey = (uint)(0x60 + key[3] - '0');
                return true;
            }
            return false;
        }

        private static string KeyDisplay(uint virtualKey)
        {
            if (virtualKey >= 0x70 && virtualKey <= 0x87)
                return "F" + (virtualKey - 0x70 + 1);
            if (virtualKey >= 0x60 && virtualKey <= 0x69)
                return "Num" + (virtualKey - 0x60);
            return ((char)virtualKey).ToString();
        }
    }
}
