using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Windows.Foundation;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace GlobalTranslator
{
    internal sealed class OcrOptions
    {
        public string LanguageTag = "auto";
        public bool AutoEnhance = true;
    }

    internal sealed class OcrRecognitionResult
    {
        public string Text = "";
        public string Engine = "Windows OCR";
        public string Language = "";
        public double QualityScore;
        public bool IsLowQuality;
        public bool UsedAi;
        public string Warning = "";
    }

    internal sealed class OcrService
    {
        public async Task<OcrRecognitionResult> RecognizeAsync(
            Bitmap bitmap, OcrOptions options)
        {
            if (bitmap == null) throw new ArgumentNullException("bitmap");
            options = options ?? new OcrOptions();

            bool automatic = string.IsNullOrWhiteSpace(options.LanguageTag) ||
                string.Equals(
                    options.LanguageTag, "auto",
                    StringComparison.OrdinalIgnoreCase);
            string warning;
            List<OcrEngine> engines =
                CreateEngines(options.LanguageTag, out warning);
            if (engines.Count == 0)
                throw new InvalidOperationException(
                    "Windows 没有可用的 OCR 语言包，请在系统语言设置中安装中文或英文识别语言。");

            var variants = new List<Bitmap>();
            var candidates = new List<Candidate>();
            OcrEngine engine = null;
            try
            {
                if (options.AutoEnhance)
                    BuildEnhancedVariants(bitmap, variants);
                else
                    variants.Add(To24Bit(bitmap));

                int firstVariant = 0;
                if (automatic && engines.Count > 1)
                {
                    var previews = new List<EnginePreview>();
                    for (int engineIndex = 0;
                        engineIndex < engines.Count;
                        engineIndex++)
                    {
                        OcrEngine candidateEngine = engines[engineIndex];
                        try
                        {
                            string previewText = CleanupText(
                                await RecognizeBitmapAsync(
                                    candidateEngine, variants[0]));
                            previews.Add(new EnginePreview
                            {
                                Engine = candidateEngine,
                                Text = previewText,
                                UserProfileEngine = engineIndex == 0,
                                UsefulCharacters =
                                    UsefulCharacterCount(previewText)
                            });
                        }
                        catch
                        {
                            // A single optional recognizer must not make
                            // automatic OCR fail. Other installed engines
                            // are still valid candidates.
                        }
                    }
                    int maximumCharacters = 0;
                    foreach (EnginePreview preview in previews)
                        maximumCharacters = Math.Max(
                            maximumCharacters,
                            preview.UsefulCharacters);
                    EnginePreview selected = null;
                    foreach (EnginePreview preview in previews)
                    {
                        string tag = preview.Engine.RecognizerLanguage == null
                            ? ""
                            : preview.Engine.RecognizerLanguage.LanguageTag;
                        preview.Score = AutoLanguageScore(
                            preview.Text,
                            tag,
                            preview.UserProfileEngine,
                            maximumCharacters);
                        if (selected == null ||
                            preview.Score > selected.Score)
                            selected = preview;
                    }
                    if (selected != null)
                    {
                        engine = selected.Engine;
                        candidates.Add(new Candidate
                        {
                            Text = selected.Text,
                            Plausibility =
                                PlausibilityScore(selected.Text)
                        });
                        firstVariant = 1;
                    }
                }
                if (engine == null) engine = engines[0];

                for (int i = firstVariant; i < variants.Count; i++)
                {
                    string text = CleanupText(
                        await RecognizeBitmapAsync(engine, variants[i]));
                    candidates.Add(new Candidate
                    {
                        Text = text,
                        Plausibility = PlausibilityScore(text)
                    });
                }
            }
            finally
            {
                foreach (Bitmap variant in variants) variant.Dispose();
            }

            ScoreAgreement(candidates);
            Candidate best = BestCandidate(candidates);
            double quality = best == null ? 0 : best.CombinedScore;
            bool inconsistent = candidates.Count > 1 &&
                best != null && best.Text.Length > 3 && best.Agreement < .35;
            bool lowQuality = best == null || string.IsNullOrWhiteSpace(best.Text) ||
                              quality < .58 || inconsistent;
            return new OcrRecognitionResult
            {
                Text = best == null ? "" : best.Text,
                Engine = "Windows OCR" +
                    (automatic
                        ? " · 自动(" +
                          (engine.RecognizerLanguage == null
                              ? "未知"
                              : engine.RecognizerLanguage.LanguageTag) + ")"
                        : "") +
                    (options.AutoEnhance ? " · 本地增强" : ""),
                Language = engine.RecognizerLanguage == null
                    ? ""
                    : engine.RecognizerLanguage.LanguageTag,
                QualityScore = Math.Max(0, Math.Min(1, quality)),
                IsLowQuality = lowQuality,
                UsedAi = false,
                Warning = warning ?? ""
            };
        }

        private static List<OcrEngine> CreateEngines(
            string requested, out string warning)
        {
            warning = "";
            if (!string.IsNullOrWhiteSpace(requested) &&
                !string.Equals(requested, "auto", StringComparison.OrdinalIgnoreCase))
            {
                foreach (Language available in OcrEngine.AvailableRecognizerLanguages)
                {
                    if (!LanguageMatches(available.LanguageTag, requested)) continue;
                    OcrEngine selected =
                        OcrEngine.TryCreateFromLanguage(new Language(available.LanguageTag));
                    if (selected != null)
                        return new List<OcrEngine> { selected };
                }
                warning = "未安装所选 OCR 语言包，已改用多语言自动识别。";
            }

            var result = new List<OcrEngine>();
            var categories = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            AddAutoEngine(
                result, categories,
                OcrEngine.TryCreateFromUserProfileLanguages());
            foreach (Language available in
                OcrEngine.AvailableRecognizerLanguages)
            {
                if (!IsAutomaticCandidate(available.LanguageTag)) continue;
                OcrEngine engine = OcrEngine.TryCreateFromLanguage(
                    new Language(available.LanguageTag));
                AddAutoEngine(result, categories, engine);
            }
            return result;
        }

        private static void AddAutoEngine(
            List<OcrEngine> engines,
            HashSet<string> categories,
            OcrEngine engine)
        {
            if (engine == null || engine.RecognizerLanguage == null) return;
            string category =
                LanguageCategory(engine.RecognizerLanguage.LanguageTag);
            if (!categories.Add(category)) return;
            engines.Add(engine);
        }

        private static bool IsAutomaticCandidate(string tag)
        {
            string root = ((tag ?? "").Split('-')[0]).ToLowerInvariant();
            return root == "en" || root == "zh" ||
                   root == "ja" || root == "ko";
        }

        private static string LanguageCategory(string tag)
        {
            if ((tag ?? "").StartsWith(
                "zh-Hans", StringComparison.OrdinalIgnoreCase))
                return "zh-Hans";
            if ((tag ?? "").StartsWith(
                "zh-Hant", StringComparison.OrdinalIgnoreCase))
                return "zh-Hant";
            return (tag ?? "").Split('-')[0].ToLowerInvariant();
        }

        private static double AutoLanguageScore(
            string text,
            string languageTag,
            bool userProfileEngine,
            int maximumCharacters)
        {
            double score =
                PlausibilityScore(text) * .35 +
                LanguageFitScore(text, languageTag) * .25;
            if (maximumCharacters > 0)
                score += Math.Min(
                    1,
                    UsefulCharacterCount(text) /
                    (double)maximumCharacters) * .4;
            if (userProfileEngine) score += .02;
            return Math.Max(0, Math.Min(1, score));
        }

        private static int UsefulCharacterCount(string text)
        {
            int count = 0;
            foreach (char value in text ?? "")
                if (!char.IsWhiteSpace(value) &&
                    !char.IsControl(value))
                    count++;
            return count;
        }

        private static double LanguageFitScore(
            string text, string languageTag)
        {
            int latin = 0;
            int han = 0;
            int kana = 0;
            int hangul = 0;
            foreach (char value in text ?? "")
            {
                if ((value >= 'A' && value <= 'Z') ||
                    (value >= 'a' && value <= 'z'))
                    latin++;
                else if (value >= '\u3400' && value <= '\u9FFF')
                    han++;
                else if (value >= '\u3040' && value <= '\u30FF')
                    kana++;
                else if (value >= '\uAC00' && value <= '\uD7AF')
                    hangul++;
            }
            int letters = latin + han + kana + hangul;
            if (letters == 0) return .5;

            double latinRatio = latin / (double)letters;
            double hanRatio = han / (double)letters;
            double kanaRatio = kana / (double)letters;
            double hangulRatio = hangul / (double)letters;
            double cjkRatio = hanRatio + kanaRatio + hangulRatio;
            string root =
                ((languageTag ?? "").Split('-')[0]).ToLowerInvariant();
            if (root == "en")
                return cjkRatio >= .1
                    ? Math.Max(.12, latinRatio * .45)
                    : (latinRatio >= .65 ? 1 : Math.Max(.1, latinRatio));
            if (root == "zh")
                return hanRatio >= .1
                    ? Math.Min(1, .76 + hanRatio * .24)
                    : Math.Max(.16, hanRatio);
            if (root == "ja")
            {
                if (kanaRatio > .04)
                    return Math.Min(1, .82 + kanaRatio);
                return hanRatio >= .35 ? .7 : .18;
            }
            if (root == "ko")
                return hangulRatio >= .15
                    ? Math.Min(1, .8 + hangulRatio)
                    : .18;
            return .45;
        }

        private static bool LanguageMatches(string available, string requested)
        {
            if (string.Equals(available, requested, StringComparison.OrdinalIgnoreCase))
                return true;
            if ((requested ?? "").StartsWith(
                "zh-", StringComparison.OrdinalIgnoreCase))
                return (available ?? "").StartsWith(
                    requested, StringComparison.OrdinalIgnoreCase);
            string availableRoot = (available ?? "").Split('-')[0];
            string requestedRoot = (requested ?? "").Split('-')[0];
            return string.Equals(
                availableRoot, requestedRoot, StringComparison.OrdinalIgnoreCase);
        }

        private static void BuildEnhancedVariants(Bitmap source, List<Bitmap> variants)
        {
            double factor;
            if (source.Height < 120) factor = 4;
            else if (source.Height < 300) factor = 3;
            else if (source.Height < 700) factor = 2;
            else factor = 1;

            int longest = Math.Max(source.Width, source.Height);
            int maximum = (int)OcrEngine.MaxImageDimension;
            if (longest > 0)
                factor = Math.Min(factor, maximum / (double)longest);
            factor = Math.Max(.1, factor);

            Bitmap scaled = Scale(source, factor);
            variants.Add(scaled);

            Bitmap contrast;
            Bitmap binary;
            CreateProcessedVariants(scaled, out contrast, out binary);
            variants.Add(contrast);
            variants.Add(binary);
        }

        private static Bitmap Scale(Bitmap source, double factor)
        {
            int width = Math.Max(1, (int)Math.Floor(source.Width * factor));
            int height = Math.Max(1, (int)Math.Floor(source.Height * factor));
            var result = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.White);
                graphics.CompositingQuality = CompositingQuality.HighQuality;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.SmoothingMode = SmoothingMode.HighQuality;
                graphics.DrawImage(source, new Rectangle(0, 0, width, height));
            }
            return result;
        }

        private static Bitmap To24Bit(Bitmap source)
        {
            var result = new Bitmap(
                source.Width, source.Height, PixelFormat.Format24bppRgb);
            using (Graphics graphics = Graphics.FromImage(result))
            {
                graphics.Clear(Color.White);
                graphics.DrawImageUnscaled(source, 0, 0);
            }
            return result;
        }

        private static void CreateProcessedVariants(
            Bitmap input, out Bitmap contrast, out Bitmap binary)
        {
            Bitmap source = input.PixelFormat == PixelFormat.Format24bppRgb
                ? input
                : To24Bit(input);
            bool disposeSource = !ReferenceEquals(source, input);
            try
            {
                Rectangle rectangle = new Rectangle(0, 0, source.Width, source.Height);
                BitmapData data = source.LockBits(
                    rectangle, ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);
                byte[] pixels;
                int stride = Math.Abs(data.Stride);
                try
                {
                    pixels = new byte[stride * source.Height];
                    Marshal.Copy(data.Scan0, pixels, 0, pixels.Length);
                }
                finally
                {
                    source.UnlockBits(data);
                }

                int count = source.Width * source.Height;
                byte[] gray = new byte[count];
                int[] histogram = new int[256];
                long luminanceSum = 0;
                int index = 0;
                for (int y = 0; y < source.Height; y++)
                {
                    int row = y * stride;
                    for (int x = 0; x < source.Width; x++)
                    {
                        int pixel = row + x * 3;
                        int value = (pixels[pixel + 2] * 299 +
                                     pixels[pixel + 1] * 587 +
                                     pixels[pixel] * 114) / 1000;
                        gray[index++] = (byte)value;
                        histogram[value]++;
                        luminanceSum += value;
                    }
                }

                int low = Percentile(histogram, count, .02);
                int high = Percentile(histogram, count, .98);
                if (high <= low) { low = 0; high = 255; }
                int threshold = OtsuThreshold(histogram, count);
                bool darkBackground = count > 0 &&
                    luminanceSum / (double)count < 128;

                contrast = CreateGrayBitmap(
                    source.Width, source.Height, gray,
                    delegate(byte value)
                    {
                        int adjusted = (value - low) * 255 / Math.Max(1, high - low);
                        adjusted = Math.Max(0, Math.Min(255, adjusted));
                        if (darkBackground) adjusted = 255 - adjusted;
                        return (byte)adjusted;
                    });
                binary = CreateGrayBitmap(
                    source.Width, source.Height, gray,
                    delegate(byte value)
                    {
                        bool foreground = darkBackground
                            ? value > threshold
                            : value <= threshold;
                        return foreground ? (byte)0 : (byte)255;
                    });
            }
            finally
            {
                if (disposeSource) source.Dispose();
            }
        }

        private static Bitmap CreateGrayBitmap(
            int width, int height, byte[] gray, Func<byte, byte> transform)
        {
            var bitmap = new Bitmap(width, height, PixelFormat.Format24bppRgb);
            Rectangle rectangle = new Rectangle(0, 0, width, height);
            BitmapData data = bitmap.LockBits(
                rectangle, ImageLockMode.WriteOnly, PixelFormat.Format24bppRgb);
            int stride = Math.Abs(data.Stride);
            byte[] pixels = new byte[stride * height];
            int index = 0;
            for (int y = 0; y < height; y++)
            {
                int row = y * stride;
                for (int x = 0; x < width; x++)
                {
                    byte value = transform(gray[index++]);
                    int pixel = row + x * 3;
                    pixels[pixel] = value;
                    pixels[pixel + 1] = value;
                    pixels[pixel + 2] = value;
                }
            }
            Marshal.Copy(pixels, 0, data.Scan0, pixels.Length);
            bitmap.UnlockBits(data);
            return bitmap;
        }

        private static int Percentile(int[] histogram, int count, double percentile)
        {
            int target = (int)(count * percentile);
            int sum = 0;
            for (int value = 0; value < histogram.Length; value++)
            {
                sum += histogram[value];
                if (sum >= target) return value;
            }
            return 255;
        }

        private static int OtsuThreshold(int[] histogram, int count)
        {
            long total = 0;
            for (int i = 0; i < 256; i++) total += (long)i * histogram[i];
            long backgroundSum = 0;
            int backgroundWeight = 0;
            double bestVariance = -1;
            int best = 127;
            for (int threshold = 0; threshold < 256; threshold++)
            {
                backgroundWeight += histogram[threshold];
                if (backgroundWeight == 0) continue;
                int foregroundWeight = count - backgroundWeight;
                if (foregroundWeight == 0) break;
                backgroundSum += (long)threshold * histogram[threshold];
                double backgroundMean =
                    backgroundSum / (double)backgroundWeight;
                double foregroundMean =
                    (total - backgroundSum) / (double)foregroundWeight;
                double difference = backgroundMean - foregroundMean;
                double variance =
                    backgroundWeight * (double)foregroundWeight *
                    difference * difference;
                if (variance > bestVariance)
                {
                    bestVariance = variance;
                    best = threshold;
                }
            }
            return best;
        }

        private static async Task<string> RecognizeBitmapAsync(
            OcrEngine engine, Bitmap bitmap)
        {
            if (bitmap.Width > OcrEngine.MaxImageDimension ||
                bitmap.Height > OcrEngine.MaxImageDimension)
                throw new InvalidOperationException(
                    "增强后的截图超过 Windows OCR 尺寸限制。");

            string tempFile = Path.Combine(
                Path.GetTempPath(),
                "Sharkey-OCR-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                bitmap.Save(tempFile, ImageFormat.Png);
                StorageFile file =
                    await WaitAsync(StorageFile.GetFileFromPathAsync(tempFile));
                IRandomAccessStream stream =
                    await WaitAsync(file.OpenAsync(FileAccessMode.Read));
                try
                {
                    BitmapDecoder decoder =
                        await WaitAsync(BitmapDecoder.CreateAsync(stream));
                    using (SoftwareBitmap softwareBitmap =
                        await WaitAsync(decoder.GetSoftwareBitmapAsync()))
                    {
                        OcrResult result =
                            await WaitAsync(engine.RecognizeAsync(softwareBitmap));
                        return ReconstructLayout(result);
                    }
                }
                finally
                {
                    stream.Dispose();
                }
            }
            finally
            {
                try { if (File.Exists(tempFile)) File.Delete(tempFile); }
                catch { }
            }
        }

        private static string ReconstructLayout(OcrResult result)
        {
            if (result == null || result.Lines == null)
                return "";

            var lines = new List<LayoutLine>();
            double characterWidthSum = 0;
            int characterCount = 0;
            foreach (OcrLine line in result.Lines)
            {
                var layout = new LayoutLine();
                bool hasBounds = false;
                foreach (OcrWord word in line.Words)
                {
                    Rect bounds = word.BoundingRect;
                    layout.Words.Add(word);
                    if (!hasBounds)
                    {
                        layout.Left = bounds.X;
                        layout.Top = bounds.Y;
                        layout.Right = bounds.X + bounds.Width;
                        layout.Bottom = bounds.Y + bounds.Height;
                        hasBounds = true;
                    }
                    else
                    {
                        layout.Left = Math.Min(layout.Left, bounds.X);
                        layout.Top = Math.Min(layout.Top, bounds.Y);
                        layout.Right = Math.Max(
                            layout.Right, bounds.X + bounds.Width);
                        layout.Bottom = Math.Max(
                            layout.Bottom, bounds.Y + bounds.Height);
                    }
                    int length = UsefulCharacterCount(word.Text);
                    if (length > 0 && bounds.Width > 0)
                    {
                        characterWidthSum += bounds.Width;
                        characterCount += length;
                    }
                }
                if (!hasBounds)
                {
                    layout.FallbackText = line.Text ?? "";
                    layout.Top = lines.Count;
                    layout.Bottom = layout.Top + 1;
                }
                lines.Add(layout);
            }
            if (lines.Count == 0) return result.Text ?? "";

            lines.Sort(delegate(LayoutLine first, LayoutLine second)
            {
                int vertical = first.Top.CompareTo(second.Top);
                return vertical != 0
                    ? vertical
                    : first.Left.CompareTo(second.Left);
            });
            double characterWidth = characterCount == 0
                ? 8
                : Math.Max(1, characterWidthSum / characterCount);
            double minimumLeft = double.MaxValue;
            double lineHeightSum = 0;
            int boundedLines = 0;
            foreach (LayoutLine line in lines)
            {
                if (line.Words.Count == 0) continue;
                minimumLeft = Math.Min(minimumLeft, line.Left);
                lineHeightSum += Math.Max(1, line.Bottom - line.Top);
                boundedLines++;
            }
            if (minimumLeft == double.MaxValue) minimumLeft = 0;
            double averageLineHeight = boundedLines == 0
                ? 1
                : lineHeightSum / boundedLines;

            var text = new StringBuilder();
            LayoutLine previous = null;
            foreach (LayoutLine line in lines)
            {
                if (previous != null)
                {
                    text.Append('\n');
                    double verticalGap = line.Top - previous.Bottom;
                    int blankLines = (int)Math.Floor(
                        Math.Max(0, verticalGap) /
                        Math.Max(1, averageLineHeight));
                    for (int blank = 0;
                        blank < Math.Min(2, blankLines);
                        blank++)
                        text.Append('\n');
                }
                text.Append(BuildLayoutLine(
                    line, minimumLeft, characterWidth));
                previous = line;
            }
            return text.ToString();
        }

        private static string BuildLayoutLine(
            LayoutLine line,
            double minimumLeft,
            double characterWidth)
        {
            if (line.Words.Count == 0)
                return line.FallbackText ?? "";

            line.Words.Sort(delegate(OcrWord first, OcrWord second)
            {
                return first.BoundingRect.X.CompareTo(
                    second.BoundingRect.X);
            });
            var result = new StringBuilder();
            double indent = (line.Left - minimumLeft) /
                Math.Max(1, characterWidth);
            if (indent >= 1.25)
                result.Append(
                    ' ', Math.Min(24, (int)Math.Round(indent)));

            OcrWord previous = null;
            foreach (OcrWord word in line.Words)
            {
                if (previous != null)
                {
                    double gap = word.BoundingRect.X -
                        (previous.BoundingRect.X +
                         previous.BoundingRect.Width);
                    int spaces = gap < characterWidth * .18
                        ? 0
                        : Math.Max(
                            1,
                            (int)Math.Round(
                                gap / Math.Max(1, characterWidth)));
                    if (spaces > 0)
                        result.Append(' ', Math.Min(12, spaces));
                }
                result.Append(word.Text ?? "");
                previous = word;
            }
            return result.ToString().TrimEnd();
        }

        private static string CleanupText(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            var result = new StringBuilder(text.Length);
            for (int i = 0; i < text.Length; i++)
            {
                char current = text[i];
                if (current == '\n')
                {
                    while (result.Length > 0 && result[result.Length - 1] == ' ')
                        result.Length--;
                    int existingNewlines = 0;
                    for (int tail = result.Length - 1;
                        tail >= 0 && result[tail] == '\n';
                        tail--)
                        existingNewlines++;
                    if (existingNewlines < 3)
                        result.Append('\n');
                    continue;
                }
                if (!char.IsWhiteSpace(current))
                {
                    result.Append(current);
                    continue;
                }

                int whitespaceEnd = i;
                while (whitespaceEnd + 1 < text.Length &&
                    text[whitespaceEnd + 1] != '\n' &&
                    text[whitespaceEnd + 1] != '\r' &&
                    char.IsWhiteSpace(text[whitespaceEnd + 1]))
                    whitespaceEnd++;
                int whitespaceCount = whitespaceEnd - i + 1;
                char previous = PreviousNonSpace(result);
                char next = NextNonSpace(text, whitespaceEnd + 1);
                bool lineStart = result.Length == 0 ||
                    result[result.Length - 1] == '\n';
                if (!lineStart &&
                    whitespaceCount == 1 &&
                    IsCjk(previous) && IsCjk(next))
                {
                    i = whitespaceEnd;
                    continue;
                }
                int spaces = lineStart
                    ? Math.Min(24, whitespaceCount)
                    : (whitespaceCount >= 2
                        ? Math.Min(12, whitespaceCount)
                        : 1);
                if (spaces > 0 &&
                    (lineStart ||
                     (result.Length > 0 &&
                      result[result.Length - 1] != ' ')))
                    result.Append(' ', spaces);
                i = whitespaceEnd;
            }
            return result.ToString()
                .TrimEnd()
                .TrimStart('\n', '\r');
        }

        private static char PreviousNonSpace(StringBuilder text)
        {
            for (int i = text.Length - 1; i >= 0; i--)
                if (!char.IsWhiteSpace(text[i])) return text[i];
            return '\0';
        }

        private static char NextNonSpace(string text, int start)
        {
            for (int i = start; i < text.Length; i++)
            {
                if (text[i] == '\n' || text[i] == '\r') return '\0';
                if (!char.IsWhiteSpace(text[i])) return text[i];
            }
            return '\0';
        }

        private static bool IsCjk(char value)
        {
            return (value >= '\u3400' && value <= '\u9FFF') ||
                   (value >= '\u3040' && value <= '\u30FF') ||
                   (value >= '\uAC00' && value <= '\uD7AF');
        }

        private static double PlausibilityScore(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return 0;
            int useful = 0;
            int bad = 0;
            int total = 0;
            foreach (char value in text)
            {
                if (char.IsWhiteSpace(value)) continue;
                total++;
                if (value == '\uFFFD' || char.IsControl(value) ||
                    (value >= '\uE000' && value <= '\uF8FF'))
                    bad++;
                else if (char.IsLetterOrDigit(value) || IsCjk(value) ||
                         char.IsPunctuation(value) || char.IsSymbol(value))
                    useful++;
            }
            if (total == 0) return 0;
            double score = useful / (double)total - bad / (double)total * .8;
            score += Math.Min(.12, total / 100.0 * .12);
            return Math.Max(0, Math.Min(1, score));
        }

        private static void ScoreAgreement(List<Candidate> candidates)
        {
            for (int i = 0; i < candidates.Count; i++)
            {
                double total = 0;
                int comparisons = 0;
                for (int j = 0; j < candidates.Count; j++)
                {
                    if (i == j || string.IsNullOrWhiteSpace(candidates[j].Text))
                        continue;
                    total += Similarity(candidates[i].Text, candidates[j].Text);
                    comparisons++;
                }
                candidates[i].Agreement = comparisons == 0
                    ? (string.IsNullOrWhiteSpace(candidates[i].Text) ? 0 : 1)
                    : total / comparisons;
                candidates[i].CombinedScore = Math.Min(
                    1, candidates[i].Plausibility * .8 +
                       candidates[i].Agreement * .2);
            }
        }

        private static double Similarity(string first, string second)
        {
            string a = NormalizeForComparison(first);
            string b = NormalizeForComparison(second);
            if (a.Length == 0 || b.Length == 0) return 0;
            int maximum = Math.Max(a.Length, b.Length);
            int distance = Levenshtein(a, b);
            return Math.Max(0, 1 - distance / (double)maximum);
        }

        private static string NormalizeForComparison(string text)
        {
            var result = new StringBuilder();
            foreach (char value in text ?? "")
            {
                if (char.IsWhiteSpace(value)) continue;
                result.Append(char.ToLowerInvariant(value));
                if (result.Length >= 1000) break;
            }
            return result.ToString();
        }

        private static int Levenshtein(string first, string second)
        {
            int[] previous = new int[second.Length + 1];
            int[] current = new int[second.Length + 1];
            for (int j = 0; j <= second.Length; j++) previous[j] = j;
            for (int i = 1; i <= first.Length; i++)
            {
                current[0] = i;
                for (int j = 1; j <= second.Length; j++)
                    current[j] = Math.Min(
                        Math.Min(current[j - 1] + 1, previous[j] + 1),
                        previous[j - 1] +
                        (first[i - 1] == second[j - 1] ? 0 : 1));
                int[] swap = previous;
                previous = current;
                current = swap;
            }
            return previous[second.Length];
        }

        private static Candidate BestCandidate(List<Candidate> candidates)
        {
            Candidate best = null;
            foreach (Candidate candidate in candidates)
                if (best == null || candidate.CombinedScore > best.CombinedScore ||
                    (Math.Abs(candidate.CombinedScore - best.CombinedScore) < .01 &&
                     candidate.Text.Length > best.Text.Length))
                    best = candidate;
            return best;
        }

        private static async Task<T> WaitAsync<T>(IAsyncOperation<T> operation)
        {
            while (operation.Status == AsyncStatus.Started)
                await Task.Delay(10);
            if (operation.Status == AsyncStatus.Error)
                throw operation.ErrorCode ??
                      new InvalidOperationException("Windows OCR 操作失败。");
            if (operation.Status == AsyncStatus.Canceled)
                throw new TaskCanceledException();
            return operation.GetResults();
        }

        private sealed class Candidate
        {
            public string Text = "";
            public double Plausibility;
            public double Agreement;
            public double CombinedScore;
        }

        private sealed class EnginePreview
        {
            public OcrEngine Engine;
            public string Text = "";
            public bool UserProfileEngine;
            public int UsefulCharacters;
            public double Score;
        }

        private sealed class LayoutLine
        {
            public readonly List<OcrWord> Words =
                new List<OcrWord>();
            public string FallbackText = "";
            public double Left;
            public double Top;
            public double Right;
            public double Bottom;
        }
    }
}
