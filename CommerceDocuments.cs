using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Xml.Linq;
using System.Xml;

namespace GlobalTranslator
{
    internal sealed class CommerceDocument
    {
        public string Name = "";
        public string Kind = "";
        public string Status = "已就绪";
        public string Text = "";
        public override string ToString() { return Name + " · " + Status; }
    }

    internal static class CommerceDocuments
    {
        private static readonly object PdfLoadLock = new object();
        private static readonly Dictionary<string, Assembly> PdfAssemblies = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        private static bool _pdfResolverAttached;
        internal const int MaxFiles = 5;
        internal const int MaxCharacters = 60000;
        internal static readonly string[] Extensions = { ".pdf", ".docx", ".xlsx", ".csv" };

        internal static bool IsSupported(string path)
        { return Extensions.Contains(Path.GetExtension(path).ToLowerInvariant()); }

        internal static CommerceDocument Read(string path)
        {
            if (!IsSupported(path)) throw new InvalidDataException("支持 PDF、DOCX、XLSX 和 CSV 文件。");
            var file = new FileInfo(path);
            if (!file.Exists || file.Length > 20 * 1024 * 1024)
                throw new InvalidDataException("单个文档需小于 20 MB。");
            string extension = file.Extension.ToLowerInvariant();
            string text;
            if (extension == ".docx") text = ReadWord(path);
            else if (extension == ".xlsx") text = ReadWorkbook(path);
            else if (extension == ".csv") text = ReadCsv(path);
            else text = ReadPdf(path);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidDataException("文档中没有可读取的文字。扫描件请截取相关页面添加为图片。");
            if (text.Length > MaxCharacters)
                throw new InvalidDataException("文档超过一次可处理的内容长度，请拆分文件或选择相关页面。");
            return new CommerceDocument { Name = file.Name, Kind = extension.TrimStart('.').ToUpperInvariant(),
                Text = text, Status = extension == ".pdf" ? "文本已读取" : "已就绪" };
        }

