using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlobalTranslator
{
    internal static class ClipboardImages
    {
        internal static bool HasImage(IDataObject data)
        {
            if (data == null) return false;
            var paths = data.GetData(DataFormats.FileDrop) as string[];
            bool imageFile = paths != null && Array.Exists(paths, path =>
                string.Equals(Path.GetExtension(path), ".png", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(path), ".jpg", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetExtension(path), ".jpeg", StringComparison.OrdinalIgnoreCase));
            return data.GetDataPresent("PNG") ||
                data.GetDataPresent("image/png") || data.GetDataPresent(DataFormats.Bitmap) ||
                imageFile;
        }

        internal static byte[] Encode(BitmapSource source, bool repairBitmapAlpha)
        {
            if (source == null || source.PixelWidth < 1 || source.PixelHeight < 1)
                throw new InvalidDataException("无法读取图片，请重新复制截图。");
            if ((long)source.PixelWidth * source.PixelHeight > 50000000)
                throw new InvalidDataException("图片尺寸过大，请缩小截图区域。");
            // Windows CF_BITMAP may have an unused, entirely zero alpha channel.
            // Preserve real transparency in PNG; repair only this bitmap fallback.
            if (repairBitmapAlpha && source.Format == PixelFormats.Bgra32)
            {
                int stride = checked(source.PixelWidth * 4);
                var pixels = new byte[checked(stride * source.PixelHeight)];
                source.CopyPixels(pixels, stride, 0);
                bool allZero = true;
                for (int i = 3; i < pixels.Length; i += 4)
                    if (pixels[i] != 0) { allZero = false; break; }
                if (allZero)
                {
                    for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;
                    source = BitmapSource.Create(source.PixelWidth, source.PixelHeight,
                        96, 96, PixelFormats.Bgra32, null, pixels, stride);
                }
            }
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var output = new MemoryStream())
            { encoder.Save(output); return output.ToArray(); }
        }

        internal static List<byte[]> Read(IDataObject data)
        {
            var result = new List<byte[]>();
            if (data == null) return result;
            foreach (string format in new[] { "PNG", "image/png" })
            {
                if (!data.GetDataPresent(format)) continue;
                object value = data.GetData(format);
                byte[] bytes = value as byte[];
                var input = value as Stream;
                if (input != null)
                {
                    long position = input.CanSeek ? input.Position : 0;
                    try
                    {
                        if (input.CanSeek) input.Position = 0;
                        using (var copy = new MemoryStream())
                        { input.CopyTo(copy); bytes = copy.ToArray(); }
                    }
                    finally { if (input.CanSeek) input.Position = position; }
                }
                if (bytes == null) continue;
                using (var stream = new MemoryStream(bytes))
                    result.Add(Encode(BitmapFrame.Create(stream,
                        BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad), false));
                return result;
            }
            var bitmap = data.GetData(DataFormats.Bitmap) as BitmapSource;
            if (bitmap != null) { result.Add(Encode(bitmap, true)); return result; }
            var paths = data.GetData(DataFormats.FileDrop) as string[];
            if (paths != null)
                foreach (string path in paths)
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension != ".png" && extension != ".jpg" && extension != ".jpeg") continue;
                    if (new FileInfo(path).Length > 10 * 1024 * 1024)
                        throw new InvalidDataException("单张图片需小于 10 MB。");
                    using (var stream = File.OpenRead(path))
                        result.Add(Encode(BitmapFrame.Create(stream,
                            BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad), false));
                    if (result.Count == 5) break;
                }
            return result;
        }
    }
}
