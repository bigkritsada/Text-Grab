﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using Text_Grab.Controls;
using Text_Grab.Models;
using Text_Grab.Properties;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;
using BitmapDecoder = Windows.Graphics.Imaging.BitmapDecoder;
using Point = System.Windows.Point;

namespace Text_Grab.Utilities;

public static class OcrExtensions
{

    public static void GetTextFromOcrLine(this OcrLine ocrLine, bool isSpaceJoiningOCRLang, StringBuilder text)
    {
        // (when OCR language is zh or ja)
        // matches words in a space-joining language, which contains:
        // - one letter that is not in "other letters" (CJK characters are "other letters")
        // - one number digit
        // - any words longer than one character
        // Chinese and Japanese characters are single-character words
        // when a word is one punctuation/symbol, join it without spaces

        if (isSpaceJoiningOCRLang)
        {
            text.AppendLine(ocrLine.Text);

            if (Settings.Default.CorrectErrors)
                text.TryFixEveryWordLetterNumberErrors();
        }
        else
        {
            bool isFirstWord = true;
            bool isPrevWordSpaceJoining = false;

            Regex regexSpaceJoiningWord = new(@"(^[\p{L}-[\p{Lo}]]|\p{Nd}$)|.{2,}");

            foreach (OcrWord ocrWord in ocrLine.Words)
            {
                string wordString = ocrWord.Text;

                bool isThisWordSpaceJoining = regexSpaceJoiningWord.IsMatch(wordString);

                if (Settings.Default.CorrectErrors)
                    wordString = wordString.TryFixNumberLetterErrors();

                if (isFirstWord || (!isThisWordSpaceJoining && !isPrevWordSpaceJoining))
                    _ = text.Append(wordString);
                else
                    _ = text.Append(' ').Append(wordString);

                isFirstWord = false;
                isPrevWordSpaceJoining = isThisWordSpaceJoining;
            }
        }

        if (Settings.Default.CorrectToLatin)
            text.ReplaceGreekOrCyrillicWithLatin();
    }

    public static async Task<string> GetRegionsText(Window passedWindow, Rectangle selectedRegion, Language language)
    {
        Point absPosPoint = passedWindow.GetAbsolutePosition();

        int thisCorrectedLeft = (int)absPosPoint.X + selectedRegion.Left;
        int thisCorrectedTop = (int)absPosPoint.Y + selectedRegion.Top;

        Rectangle correctedRegion = new(thisCorrectedLeft, thisCorrectedTop, selectedRegion.Width, selectedRegion.Height);
        Bitmap bmp = ImageMethods.GetRegionOfScreenAsBitmap(correctedRegion);

        // return await GetTextFromEntireBitmap(bmp, language);
        return await GetTextWithSpacesFromBitmap(bmp, language);
    }

    public static async Task<(OcrResult, double)> GetOcrResultFromRegion(Rectangle region, Language language)
    {
        Bitmap bmp = ImageMethods.GetRegionOfScreenAsBitmap(region);

        double scale = await GetIdealScaleFactorForOCR(bmp, language);
        using Bitmap scaledBitmap = ImageMethods.ScaleBitmapUniform(bmp, scale);

        OcrResult ocrResult = await GetOcrResultFromBitmap(scaledBitmap, language);

        return (ocrResult, scale);
    }

    public async static Task<OcrResult> GetOcrResultFromBitmap(Bitmap scaledBitmap, Language selectedLanguage)
    {
        await using MemoryStream memory = new();

        scaledBitmap.Save(memory, ImageFormat.Bmp);
        memory.Position = 0;
        BitmapDecoder bmpDecoder = await BitmapDecoder.CreateAsync(memory.AsRandomAccessStream());
        using SoftwareBitmap softwareBmp = await bmpDecoder.GetSoftwareBitmapAsync();

        await memory.FlushAsync();

        OcrEngine ocrEngine = OcrEngine.TryCreateFromLanguage(selectedLanguage);
        return await ocrEngine.RecognizeAsync(softwareBmp);
    }

