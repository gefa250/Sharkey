using System;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace GlobalTranslator
{
    internal sealed class TableDocument
    {
        public TableSheet[] tables { get; set; }
        public bool too_large { get; set; }

        public static TableDocument Parse(string text)
        {
            try
            {
                string json = (text ?? "").Trim();
                if (json.StartsWith("```"))
                {
                    int line = json.IndexOf('\n');
                    if (line < 0 || !json.EndsWith("```")) throw new FormatException();
                    json = json.Substring(line + 1, json.Length - line - 4).Trim();
                }
                var serializer = new JavaScriptSerializer { MaxJsonLength = 2000000 };
                var raw = serializer.DeserializeObject(json) as System.Collections.Generic.Dictionary<string, object>;
                object tablesRaw;
                if (raw == null || !raw.TryGetValue("tables", out tablesRaw) || !(tablesRaw is object[])) throw new FormatException();
                foreach (object item in (object[])tablesRaw)
                {
                    var tableRaw = item as System.Collections.Generic.Dictionary<string, object>;
                    object rowsRaw;
                    if (tableRaw == null || !tableRaw.TryGetValue("rows", out rowsRaw) || !(rowsRaw is object[])) throw new FormatException();
                    foreach (object row in (object[])rowsRaw)
                        if (!(row is object[]) || ((object[])row).Any(cell => !(cell is string))) throw new FormatException();
                }
                var result = serializer.Deserialize<TableDocument>(json);
                if (result == null || result.tables == null || result.tables.Length > 10) throw new FormatException();
                if (result.too_large) throw new InvalidOperationException("表格过大，请分区域截图识别。");
                foreach (var table in result.tables)
                {
                    if (table == null || table.rows == null || table.rows.Length == 0 || table.rows.Length > 500 ||
                        table.rows.Any(r => r == null || r.Length == 0 || r.Length > 50 || r.Any(c => c == null || c.Length > 10000)))
                        throw new FormatException();
                    int columns = table.rows[0].Length;
                    if (table.rows.Any(r => r.Length != columns)) throw new FormatException();
                }
                return result;
            }
            catch (InvalidOperationException) { throw; }
            catch (Exception) { throw new InvalidOperationException("表格结构不完整，请缩小截图范围后重试。"); }
        }

        // Quote every cell; neutralize spreadsheet formulas when exporting untrusted image text.
        public static string Export(string[][] rows, char separator)
        {
            var output = new StringBuilder();
            foreach (string[] row in rows)
            {
                if (output.Length > 0) output.Append("\r\n");
                output.Append(string.Join(separator.ToString(), row.Select(cell =>
                {
                    string value = cell ?? "";
                    string trimmed = value.TrimStart();
                    decimal number;
                    if (trimmed.Length > 0 && "=+-@".IndexOf(trimmed[0]) >= 0 &&
                        !decimal.TryParse(trimmed, System.Globalization.NumberStyles.Number,
                            System.Globalization.CultureInfo.InvariantCulture, out number)) value = "'" + value;
                    return "\"" + value.Replace("\"", "\"\"") + "\"";
                })));
            }
            return output.ToString();
        }
    }

    internal sealed class TableSheet
    {
        public string title { get; set; }
        public string[][] rows { get; set; }
    }
}
