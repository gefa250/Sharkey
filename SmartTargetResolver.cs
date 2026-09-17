using System;
using System.Text.RegularExpressions;

namespace GlobalTranslator
{
    internal static class SmartTargetResolver
    {
        private static readonly Regex DirectionNoisePattern =
            new Regex(
                @"(?i)(?:\bhttps?://|\bwww\.)\S+|\b[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}\b",
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant);

        private static readonly Regex EmailPattern =
            new Regex(
                @"^[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}$",
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant |
                RegexOptions.IgnoreCase);

        private static readonly Regex CodeKeywordPattern =
            new Regex(
                @"^\s*(?:if|else|for|foreach|while|switch|case|return|var|let|const|class|struct|interface|public|private|protected|internal|static|void|function|namespace|using|import|from|def|select|insert|update|delete)\b",
                RegexOptions.Compiled |
                RegexOptions.CultureInvariant |
                RegexOptions.IgnoreCase);

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
            string meaningfulText = DirectionNoisePattern.Replace(
                text ?? "",
                " ");
            int han = 0;
            int latin = 0;
            int kana = 0;
            int hangul = 0;
            foreach (char value in meaningfulText)
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

            if (EmailPattern.IsMatch(value))
                return true;

            bool hasLetter = false;
            bool hasDigit = false;
            foreach (char character in value)
            {
                if (char.IsLetter(character))
                    hasLetter = true;
                if (char.IsDigit(character))
                    hasDigit = true;
            }
            if (!hasLetter && hasDigit)
                return true;

            if (LooksLikeStandaloneSku(value))
                return true;
            if (LooksLikeCode(value))
                return true;
            return false;
        }

        private static bool LooksLikeStandaloneSku(string value)
        {
            if (value.Length > 128 ||
                value.IndexOfAny(new[] { ' ', '\t', '\r', '\n' }) >= 0)
                return false;

            bool hasLatinLetter = false;
            bool hasDigit = false;
            foreach (char character in value)
            {
                if (IsLatinLetter(character))
                {
                    hasLatinLetter = true;
                    continue;
                }
                if (char.IsDigit(character))
                {
                    hasDigit = true;
                    continue;
                }
                if ("._-/".IndexOf(character) < 0)
                    return false;
            }
            return hasLatinLetter && hasDigit;
        }

        private static bool LooksLikeCode(string value)
        {
            bool hasPairedBraces =
                value.IndexOf('{') >= 0 && value.IndexOf('}') >= 0;
            bool hasStrongOperator =
                value.IndexOf("=>", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("==", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("!=", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("&&", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("||", StringComparison.Ordinal) >= 0 ||
                value.IndexOf("::", StringComparison.Ordinal) >= 0;
            if (hasPairedBraces || hasStrongOperator)
                return true;

            bool hasAssignment = value.IndexOf('=') >= 0;
            bool hasStatementEnd = value.IndexOf(';') >= 0;
            bool hasParentheses =
                value.IndexOf('(') >= 0 && value.IndexOf(')') >= 0;
            bool hasBrackets =
                value.IndexOf('[') >= 0 && value.IndexOf(']') >= 0;
            bool hasCodeKeyword = CodeKeywordPattern.IsMatch(value);

            int syntaxSignals = 0;
            if (hasAssignment) syntaxSignals++;
            if (hasStatementEnd) syntaxSignals++;
            if (hasParentheses) syntaxSignals++;
            if (hasBrackets) syntaxSignals++;

            if (hasCodeKeyword && syntaxSignals > 0)
                return true;

            int wordCount = Regex.Matches(
                value,
                @"[\p{L}_][\p{L}\p{Nd}_-]*").Count;
            return syntaxSignals >= 2 && wordCount <= 3;
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
