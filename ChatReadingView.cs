using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GlobalTranslator
{
    // Render a small, safe Markdown subset as selectable WPF content. Never execute HTML or links.
    internal static class ChatReadingView
    {
        internal static RichTextBox Create(string text)
        {
            var view = new RichTextBox
            {
                IsReadOnly = true, IsReadOnlyCaretVisible = true,
                BorderThickness = new Thickness(0), Padding = new Thickness(0),
                Background = Brushes.Transparent, FontSize = 15,
                VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                IsDocumentEnabled = false
            };
            SetText(view, text);
            return view;
        }

        internal static void SetText(RichTextBox view, string text)
        {
            var document = new FlowDocument
            {
                FontFamily = new FontFamily("Microsoft YaHei UI"), FontSize = 15,
                Foreground = new SolidColorBrush(Color.FromRgb(35, 54, 67)),
                PagePadding = new Thickness(0), LineHeight = 25,
                ColumnWidth = double.PositiveInfinity
            };
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) continue;
                if (i + 1 < lines.Length && line.Contains("|") && IsDivider(lines[i + 1]))
                {
                    string[] headers = Cells(line);
                    var table = new Table { CellSpacing = 0, Margin = new Thickness(0, 8, 0, 16) };
                    for (int c = 0; c < headers.Length; c++)
                        table.Columns.Add(new TableColumn { Width = new GridLength(1, GridUnitType.Star) });
                    var rows = new TableRowGroup(); table.RowGroups.Add(rows);
                    AddRow(rows, headers, headers.Length, true, 0);
                    i += 2;
                    int row = 0;
                    while (i < lines.Length && lines[i].Contains("|") && !string.IsNullOrWhiteSpace(lines[i]))
                    { AddRow(rows, Cells(lines[i]), headers.Length, false, row++); i++; }
                    i--;
                    document.Blocks.Add(table);
                    continue;
                }
                if (line.StartsWith("```"))
                {
                    var code = new Paragraph { FontFamily = new FontFamily("Consolas"), FontSize = 13,
                        Background = new SolidColorBrush(Color.FromRgb(240, 245, 248)),
                        Padding = new Thickness(10), Margin = new Thickness(0, 8, 0, 12) };
                    for (i++; i < lines.Length && !lines[i].TrimStart().StartsWith("```"); i++)
                    { if (code.Inlines.Count > 0) code.Inlines.Add(new LineBreak()); code.Inlines.Add(new Run(lines[i])); }
                    document.Blocks.Add(code); continue;
                }
                var heading = Regex.Match(line, @"^(#{1,6})\s+(.+)$");
                var item = Regex.Match(line, @"^(?:([-*+])\s+|(\d+[.)、])\s*)(.+)$");
                var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 10) };
                if (heading.Success)
                {
                    paragraph.FontSize = heading.Groups[1].Length <= 2 ? 18 : 16;
                    paragraph.FontWeight = FontWeights.SemiBold;
                    paragraph.Margin = new Thickness(0, 14, 0, 9);
                    AddInline(paragraph, heading.Groups[2].Value);
                }
                else if (item.Success)
                {
                    paragraph.Margin = new Thickness(20, 0, 0, 6);
                    paragraph.TextIndent = -18;
                    paragraph.Inlines.Add(new Run(item.Groups[2].Success ? item.Groups[2].Value + "  " : "•  "));
                    AddInline(paragraph, item.Groups[3].Value);
                }
                else AddInline(paragraph, line);
                document.Blocks.Add(paragraph);
            }
            view.Document = document;
        }

        internal static string ToPlainText(string text)
        {
            string[] lines = (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            var output = new List<string>();
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0) { output.Add(""); continue; }
                if (i + 1 < lines.Length && line.Contains("|") && IsDivider(lines[i + 1]))
                {
                    output.Add(string.Join("\t", Cells(line)));
                    i++;
                    while (i + 1 < lines.Length && lines[i + 1].Contains("|") && !string.IsNullOrWhiteSpace(lines[i + 1]))
                        output.Add(string.Join("\t", Cells(lines[++i])));
                    continue;
                }
                line = Regex.Replace(line, @"^#{1,6}\s+", "");
                line = Regex.Replace(line, @"^(?:[-*+]\s+|\d+[.)、]\s*)", "");
                output.Add(Regex.Replace(line, @"\*\*(.+?)\*\*|`([^`]+)`", match =>
                    match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value));
            }
            return string.Join(Environment.NewLine, output).Trim();
        }

        private static void AddInline(Paragraph paragraph, string text)
        {
            int start = 0;
            foreach (Match match in Regex.Matches(text, @"\*\*(.+?)\*\*|`([^`]+)`"))
            {
                if (match.Index > start) paragraph.Inlines.Add(new Run(text.Substring(start, match.Index - start)));
                if (match.Groups[1].Success) paragraph.Inlines.Add(new Bold(new Run(match.Groups[1].Value)));
                else paragraph.Inlines.Add(new Run(match.Groups[2].Value) { FontFamily = new FontFamily("Consolas") });
                start = match.Index + match.Length;
            }
            if (start < text.Length) paragraph.Inlines.Add(new Run(text.Substring(start)));
        }

        private static string[] Cells(string line)
        {
            line = line.Trim();
            if (line.StartsWith("|")) line = line.Substring(1);
            if (line.EndsWith("|") && !line.EndsWith(@"\|")) line = line.Substring(0, line.Length - 1);
            return Regex.Split(line, @"(?<!\\)\|").Select(cell => cell.Trim().Replace(@"\|", "|")).ToArray();
        }

        private static bool IsDivider(string line)
        { string[] cells = Cells(line); return cells.Length > 0 && cells.All(cell => Regex.IsMatch(cell, @"^:?-{3,}:?$")); }

        private static void AddRow(TableRowGroup group, string[] values, int columns, bool header, int index)
        {
            var row = new TableRow();
            row.Background = new SolidColorBrush(header ? Color.FromRgb(227, 240, 246) :
                index % 2 == 0 ? Colors.White : Color.FromRgb(247, 250, 252));
            for (int i = 0; i < columns; i++)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0), LineHeight = 24 };
                AddInline(paragraph, i == columns - 1 && values.Length > columns ?
                    string.Join(" | ", values.Skip(i)) : i < values.Length ? values[i] : "");
                row.Cells.Add(new TableCell(paragraph)
                {
                    Padding = new Thickness(10, 8, 10, 8),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(213, 226, 233)), BorderThickness = new Thickness(.5),
                    FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal
                });
            }
            group.Rows.Add(row);
        }
    }
}
