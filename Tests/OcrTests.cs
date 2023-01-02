using System.Drawing;
using System.Text;
using System.Windows;
using Text_Grab;
using Text_Grab.Controls;
using Text_Grab.Models;
using Text_Grab.Utilities;
using Windows.Globalization;
using Windows.Media.Ocr;

namespace Tests;

public class OcrTests
{
    [Fact]
    public async Task OcrFontSampleImage()
    {
        // Given
        string expectedResult = @"Times-Roman
Helvetica
Courier
Palatino-Roman
Helvetica-Narrow
Bookman-Demi
";

        string testImagePath = @".\Images\font_sample.png";

        // When
        string ocrTextResult = await OcrExtensions.OcrAbsoluteFilePath(getPathToImages(testImagePath));

        // Then
        Assert.Equal(expectedResult, ocrTextResult);
    }

    [Fact]
    public async Task OcrFontTestImage()
    {
        // Given
        string expectedResult = @"Arial
Times New Roman
Georgia
Segoe
Rockwell Condensed
Couier New
";

        string testImagePath = @".\Images\FontTest.png";
        Uri uri = new Uri(testImagePath, UriKind.Relative);
        // When
        string ocrTextResult = await OcrExtensions.OcrAbsoluteFilePath(getPathToImages(testImagePath));

        // Then
        Assert.Equal(expectedResult, ocrTextResult);
    }

    [WpfFact]
    public async Task AnalyzeTable()
    {
        string expectedResult = @"Month	Int	Season
January	1	Winter
February	2	Winter
March	3	Spring
April	4	Spring
May	5	Spring
June	6	Summer
July	7	Summer
August	8	Summer
September	9	Fall
October	10	Fall
November	11	Fall
December	12	Winter";


        string testImagePath = @".\Images\Table-Test.png";
        Uri uri = new Uri(testImagePath, UriKind.Relative);
        Language englishLanguage = new("en-US");
        Bitmap testBitmap = new(getPathToImages(testImagePath));
        // When
        OcrResult ocrResult = await OcrExtensions.GetOcrResultFromBitmap(testBitmap, englishLanguage);

        DpiScale dpi = new(1, 1);
        Rectangle rectCanvasSize = new()
        {
            Width = 1132,
            Height = 1158,
            X = 0,
            Y = 0
        };

        List<WordBorder> wordBorders = ResultTable.ParseOcrResultIntoWordBorders(ocrResult, dpi);

        ResultTable resultTable = new();
        resultTable.AnalyzeAsTable(wordBorders, rectCanvasSize);

        StringBuilder stringBuilder = new();

        ResultTable.GetTextFromTabledWordBorders(stringBuilder, wordBorders, true);

        // Then
        Assert.Equal(expectedResult, stringBuilder.ToString());

    }

    [WpfFact]
    public async Task AnalyzeTable2()
    {
        string expectedResult = @"Test	Text
12	The Quick Brown Fox
13	Jumped over the
14	Lazy
15
20
200
300	Brown
400	Dog";

        string testImagePath = @".\Images\Table-Test-2.png";
        Uri uri = new Uri(testImagePath, UriKind.Relative);
        Language englishLanguage = new("en-US");
        Bitmap testBitmap = new(getPathToImages(testImagePath));
        // When
        OcrResult ocrResult = await OcrExtensions.GetOcrResultFromBitmap(testBitmap, englishLanguage);

        DpiScale dpi = new(1, 1);
        Rectangle rectCanvasSize = new()
        {
            Width = 1152,
            Height = 1132,
            X = 0,
            Y = 0
        };

        List<WordBorder> wordBorders = ResultTable.ParseOcrResultIntoWordBorders(ocrResult, dpi);

        ResultTable resultTable = new();
        resultTable.AnalyzeAsTable(wordBorders, rectCanvasSize);

        StringBuilder stringBuilder = new();

        ResultTable.GetTextFromTabledWordBorders(stringBuilder, wordBorders, true);

        // Then
        Assert.Equal(expectedResult, stringBuilder.ToString());
    }

    [WpfFact]
    public async Task ReadTableOfContentsWithSpaces()
    {
        string expectedResult = @"								Contents
Introduction to the Tenth Anniversary Edition				page xvii
Afterword to the Tenth Anniversary Edition					xix
Preface										xxi
Acknowledgements									xxvii
Nomenclature and notation								xxix
Part I Fundamental concepts								1
1 Introduction and overview								1
   1.1 Global perspectives
	I.I.I Ilistory of quantum computation and quantum
		information								2
	l. 1.2 Future directions							12
   1.2 Quantum bits									13
	1.2. I Nlultiple qubits							16
   1.3 Quantum computation								17
	1.3.1 Single qubit gates							17
	1.3.2 Multiple qubit gates							20
	1.3.3 Measurements in bases other than the computational basis	22
	1..3.4 Quantum circuits							22
	1.3.5 Qubit copying circuit?						24";

        string testImagePath = @".\Images\toc.png";
        Language englishLanguage = new("en-US");
        Bitmap testBitmap = new(getPathToImages(testImagePath));
        double idealScaleFactor = await OcrExtensions.GetIdealScaleFactorForOCR(testBitmap, englishLanguage);
        Bitmap scaledBitmap = ImageMethods.ScaleBitmapUniform(testBitmap, idealScaleFactor);
        string actualString = await OcrExtensions.GetTextWithSpacesFromBitmap(scaledBitmap, englishLanguage);

        Assert.Equal(expectedResult, actualString);
    }

    private string getPathToImages(string imageRelativePath)
    {
        Uri codeBaseUrl = new(System.AppDomain.CurrentDomain.BaseDirectory);
        string codeBasePath = Uri.UnescapeDataString(codeBaseUrl.AbsolutePath);
        string? dirPath = Path.GetDirectoryName(codeBasePath);

        if (dirPath is null)
            dirPath = "";

        return Path.Combine(dirPath, imageRelativePath);
    }
}