    public async static Task<string> GetTextFromRandomAccessStream(IRandomAccessStream randomAccessStream, Language language)
    {
        BitmapDecoder bmpDecoder = await BitmapDecoder.CreateAsync(randomAccessStream);
        using SoftwareBitmap softwareBmp = await bmpDecoder.GetSoftwareBitmapAsync();

        OcrEngine ocrEngine = OcrEngine.TryCreateFromLanguage(language);
        OcrResult ocrResult = await ocrEngine.RecognizeAsync(softwareBmp);

        StringBuilder stringBuilder = new();

        foreach (OcrLine line in ocrResult.Lines)
            line.GetTextFromOcrLine(LanguageUtilities.IsLanguageSpaceJoining(language), stringBuilder);

        return stringBuilder.ToString();
    }

    public async static Task<string> GetTextFromEntireBitmap(Bitmap bitmap, Language language)
    {
        double scale = await GetIdealScaleFactorForOCR(bitmap, language);
        Bitmap scaledBitmap = ImageMethods.ScaleBitmapUniform(bitmap, scale);

        StringBuilder text = new();

        OcrResult ocrResult = await OcrExtensions.GetOcrResultFromBitmap(scaledBitmap, language);
        bool isSpaceJoiningOCRLang = LanguageUtilities.IsLanguageSpaceJoining(language);

        foreach (OcrLine ocrLine in ocrResult.Lines)
            ocrLine.GetTextFromOcrLine(isSpaceJoiningOCRLang, text);

        if (LanguageUtilities.IsLanguageRightToLeft(language))
            text.ReverseWordsForRightToLeft();

        if (Settings.Default.TryToReadBarcodes)
        {
            string barcodeResult = BarcodeUtilities.TryToReadBarcodes(scaledBitmap);

            if (!string.IsNullOrWhiteSpace(barcodeResult))
                text.AppendLine(barcodeResult);
        }

        return text.ToString();
    }

    public async static Task<string> GetTextWithSpacesFromBitmap(Bitmap bitmap, Language language)
    {
        double scale = await GetIdealScaleFactorForOCR(bitmap, language);
        Bitmap scaledBitmap = ImageMethods.ScaleBitmapUniform(bitmap, scale);

        StringBuilder text = new();

        OcrResult ocrResult = await OcrExtensions.GetOcrResultFromBitmap(scaledBitmap, language);
        bool isSpaceJoiningOCRLang = LanguageUtilities.IsLanguageSpaceJoining(language);

        ResultTable resultTable = new();
        List<WordBorder> wordBorders = ResultTable.ParseOcrResultIntoWordBorders(ocrResult, new DpiScale(1, 1));
        Rectangle bitmapBounds = new(0, 0, bitmap.Width, bitmap.Height);
        resultTable.AnalyzeAsTable(wordBorders, bitmapBounds);

        return GetTextWithSpacesFromTabledWordBorders(wordBorders);
    }

    public static string GetTextWithSpacesFromTabledWordBorders(List<WordBorder> wordBorders)
    {
        StringBuilder stringBuilder = new();
        List<IGrouping<int, WordBorder>> wordsGroupedByRow = wordBorders.GroupBy(x => x.ResultRowID).ToList();

        double leftMarginStart = wordBorders.Min(x => x.Left);

        List<double> pixelCharWidths = new();
        foreach (WordBorder wbr in wordBorders)
            pixelCharWidths.Add(wbr.AverageCharPixelWidth());
        double averageCharWidth = pixelCharWidths.Average();

        foreach (IGrouping<int, WordBorder> Group in wordsGroupedByRow)
        {
            double distToLeft = leftMarginStart;

            foreach (WordBorder wb in Group.OrderBy(x => x.Left))
            {
                int numberOfSpaces = (int)((wb.Left - distToLeft) / averageCharWidth);
                if (numberOfSpaces < 1)
                    numberOfSpaces = 0;

                if (numberOfSpaces < 4)
                    stringBuilder.Append(new string(' ', numberOfSpaces)).Append(wb.Word);
                else
                    stringBuilder.Append(new string('\t', numberOfSpaces / 4)).Append(wb.Word);
                distToLeft = wb.Left + wb.Width;
            }
            stringBuilder.Append(Environment.NewLine);
        }

        return stringBuilder.ToString();
    }

