using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace GlobalTranslator
{
    internal sealed class HistoryEntry
    {
        public DateTime Time;
        public string Source, Translation, Target, Provider, Requirements;
        public bool Rewrite;
        public override string ToString()
        {
            string preview = Source.Replace("\r", " ").Replace("\n", " ");
            return Time.ToString("HH:mm") + "  " + (preview.Length > 90 ? preview.Substring(0, 90) + "…" : preview);
        }
    }

    internal static class TranslationHistory
    {
        private static readonly List<HistoryEntry> Entries = new List<HistoryEntry>();
        public static void Add(string source, TranslationResult result, string requirements, bool rewrite)
        {
            if (string.IsNullOrWhiteSpace(result.Text)) return;
            lock (Entries)
            {
                Entries.RemoveAll(e => e.Source == source && e.Translation == result.Text && e.Requirements == requirements);
                Entries.Insert(0, new HistoryEntry { Time = DateTime.Now, Source = source,
                    Translation = result.Text, Target = result.EffectiveTargetLanguage,
                    Provider = result.Provider, Requirements = requirements ?? "", Rewrite = rewrite });
                if (Entries.Count > 100) Entries.RemoveAt(Entries.Count - 1);
            }
        }
        public static HistoryEntry[] Search(string query)
        {
            lock (Entries) return Entries.Where(e => string.IsNullOrEmpty(query) ||
                (e.Source + "\n" + e.Translation).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0).ToArray();
        }
        public static void Clear() { lock (Entries) Entries.Clear(); }
    }

    internal static class TextTools
    {
        public static string JoinLines(string text)
        {
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var output = new System.Text.StringBuilder();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (i > 0)
                {
                    string previous = lines[i - 1];
                    bool boundary = string.IsNullOrWhiteSpace(line) || string.IsNullOrWhiteSpace(previous) ||
                        Structured(line) || Structured(previous) || Regex.IsMatch(previous.TrimEnd(), "[。！？.!?:：;；]$");
                    if (boundary) output.Append('\n');
                    else if (!(Regex.IsMatch(previous.TrimEnd(), "[\u4e00-\u9fff]$") && Regex.IsMatch(line.TrimStart(), "^[\u4e00-\u9fff]"))) output.Append(' ');
                }
                output.Append(line.TrimEnd());
            }
            return output.ToString();
        }
        private static bool Structured(string line)
        {
            return Regex.IsMatch(line, @"^\s*(?:[-*•]|\d+[.)、])\s|\t|\S {2,}\S|\||^ {2,}\S");
        }
        private static HashSet<string> Tokens(string text)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // Grouped thousands normalize, while decimal separators, signs and units remain significant.
            foreach (Match m in Regex.Matches(text ?? "", @"(?<![A-Za-z0-9])(?=[A-Za-z0-9_-]*[A-Za-z])(?=[A-Za-z0-9_-]*\d)[A-Za-z0-9]+(?:[-_/][A-Za-z0-9]+)*|(?<![A-Za-z0-9])[-+]?\d+(?:[,，]\d{3})*(?:\.\d+)?(?:%|％)?"))
                set.Add(m.Value.Replace(",", "").Replace("，", "").Replace("％", "%"));
            return set;
        }
        public static string Check(string source, string translated)
        {
            var before = Tokens(source); var after = Tokens(translated);
            string missing = string.Join("、", before.Except(after).Take(12).ToArray());
            string added = string.Join("、", after.Except(before).Take(12).ToArray());
            if (missing.Length == 0 && added.Length == 0) return "";
            return "请核对数字/型号（格式转换也可能触发提示）" +
                (missing.Length > 0 ? "\n原文有、译文未匹配：" + missing : "") +
                (added.Length > 0 ? "\n译文新增或变化：" + added : "");
        }
    }
}
