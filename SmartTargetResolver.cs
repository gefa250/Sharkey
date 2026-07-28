using System;

namespace GlobalTranslator
{
    internal static class SmartTargetResolver
    {
        public static string Resolve(
            string text,
            string mode,
            string fixedTarget)
        {
            if (string.Equals(
                mode,
                "Fixed",
                StringComparison.OrdinalIgnoreCase))
                return NormalizeFixedTarget(fixedTarget);
            return ResolveSmart(text);
        }

        public static string ResolveSmart(string text)
        {
            int han = 0;
            int latin = 0;
            int kana = 0;
            int hangul = 0;
            foreach (char value in text ?? "")
            {
                int code = value;
                if (IsKana(code))
                    kana++;
                else if (IsHangul(code))
                    hangul++;
                else if (IsHan(code))
                    han++;
                else if (IsLatinLetter(code))
                    latin++;
            }

            if (kana > 0 || hangul > 0)
                return "zh-Hans";
            if (han > 0 &&
                (latin == 0 ||
                 han >= 2 ||
                 (double)han / (han + latin) >= 0.30))
                return "en";
            return "zh-Hans";
        }

        public static bool ShouldPreserveContent(
            string text,
            string mode)
        {
            if (string.Equals(
                mode,
                "Fixed",
                StringComparison.OrdinalIgnoreCase))
                return false;
            string value = (text ?? "").Trim();
            if (value.Length == 0) return true;
            Uri uri;
            if (Uri.TryCreate(
                    value, UriKind.Absolute, out uri) &&
                (uri.Scheme == Uri.UriSchemeHttp ||
                 uri.Scheme == Uri.UriSchemeHttps))
                return true;

            bool hasLetter = false;
            bool hasDigit = false;
            bool hasCodePunctuation = false;
            foreach (char character in value)
            {
                if (char.IsLetter(character))
                    hasLetter = true;
                if (char.IsDigit(character))
                    hasDigit = true;
                if ("{}[]();=<>".IndexOf(character) >= 0)
                    hasCodePunctuation = true;
            }
            if (!hasLetter && hasDigit)
                return true;
            if (hasCodePunctuation &&
                (value.IndexOf(';') >= 0 ||
                 value.IndexOf('{') >= 0 ||
                 value.IndexOf('}') >= 0 ||
                 value.IndexOf('=') >= 0 ||
                 value.StartsWith(
                     "if ",
                     StringComparison.OrdinalIgnoreCase) ||
                 value.StartsWith(
                     "if(",
                     StringComparison.OrdinalIgnoreCase)))
                return true;
            return false;
        }

        private static string NormalizeFixedTarget(
            string target)
        {
            switch (target)
            {
                case "zh-Hans":
                case "zh-Hant":
                case "en":
                case "ja":
                case "ko":
                case "fr":
                case "de":
                case "es":
                    return target;
                default:
                    return "zh-Hans";
            }
        }

        private static bool IsHan(int code)
        {
            return (code >= 0x3400 && code <= 0x4DBF) ||
                   (code >= 0x4E00 && code <= 0x9FFF) ||
                   (code >= 0xF900 && code <= 0xFAFF);
        }

        private static bool IsKana(int code)
        {
            return (code >= 0x3040 && code <= 0x30FF) ||
                   (code >= 0x31F0 && code <= 0x31FF);
        }

        private static bool IsHangul(int code)
        {
            return (code >= 0x1100 && code <= 0x11FF) ||
                   (code >= 0x3130 && code <= 0x318F) ||
                   (code >= 0xAC00 && code <= 0xD7AF);
        }

        private static bool IsLatinLetter(int code)
        {
            return (code >= 'A' && code <= 'Z') ||
                   (code >= 'a' && code <= 'z') ||
                   (code >= 0x00C0 && code <= 0x024F);
        }
    }
}