    public static async Task<string> OcrAbsoluteFilePath(string absolutePath)
    {
        Uri fileURI = new(absolutePath, UriKind.Absolute);
        BitmapImage droppedImage = new(fileURI);
        droppedImage.Freeze();
        Bitmap bmp = ImageMethods.BitmapImageToBitmap(droppedImage);
        Language language = LanguageUtilities.GetOCRLanguage();
        return await GetTextFromEntireBitmap(bmp, language);
    }

    public static async Task<string> GetClickedWord(Window passedWindow, Point clickedPoint, Language OcrLang)
    {
        using Bitmap bmp = ImageMethods.GetWindowsBoundsBitmap(passedWindow);
        string ocrText = await GetTextFromClickedWord(clickedPoint, bmp, OcrLang);
        return ocrText.Trim();
    }

    private static async Task<string> GetTextFromClickedWord(Point singlePoint, Bitmap bitmap, Language language)
    {
        return GetTextFromClickedWord(singlePoint, await OcrExtensions.GetOcrResultFromBitmap(bitmap, language));
    }

    private static string GetTextFromClickedWord(Point singlePoint, OcrResult ocrResult)
    {
        Windows.Foundation.Point fPoint = new(singlePoint.X, singlePoint.Y);

        foreach (OcrLine ocrLine in ocrResult.Lines)
            foreach (OcrWord ocrWord in ocrLine.Words)
                if (ocrWord.BoundingRect.Contains(fPoint))
                    return ocrWord.Text;

        return string.Empty;
    }

    public async static Task<double> GetIdealScaleFactorForOCR(Bitmap bitmap, Language selectedLanguage)
    {
        List<double> heightsList = new();
        double scaleFactor = 1.5;

        OcrResult ocrResult = await OcrExtensions.GetOcrResultFromBitmap(bitmap, selectedLanguage);

        foreach (OcrLine ocrLine in ocrResult.Lines)
            foreach (OcrWord ocrWord in ocrLine.Words)
                heightsList.Add(ocrWord.BoundingRect.Height);

        double lineHeight = 10;

        if (heightsList.Count > 0)
            lineHeight = heightsList.Average();

        // Ideal Line Height is 40px
        const double idealLineHeight = 40.0;

        scaleFactor = idealLineHeight / lineHeight;

        if (bitmap.Width * scaleFactor > OcrEngine.MaxImageDimension || bitmap.Height * scaleFactor > OcrEngine.MaxImageDimension)
        {
            int largerDim = Math.Max(bitmap.Width, bitmap.Height);
            // find the largest possible scale factor, because the ideal scale factor is too high

            scaleFactor = OcrEngine.MaxImageDimension / largerDim;
        }

        return scaleFactor;
    }

    public static Rect GetBoundingRect(this OcrLine ocrLine)
    {
        double top = ocrLine.Words.Select(x => x.BoundingRect.Top).Min();
        double bottom = ocrLine.Words.Select(x => x.BoundingRect.Bottom).Max();
        double left = ocrLine.Words.Select(x => x.BoundingRect.Left).Min();
        double right = ocrLine.Words.Select(x => x.BoundingRect.Right).Max();

        return new()
        {
            X = left,
            Y = top,
            Width = Math.Abs(right - left),
            Height = Math.Abs(bottom - top)
        };
    }

}