        private static XDocument LoadXml(ZipArchive archive, string name)
        {
            ZipArchiveEntry entry = archive.GetEntry(name);
            if (entry == null) throw new InvalidDataException("文档缺少必要的内容文件。");
            if (entry.Length > 25 * 1024 * 1024)
                throw new InvalidDataException("文档内容过大，请选择相关页或工作表。");
            using (var stream = entry.Open())
            using (var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                MaxCharactersInDocument = 25 * 1024 * 1024
            })) return XDocument.Load(reader);
        }

        private static string ReadWord(string path)
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                XDocument doc = LoadXml(archive, "word/document.xml");
                var lines = new List<string>();
                foreach (XElement paragraph in doc.Descendants(w + "p"))
                {
                    string line = string.Concat(paragraph.Descendants(w + "t").Select(x => x.Value));
                    if (!string.IsNullOrWhiteSpace(line)) lines.Add(line.Trim());
                }
                return string.Join("\n", lines);
            }
        }

        private static string ReadWorkbook(string path)
        {
            using (var archive = ZipFile.OpenRead(path))
            {
                XNamespace m = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
                XNamespace r = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
                XDocument book = LoadXml(archive, "xl/workbook.xml");
                XDocument rels = LoadXml(archive, "xl/_rels/workbook.xml.rels");
                var paths = rels.Descendants().Where(x => x.Name.LocalName == "Relationship")
                    .ToDictionary(x => (string)x.Attribute("Id"), x => (string)x.Attribute("Target"));
                var shared = new List<string>();
                if (archive.GetEntry("xl/sharedStrings.xml") != null)
                    shared = LoadXml(archive, "xl/sharedStrings.xml").Descendants(m + "si")
                        .Select(si => string.Concat(si.Descendants(m + "t").Select(t => t.Value))).ToList();
                var output = new StringBuilder();
                foreach (XElement sheet in book.Descendants(m + "sheet"))
                {
                    string id = (string)sheet.Attribute(r + "id"), target;
                    if (id == null || !paths.TryGetValue(id, out target)) continue;
                    string name = target.StartsWith("/") ? target.TrimStart('/') : "xl/" + target.TrimStart('/');
                    if (name.Contains("..")) throw new InvalidDataException("工作表路径无效。");
                    XDocument cells = LoadXml(archive, name.Replace('\\', '/'));
                    output.Append("\n[工作表: ").Append((string)sheet.Attribute("name")).Append("]\n");
                    foreach (XElement row in cells.Descendants(m + "row"))
                    {
                        var values = new List<string>();
                        foreach (XElement cell in row.Elements(m + "c"))
                        {
                            string raw = (string)cell.Element(m + "v") ?? "";
                            if ((string)cell.Attribute("t") == "s")
                            {
                                int index;
                                if (int.TryParse(raw, out index) && index >= 0 && index < shared.Count)
                                    raw = shared[index];
                            }
                            else if ((string)cell.Attribute("t") == "inlineStr")
                                raw = string.Concat(cell.Descendants(m + "t").Select(x => x.Value));
                            if (raw.Length > 0) values.Add((string)cell.Attribute("r") + ": " + raw);
                        }
                        if (values.Count > 0) output.AppendLine(string.Join(" | ", values));
                        if (output.Length > MaxCharacters) break;
                    }
                    if (output.Length > MaxCharacters) break;
                }
                return output.ToString().Trim();
            }
        }

        private static string ReadCsv(string path)
        {
            string content;
            try
            {
                using (var reader = new StreamReader(path, new UTF8Encoding(false, true), true))
                    content = reader.ReadToEnd();
            }
            catch (DecoderFallbackException)
            {
                // Excel on Chinese Windows often exports legacy GBK/GB18030 CSV without a BOM.
                content = Encoding.GetEncoding("GB18030").GetString(File.ReadAllBytes(path));
            }
            var output = new StringBuilder();
            var cell = new StringBuilder();
            var row = new List<string>();
            bool quoted = false;
            int line = 1;
            char delimiter = content.Contains("\t") && !content.Contains(",") ? '\t' : ',';
            for (int i = 0; i < content.Length; i++)
            {
                char c = content[i];
                if (c == '"')
                {
                    if (quoted && i + 1 < content.Length && content[i + 1] == '"')
                    { cell.Append('"'); i++; }
                    else quoted = !quoted;
                }
                else if (c == delimiter && !quoted) { row.Add(cell.ToString()); cell.Clear(); }
                else if ((c == '\n' || c == '\r') && !quoted)
                {
                    if (c == '\r' && i + 1 < content.Length && content[i + 1] == '\n') i++;
                    row.Add(cell.ToString()); cell.Clear();
                    AppendCsvRow(output, row, line++); row.Clear();
                }
                else cell.Append(c);
                if (cell.Length > MaxCharacters)
                    throw new InvalidDataException("CSV 单元格内容过长，请拆分文件。");
                if (output.Length > MaxCharacters) break;
            }
            if (quoted) throw new InvalidDataException("CSV 引号没有闭合。");
            if (cell.Length > 0 || row.Count > 0)
            { row.Add(cell.ToString()); AppendCsvRow(output, row, line); }
            return output.ToString().Trim();
        }

        private static void AppendCsvRow(StringBuilder output, List<string> row, int number)
        {
            if (row.All(string.IsNullOrWhiteSpace)) return;
            output.Append("[行 ").Append(number).Append("] ");
            for (int i = 0; i < row.Count; i++)
                output.Append(i == 0 ? "" : " | ").Append(i + 1).Append(": ").Append(row[i]);
            output.AppendLine();
        }

        private static string ReadPdf(string path)
        {
            try
            {
                Assembly library = LoadPdfAssembly("UglyToad.PdfPig");
                Type documentType = library.GetType("UglyToad.PdfPig.PdfDocument", true);
                MethodInfo open = documentType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .First(m => m.Name == "Open" && m.GetParameters().Length == 2 &&
                        m.GetParameters()[0].ParameterType == typeof(string));
                using (var document = (IDisposable)open.Invoke(null, new object[] { path, null }))
                {
                    int pages = (int)documentType.GetProperty("NumberOfPages").GetValue(document, null);
                    MethodInfo getPage = documentType.GetMethod("GetPage", new[] { typeof(int) });
                    var result = new StringBuilder();
                    for (int index = 1; index <= pages; index++)
                    {
                        object page = getPage.Invoke(document, new object[] { index });
                        string text = (string)page.GetType().GetProperty("Text").GetValue(page, null);
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        result.Append("[第 ").Append(index).Append(" 页]\n").Append(text.Trim()).Append("\n\n");
                        if (result.Length > MaxCharacters) break;
                    }
                    if (result.Length == 0)
                        throw new InvalidDataException("PDF 中没有可提取的文字。扫描件请截取相关页面添加为图片。");
                    return result.ToString().Trim();
                }
            }
            catch (TargetInvocationException ex)
            {
                throw new InvalidDataException("PDF 读取失败：" +
                    (ex.InnerException == null ? ex.Message : ex.InnerException.Message), ex);
            }
        }

        private static Assembly LoadPdfAssembly(string simpleName)
        {
            lock (PdfLoadLock)
            {
                Assembly loaded;
                if (PdfAssemblies.TryGetValue(simpleName, out loaded)) return loaded;
                if (!_pdfResolverAttached)
                {
                    AppDomain.CurrentDomain.AssemblyResolve += ResolvePdfAssembly;
                    _pdfResolverAttached = true;
                }
                Assembly host = Assembly.GetExecutingAssembly();
                string resource = host.GetManifestResourceNames().FirstOrDefault(name =>
                    name.EndsWith("." + simpleName + ".dll", StringComparison.OrdinalIgnoreCase));
                if (resource == null) throw new InvalidDataException("缺少 PDF 文本读取组件。");
                using (Stream stream = host.GetManifestResourceStream(resource))
                {
                    byte[] bytes = new byte[stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int count = stream.Read(bytes, offset, bytes.Length - offset);
                        if (count == 0) throw new EndOfStreamException("PDF 组件读取不完整。");
                        offset += count;
                    }
                    loaded = Assembly.Load(bytes);
                }
                PdfAssemblies[simpleName] = loaded;
                return loaded;
            }
        }

        private static Assembly ResolvePdfAssembly(object sender, ResolveEventArgs args)
        {
            string simpleName = new AssemblyName(args.Name).Name;
            if (!simpleName.StartsWith("UglyToad.PdfPig", StringComparison.OrdinalIgnoreCase) &&
                !simpleName.Equals("System.ValueTuple", StringComparison.OrdinalIgnoreCase)) return null;
            return LoadPdfAssembly(simpleName);
        }
    }
}
