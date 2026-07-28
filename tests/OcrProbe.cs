using System;
using Windows.Media.Ocr;

internal static class OcrProbe
{
    private static void Main()
    {
        Console.WriteLine("MaxDimension=" + OcrEngine.MaxImageDimension);
        foreach (var language in OcrEngine.AvailableRecognizerLanguages)
            Console.WriteLine(language.LanguageTag);
    }
}
