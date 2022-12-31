﻿using Fasetto.Word;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Text_Grab.Controls;
using Text_Grab.Models;
using Text_Grab.Properties;
using Text_Grab.Utilities;
using Windows.Globalization;
using Windows.Media.Ocr;
using ZXing;
using ZXing.Windows.Compatibility;

namespace Text_Grab.Views;

/// <summary>
/// Interaction logic for PersistentWindow.xaml
/// </summary>
public partial class GrabFrame : Window
{
    private bool isDrawing = false;
    private OcrResult? ocrResultOfWindow;
    private ObservableCollection<WordBorder> wordBorders = new();
    private DispatcherTimer reSearchTimer = new();
    private DispatcherTimer reDrawTimer = new();
    private bool isSelecting;
    private Point clickedPoint;
    private Border selectBorder = new();

    private ImageSource? frameContentImageSource;

    private ImageSource? droppedImageSource;

    private bool isSpaceJoining = true;

    private ResultTable? AnalyedResultTable;

    public bool IsFromEditWindow
    {
        get
        {
            return destinationTextBox is not null;
        }
    }

    public bool IsWordEditMode { get; set; } = true;

    public bool IsFreezeMode { get; set; } = false;

    private bool IsDragOver = false;

    private bool wasAltHeld = false;

    private bool isLanguageBoxLoaded = false;

    public static RoutedCommand PasteCommand = new();

    public string FrameText { get; private set; } = string.Empty;

    private TextBox? destinationTextBox;

    public TextBox? DestinationTextBox
    {
        get { return destinationTextBox; }
        set
        {
            destinationTextBox = value;
            if (destinationTextBox is not null)
                EditTextToggleButton.IsChecked = true;
        }
    }


    public GrabFrame()
    {
        InitializeComponent();

        LoadOcrLanguages();

        SetRestoreState();

        WindowResizer resizer = new(this);
        reDrawTimer.Interval = new(0, 0, 0, 0, 500);
        reDrawTimer.Tick += ReDrawTimer_Tick;
        reDrawTimer.Start();

        reSearchTimer.Interval = new(0, 0, 0, 0, 300);
        reSearchTimer.Tick += ReSearchTimer_Tick;

        RoutedCommand newCmd = new();
        _ = newCmd.InputGestures.Add(new KeyGesture(Key.Escape));
        _ = CommandBindings.Add(new CommandBinding(newCmd, Escape_Keyed));
    }

    public void GrabFrame_Loaded(object sender, RoutedEventArgs e)
    {
        this.PreviewMouseWheel += HandlePreviewMouseWheel;
        this.PreviewKeyDown += Window_PreviewKeyDown;
        this.PreviewKeyUp += Window_PreviewKeyUp;

        RoutedCommand pasteCommand = new();
        _ = pasteCommand.InputGestures.Add(new KeyGesture(Key.V, ModifierKeys.Control | ModifierKeys.Shift));
        _ = CommandBindings.Add(new CommandBinding(pasteCommand, PasteExecuted));

        CheckBottomRowButtonsVis();
    }

    public void GrabFrame_Unloaded(object sender, RoutedEventArgs e)
    {
        this.Activated -= GrabFrameWindow_Activated;
        this.Closed -= Window_Closed;
        this.Deactivated -= GrabFrameWindow_Deactivated;
        this.DragLeave -= GrabFrameWindow_DragLeave;
        this.DragOver -= GrabFrameWindow_DragOver;
        this.Loaded -= GrabFrame_Loaded;
        this.LocationChanged -= Window_LocationChanged;
        this.SizeChanged -= Window_SizeChanged;
        this.Unloaded -= GrabFrame_Unloaded;
        this.PreviewMouseWheel -= HandlePreviewMouseWheel;
        this.PreviewKeyDown -= Window_PreviewKeyDown;
        this.PreviewKeyUp -= Window_PreviewKeyUp;

        reDrawTimer.Stop();
        reDrawTimer.Tick -= ReDrawTimer_Tick;

        MinimizeButton.Click -= OnMinimizeButtonClick;
        RestoreButton.Click -= OnRestoreButtonClick;
        CloseButton.Click -= OnCloseButtonClick;

        RectanglesCanvas.MouseDown -= RectanglesCanvas_MouseDown;
        RectanglesCanvas.MouseMove -= RectanglesCanvas_MouseMove;
        RectanglesCanvas.MouseUp -= RectanglesCanvas_MouseUp;

        AspectRationMI.Checked -= AspectRationMI_Checked;
        AspectRationMI.Unchecked -= AspectRationMI_Checked;
        FreezeMI.Click -= FreezeMI_Click;

        SearchBox.GotFocus -= SearchBox_GotFocus;
        SearchBox.TextChanged -= SearchBox_TextChanged;

        ClearBTN.Click -= ClearBTN_Click;
        ExactMatchChkBx.Click -= ExactMatchChkBx_Click;

        RefreshBTN.Click -= RefreshBTN_Click;
        FreezeToggleButton.Click -= FreezeToggleButton_Click;
        TableToggleButton.Click -= TableToggleButton_Click;
        EditToggleButton.Click -= EditToggleButton_Click;
        SettingsBTN.Click -= SettingsBTN_Click;
        EditTextToggleButton.Click -= EditTextBTN_Click;
        GrabBTN.Click -= GrabBTN_Click;
    }

    private void Window_PreviewKeyUp(object sender, KeyEventArgs e)
    {
        if (wasAltHeld && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt))
        {
            RectanglesCanvas.Opacity = 1;
            wasAltHeld = false;
        }
    }

    private void CanPasteExecute(object sender, CanExecuteRoutedEventArgs e)
    {
        if (System.Windows.Clipboard.ContainsImage())
        {
            e.CanExecute = true;
            return;
        }

        e.CanExecute = false;
    }

    private async void PasteExecuted(object sender, ExecutedRoutedEventArgs? e = null)
    {
        (bool success, ImageSource? clipboardImage) = ClipboardUtilities.TryGetImageFromClipboard();

        if (!success || clipboardImage is null)
            return;

        reDrawTimer.Stop();
        droppedImageSource = null;

        ResetGrabFrame();
        await Task.Delay(300);

        droppedImageSource = clipboardImage;
        FreezeToggleButton.IsChecked = true;
        FreezeGrabFrame();

        reDrawTimer.Start();
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (!wasAltHeld && (e.SystemKey == Key.LeftAlt || e.SystemKey == Key.RightAlt))
        {
            RectanglesCanvas.Opacity = 0.1;
            wasAltHeld = true;
        }
    }

    private void HandlePreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        // Source: StackOverflow, read on Sep. 10, 2021
        // https://stackoverflow.com/a/53698638/7438031

        if (this.WindowState == WindowState.Maximized)
            return;

        e.Handled = true;
        ResetGrabFrame();
        double aspectRatio = (this.Height - 66) / (this.Width - 4);

        bool isShiftDown = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift);
        bool isCtrlDown = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

        if (e.Delta > 0)
        {
            this.Width += 100;
            this.Left -= 50;

            if (!isShiftDown)
            {
                this.Height += 100 * aspectRatio;
                this.Top -= 50 * aspectRatio;
            }
        }
        else if (e.Delta < 0)
        {
            if (this.Width > 120 && this.Height > 120)
            {
                this.Width -= 100;
                this.Left += 50;

                if (!isShiftDown)
                {
                    this.Height -= 100 * aspectRatio;
                    this.Top += 50 * aspectRatio;
                }
            }
        }
    }

    private void GrabFrameWindow_Initialized(object sender, EventArgs e)
    {
        WindowUtilities.SetWindowPosition(this);
        CheckBottomRowButtonsVis();
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        string windowSizeAndPosition = $"{this.Left},{this.Top},{this.Width},{this.Height}";
        Properties.Settings.Default.GrabFrameWindowSizeAndPosition = windowSizeAndPosition;
        Properties.Settings.Default.Save();

        WindowUtilities.ShouldShutDown();
    }

    private void Escape_Keyed(object sender, ExecutedRoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(SearchBox.Text) && SearchBox.Text != "Search For Text...")
            SearchBox.Text = "";
        else if (RectanglesCanvas.Children.Count > 0)
            ResetGrabFrame();
        else
            Close();
    }

    private async void ReDrawTimer_Tick(object? sender, EventArgs? e)
    {
        reDrawTimer.Stop();
        ResetGrabFrame();

        frameContentImageSource = ImageMethods.GetWindowBoundsImage(this);
        if (SearchBox.Text is string searchText)
            await DrawRectanglesAroundWords(searchText);
    }

    private void OnCloseButtonClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void GrabBTN_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(FrameText))
            return;

        if (destinationTextBox is not null)
        {
            destinationTextBox.Select(destinationTextBox.SelectionStart + destinationTextBox.SelectionLength, 0);
            destinationTextBox.AppendText(Environment.NewLine);
            UpdateFrameText();
            return;
        }

        if (!Settings.Default.NeverAutoUseClipboard)
            try { Clipboard.SetDataObject(FrameText, true); } catch { }

        if (Settings.Default.ShowToast)
            NotificationUtilities.ShowToast(FrameText);
    }

    private void ResetGrabFrame()
    {
        ocrResultOfWindow = null;
        frameContentImageSource = null;
        RectanglesCanvas.Children.Clear();
        wordBorders.Clear();
        MatchesTXTBLK.Text = "Matches: 0";
        UpdateFrameText();
    }

    private void Window_LocationChanged(object? sender, EventArgs e)
    {
        if (!IsLoaded || IsFreezeMode || isMiddleDown)
            return;

        ResetGrabFrame();

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void Window_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        ResetGrabFrame();
        CheckBottomRowButtonsVis();
        SetRestoreState();

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void CheckBottomRowButtonsVis()
    {
        if (this.Width < 340)
            ButtonsStackPanel.Visibility = Visibility.Collapsed;
        else
            ButtonsStackPanel.Visibility = Visibility.Visible;

        if (this.Width < 460)
        {
            SearchBox.Visibility = Visibility.Collapsed;
            MatchesTXTBLK.Visibility = Visibility.Collapsed;
            ClearBTN.Visibility = Visibility.Collapsed;
        }
        else
        {
            SearchBox.Visibility = Visibility.Visible;
            ClearBTN.Visibility = Visibility.Visible;
        }

        if (this.Width < 580)
            LanguagesComboBox.Visibility = Visibility.Collapsed;
        else
            LanguagesComboBox.Visibility = Visibility.Visible;
    }

    private void GrabFrameWindow_Deactivated(object? sender, EventArgs e)
    {
        if (!IsWordEditMode && !IsFreezeMode)
            ResetGrabFrame();
        else
        {
            RectanglesCanvas.Opacity = 1;
            if (Keyboard.Modifiers != ModifierKeys.Alt)
                wasAltHeld = false;

            if (!IsFreezeMode)
                FreezeGrabFrame();
        }

    }

    private async Task DrawRectanglesAroundWords(string searchWord = "")
    {
        if (isDrawing || IsDragOver)
            return;

        isDrawing = true;

        if (string.IsNullOrWhiteSpace(searchWord))
            searchWord = SearchBox.Text;

        RectanglesCanvas.Children.Clear();
        wordBorders.Clear();

        Point windowPosition = this.GetAbsolutePosition();
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        System.Drawing.Rectangle rectCanvasSize = new System.Drawing.Rectangle
        {
            Width = (int)((ActualWidth + 2) * dpi.DpiScaleX),
            Height = (int)((ActualHeight - 64) * dpi.DpiScaleY),
            X = (int)((windowPosition.X - 2) * dpi.DpiScaleX),
            Y = (int)((windowPosition.Y + 24) * dpi.DpiScaleY)
        };

        double scale = 1;
        Language? currentLang = LanguagesComboBox.SelectedItem as Language;
        if (currentLang is null)
            currentLang = LanguageUtilities.GetOCRLanguage();

        if (ocrResultOfWindow is null || ocrResultOfWindow.Lines.Count == 0)
            (ocrResultOfWindow, scale) = await OcrExtensions.GetOcrResultFromRegion(rectCanvasSize, currentLang);

        if (ocrResultOfWindow is null)
            return;

        isSpaceJoining = LanguageUtilities.IsLanguageSpaceJoining(currentLang);

        System.Drawing.Bitmap? bmp = null;

        if (frameContentImageSource is BitmapImage bmpImg)
            bmp = ImageMethods.BitmapSourceToBitmap(bmpImg);

        int numberOfMatches = 0;
        int lineNumber = 0;

        foreach (OcrLine ocrLine in ocrResultOfWindow.Lines)
        {
            StringBuilder lineText = new();
            ocrLine.GetTextFromOcrLine(isSpaceJoining, lineText);

            Rect lineRect = ocrLine.GetBoundingRect();

            SolidColorBrush backgroundBrush = new(Colors.Black);

            if (bmp is not null)
                backgroundBrush = GetBackgroundBrushFromBitmap(ref dpi, scale, bmp, ref lineRect);

            WordBorder wordBorderBox = new()
            {
                Width = lineRect.Width / (dpi.DpiScaleX * scale),
                Height = lineRect.Height / (dpi.DpiScaleY * scale),
                Top = lineRect.Y,
                Left = lineRect.X,
                Word = lineText.ToString().Trim(),
                ToolTip = ocrLine.Text,
                LineNumber = lineNumber,
                IsFromEditWindow = IsFromEditWindow,
                MatchingBackground = backgroundBrush,
            };

            if ((bool)ExactMatchChkBx.IsChecked!)
            {
                if (lineText.ToString().Equals(searchWord, StringComparison.CurrentCulture))
                {
                    wordBorderBox.Select();
                    numberOfMatches++;
                }
            }
            else
            {
                if (!string.IsNullOrWhiteSpace(searchWord)
                    && lineText.ToString().Contains(searchWord, StringComparison.CurrentCultureIgnoreCase))
                {
                    wordBorderBox.Select();
                    numberOfMatches++;
                }
            }
            wordBorders.Add(wordBorderBox);
            _ = RectanglesCanvas.Children.Add(wordBorderBox);
            wordBorderBox.Left = lineRect.Left / (dpi.DpiScaleX * scale);
            wordBorderBox.Top = lineRect.Top / (dpi.DpiScaleY * scale);
            Canvas.SetLeft(wordBorderBox, wordBorderBox.Left);
            Canvas.SetTop(wordBorderBox, wordBorderBox.Top);

            lineNumber++;
        }

        if (ocrResultOfWindow != null && ocrResultOfWindow.TextAngle != null)
        {
            RotateTransform transform = new((double)ocrResultOfWindow.TextAngle)
            {
                CenterX = (Width - 4) / 2,
                CenterY = (Height - 60) / 2
            };
            RectanglesCanvas.RenderTransform = transform;
        }
        else
        {
            RotateTransform transform = new(0)
            {
                CenterX = (Width - 4) / 2,
                CenterY = (Height - 60) / 2
            };
            RectanglesCanvas.RenderTransform = transform;
        }

        // if (TableToggleButton.IsChecked is true && ocrResultOfWindow is not null)
        if (ocrResultOfWindow is not null)
        {
            try
            {
                AnalyedResultTable = new();
                AnalyedResultTable.AnalyzeAsTable(wordBorders.ToList(), rectCanvasSize);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        if (Settings.Default.TryToReadBarcodes)
            TryToReadBarcodes(dpi);

        List<WordBorder> wordBordersRePlace = new();
        foreach (UIElement child in RectanglesCanvas.Children)
        {
            if (child is WordBorder wordBorder)
                wordBordersRePlace.Add(wordBorder);
        }
        RectanglesCanvas.Children.Clear();
        foreach (WordBorder wordBorder in wordBordersRePlace)
        {
            // First the Word borders are placed smaller, then table analysis occurs.
            // After table can be analyzed with the position of the word borders they are adjusted 
            wordBorder.Width += 16;
            wordBorder.Height += 4;
            double leftWB = Canvas.GetLeft(wordBorder);
            double topWB = Canvas.GetTop(wordBorder);
            Canvas.SetLeft(wordBorder, leftWB - 10);
            Canvas.SetTop(wordBorder, topWB - 2);
            RectanglesCanvas.Children.Add(wordBorder);
        }

        if (TableToggleButton.IsChecked is true
            && AnalyedResultTable is not null
            && AnalyedResultTable.TableLines is not null)
        {
            RectanglesCanvas.Children.Add(AnalyedResultTable.TableLines);
        }

        if (IsWordEditMode)
            EnterEditMode();

        MatchesTXTBLK.Text = $"Matches: {numberOfMatches}";
        isDrawing = false;

        UpdateFrameText();
        bmp?.Dispose();
    }

    private SolidColorBrush GetBackgroundBrushFromBitmap(ref DpiScale dpi, double scale, System.Drawing.Bitmap bmp, ref Rect lineRect)
    {
        SolidColorBrush backgroundBrush = new(Colors.Black);
        double pxToRectanglesFactor = (RectanglesCanvas.ActualWidth / bmp.Width) * dpi.DpiScaleX;
        double boxLeft = lineRect.Left / (dpi.DpiScaleX * scale);
        double boxTop = lineRect.Top / (dpi.DpiScaleY * scale);
        double boxRight = lineRect.Right / (dpi.DpiScaleX * scale);
        double boxBottom = lineRect.Bottom / (dpi.DpiScaleY * scale);

        double leftFraction = boxLeft / RectanglesCanvas.ActualWidth;
        double topFraction = boxTop / RectanglesCanvas.ActualHeight;
        double rightFraction = boxRight / RectanglesCanvas.ActualWidth;
        double bottomFraction = boxBottom / RectanglesCanvas.ActualHeight;

        int pxLeft = Math.Clamp((int)(leftFraction * bmp.Width) - 1, 0, bmp.Width - 1);
        int pxTop = Math.Clamp((int)(topFraction * bmp.Height) - 2, 0, bmp.Height - 1);
        int pxRight = Math.Clamp((int)(rightFraction * bmp.Width) + 1, 0, bmp.Width - 1);
        int pxBottom = Math.Clamp((int)(bottomFraction * bmp.Height) + 1, 0, bmp.Height - 1);
        System.Drawing.Color pxColorLeftTop = bmp.GetPixel(pxLeft, pxTop);
        System.Drawing.Color pxColorRightTop = bmp.GetPixel(pxRight, pxTop);
        System.Drawing.Color pxColorRightBottom = bmp.GetPixel(pxRight, pxBottom);
        System.Drawing.Color pxColorLeftBottom = bmp.GetPixel(pxLeft, pxBottom);

        List<System.Windows.Media.Color> MediaColorList = new()
        {
            ColorHelper.MediaColorFromDrawingColor(pxColorLeftTop),
            ColorHelper.MediaColorFromDrawingColor(pxColorRightTop),
            ColorHelper.MediaColorFromDrawingColor(pxColorRightBottom),
            ColorHelper.MediaColorFromDrawingColor(pxColorLeftBottom),
        };

        System.Windows.Media.Color? MostCommonColor = MediaColorList.GroupBy(c => c)
                                                                    .OrderBy(g => g.Count())
                                                                    .LastOrDefault()?.Key;

        backgroundBrush = ColorHelper.SolidColorBrushFromDrawingColor(pxColorLeftTop);

        if (MostCommonColor is not null)
            backgroundBrush = new SolidColorBrush(MostCommonColor.Value);

        return backgroundBrush;
    }

    private void TryToReadBarcodes(DpiScale dpi)
    {
        System.Drawing.Bitmap bitmapOfGrabFrame = ImageMethods.GetWindowsBoundsBitmap(this);

        BarcodeReader barcodeReader = new()
        {
            AutoRotate = true,
            Options = new ZXing.Common.DecodingOptions { TryHarder = true }
        };

        ZXing.Result result = barcodeReader.Decode(bitmapOfGrabFrame);

        if (result is not null)
        {
            ResultPoint[] rawPoints = result.ResultPoints;

            float[] xs = rawPoints.Reverse().Take(4).Select(x => x.X).ToArray();
            float[] ys = rawPoints.Reverse().Take(4).Select(x => x.Y).ToArray();

            Point minPoint = new Point(xs.Min(), ys.Min());
            Point maxPoint = new Point(xs.Max(), ys.Max());
            Point diffs = new Point(maxPoint.X - minPoint.X, maxPoint.Y - minPoint.Y);

            if (diffs.Y < 5)
                diffs.Y = diffs.X / 10;


            WordBorder wb = new();
            wb.Word = result.Text;
            wb.Width = diffs.X / dpi.DpiScaleX + 12;
            wb.Height = diffs.Y / dpi.DpiScaleY + 12;
            wb.SetAsBarcode();
            wordBorders.Add(wb);
            _ = RectanglesCanvas.Children.Add(wb);
            double left = minPoint.X / (dpi.DpiScaleX) - 6;
            double top = minPoint.Y / (dpi.DpiScaleY) - 6;
            Canvas.SetLeft(wb, left);
            Canvas.SetTop(wb, top);
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded)
            return;

        if (sender is not TextBox searchBox) return;

        reSearchTimer.Stop();
        reSearchTimer.Start();
    }

    private void ReSearchTimer_Tick(object? sender, EventArgs e)
    {
        reSearchTimer.Stop();
        if (SearchBox.Text is not string searchText)
            return;

        if (string.IsNullOrWhiteSpace(searchText))
        {
            foreach (WordBorder wb in wordBorders)
                wb.Deselect();
        }
        else
        {
            foreach (WordBorder wb in wordBorders)
            {
                if (!string.IsNullOrWhiteSpace(searchText)
                    && wb.Word.ToLower().Contains(searchText.ToLower()))
                    wb.Select();
                else
                    wb.Deselect();
            }
        }

        UpdateFrameText();

        MatchesTXTBLK.Visibility = Visibility.Visible;
    }

    private void UpdateFrameText()
    {
        if (wordBorders is null || wordBorders.Count == 0)
            return;

        string[] selectedWbs = wordBorders.Where(w => w.IsSelected).Select(t => t.Word).ToArray();

        StringBuilder stringBuilder = new();

        //if (TableToggleButton.IsChecked is true)
        //{
        //    ResultTable.GetTextFromTabledWordBorders(stringBuilder, wordBorders.ToList(), isSpaceJoining);
        //}
        //else
        //{
        //    if (selectedWbs.Length > 0)
        //        stringBuilder.AppendJoin(Environment.NewLine, selectedWbs);
        //    else
        //        stringBuilder.AppendJoin(Environment.NewLine, wordBorders.Select(w => w.Word).ToArray());
        //}

        List<IGrouping<int, WordBorder>> wordsGroupedByRow = wordBorders.GroupBy(x => x.ResultRowID).ToList();

        double leftMarginStart = wordBorders.Min(x => x.Left);

        List<double> pixelCharWidths = new();
        foreach (WordBorder wbr in wordBorders)
            pixelCharWidths.Add(wbr.AverageCharPixelWidth());
        double averageCharWidth = pixelCharWidths.Average();

        foreach (IGrouping<int, WordBorder> Group in wordsGroupedByRow)
        {
            double distToLeft = leftMarginStart;

            foreach (WordBorder wb in Group)
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

        FrameText = stringBuilder.ToString();

        if (string.IsNullOrEmpty(FrameText))
            GrabBTN.IsEnabled = false;
        else
            GrabBTN.IsEnabled = true;

        if (IsFromEditWindow
            && destinationTextBox is not null
            && EditTextToggleButton.IsChecked is true)
        {
            destinationTextBox.SelectedText = FrameText;
        }
    }

    private void SearchBox_GotFocus(object sender, RoutedEventArgs e)
    {
        if (SearchBox is TextBox searchBox)
            searchBox.Text = "";
    }

    private void ExactMatchChkBx_Click(object sender, RoutedEventArgs e)
    {
        reSearchTimer.Stop();
        reSearchTimer.Start();
    }

    private void ClearBTN_Click(object sender, RoutedEventArgs e)
    {
        SearchBox.Text = "";
    }

    private void RefreshBTN_Click(object sender, RoutedEventArgs e)
    {
        TextBox searchBox = SearchBox;
        ResetGrabFrame();

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void SettingsBTN_Click(object sender, RoutedEventArgs e)
    {
        WindowUtilities.OpenOrActivateWindow<SettingsWindow>();
    }

    private void GrabFrameWindow_Activated(object? sender, EventArgs e)
    {
        RectanglesCanvas.Opacity = 1;
        if (!IsWordEditMode && !IsFreezeMode)
            reDrawTimer.Start();
        else
            UpdateFrameText();
    }

    private bool isMiddleDown = false;

    private void RectanglesCanvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.RightButton == MouseButtonState.Pressed)
        {
            e.Handled = false;
            return;
        }

        isSelecting = true;
        clickedPoint = e.GetPosition(RectanglesCanvas);
        RectanglesCanvas.CaptureMouse();
        selectBorder.Height = 1;
        selectBorder.Width = 1;

        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            e.Handled = true;

            isMiddleDown = true;
            ResetGrabFrame();
            UnfreezeGrabFrame();
            return;
        }

        CursorClipper.ClipCursor(RectanglesCanvas);

        try { RectanglesCanvas.Children.Remove(selectBorder); } catch (Exception) { }

        selectBorder.BorderThickness = new Thickness(2);
        Color borderColor = Color.FromArgb(255, 40, 118, 126);
        selectBorder.BorderBrush = new SolidColorBrush(borderColor);
        Color backgroundColor = Color.FromArgb(15, 40, 118, 126);
        selectBorder.Background = new SolidColorBrush(backgroundColor);
        _ = RectanglesCanvas.Children.Add(selectBorder);
        Canvas.SetLeft(selectBorder, clickedPoint.X);
        Canvas.SetTop(selectBorder, clickedPoint.Y);
    }

    private async void RectanglesCanvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        isSelecting = false;
        CursorClipper.UnClipCursor();
        RectanglesCanvas.ReleaseMouseCapture();

        if (e.ChangedButton == MouseButton.Middle)
        {
            isMiddleDown = false;
            FreezeGrabFrame();

            reDrawTimer.Stop();
            reDrawTimer.Start();
            return;
        }

        try { RectanglesCanvas.Children.Remove(selectBorder); } catch { }

        await Task.Delay(50);
        CheckSelectBorderIntersections(true);
    }

    private void RectanglesCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!isSelecting && !isMiddleDown)
            return;

        Point movingPoint = e.GetPosition(RectanglesCanvas);

        var left = Math.Min(clickedPoint.X, movingPoint.X);
        var top = Math.Min(clickedPoint.Y, movingPoint.Y);

        if (isMiddleDown)
        {
            double xShiftDelta = (movingPoint.X - clickedPoint.X);
            double yShiftDelta = (movingPoint.Y - clickedPoint.Y);

            Top += yShiftDelta;
            Left += xShiftDelta;

            return;
        }

        selectBorder.Height = Math.Max(clickedPoint.Y, movingPoint.Y) - top;
        selectBorder.Width = Math.Max(clickedPoint.X, movingPoint.X) - left;

        Canvas.SetLeft(selectBorder, left);
        Canvas.SetTop(selectBorder, top);

        CheckSelectBorderIntersections();
    }

    private void CheckSelectBorderIntersections(bool finalCheck = false)
    {
        Rect rectSelect = new Rect(Canvas.GetLeft(selectBorder), Canvas.GetTop(selectBorder), selectBorder.Width, selectBorder.Height);

        bool clickedEmptySpace = true;
        bool smallSelction = false;
        if (rectSelect.Width < 10 && rectSelect.Height < 10)
            smallSelction = true;

        foreach (WordBorder wordBorder in wordBorders)
        {
            Rect wbRect = new Rect(Canvas.GetLeft(wordBorder), Canvas.GetTop(wordBorder), wordBorder.Width, wordBorder.Height);

            if (rectSelect.IntersectsWith(wbRect))
            {
                clickedEmptySpace = false;

                if (!smallSelction)
                {
                    wordBorder.Select();
                    wordBorder.WasRegionSelected = true;
                }
                else if (!finalCheck)
                {
                    if (wordBorder.IsSelected)
                        wordBorder.Deselect();
                    else
                        wordBorder.Select();
                    wordBorder.WasRegionSelected = false;
                }

            }
            else
            {
                if (wordBorder.WasRegionSelected
                    && !smallSelction)
                    wordBorder.Deselect();
            }

            if (finalCheck)
                wordBorder.WasRegionSelected = false;
        }

        if (clickedEmptySpace
            && smallSelction
            && finalCheck)
        {
            foreach (WordBorder wb in wordBorders)
                wb.Deselect();
        }

        if (finalCheck)
            UpdateFrameText();
    }

    private void TableToggleButton_Click(object sender, RoutedEventArgs e)
    {
        TextBox searchBox = SearchBox;
        ResetGrabFrame();

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private async void EditToggleButton_Click(object sender, RoutedEventArgs e)
    {
        if (EditToggleButton.IsChecked is bool isEditMode && isEditMode)
        {
            if (!IsFreezeMode)
            {
                FreezeToggleButton.IsChecked = true;
                ResetGrabFrame();
                await Task.Delay(200);
                FreezeGrabFrame();
                reDrawTimer.Stop();
                reDrawTimer.Start();
            }

            EnterEditMode();
        }
        else
            ExitEditMode();
    }

    private void EnterEditMode()
    {
        IsWordEditMode = true;

        foreach (UIElement uIElement in RectanglesCanvas.Children)
        {
            if (uIElement is WordBorder wb)
                wb.EnterEdit();
        }
    }

    private void ExitEditMode()
    {
        IsWordEditMode = false;

        foreach (UIElement uIElement in RectanglesCanvas.Children)
        {
            if (uIElement is WordBorder wb)
                wb.ExitEdit();
        }
    }

    private async void FreezeToggleButton_Click(object sender, RoutedEventArgs e)
    {
        TextBox searchBox = SearchBox;
        ResetGrabFrame();

        await Task.Delay(200);
        if (FreezeToggleButton.IsChecked is bool freezeMode && freezeMode)
            FreezeGrabFrame();
        else
            UnfreezeGrabFrame();

        await Task.Delay(200);

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void FreezeGrabFrame()
    {
        if (droppedImageSource is not null)
            GrabFrameImage.Source = droppedImageSource;
        else if (frameContentImageSource is not null)
            GrabFrameImage.Source = frameContentImageSource;
        else
        {
            frameContentImageSource = ImageMethods.GetWindowBoundsImage(this);
            GrabFrameImage.Source = frameContentImageSource;
        }

        FreezeToggleButton.IsChecked = true;
        Topmost = false;
        this.Background = new SolidColorBrush(Colors.DimGray);
        RectanglesBorder.Background.Opacity = 0;
        IsFreezeMode = true;
    }

    private void UnfreezeGrabFrame()
    {
        Topmost = true;
        GrabFrameImage.Source = null;
        frameContentImageSource = null;
        droppedImageSource = null;
        RectanglesBorder.Background.Opacity = 0.05;
        FreezeToggleButton.IsChecked = false;
        this.Background = new SolidColorBrush(Colors.Transparent);
        IsFreezeMode = false;
    }

    private void EditTextBTN_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton toggleButton
            && toggleButton.IsChecked is false
            && destinationTextBox is not null)
        {
            destinationTextBox.SelectedText = "";
            destinationTextBox = null;
            return;
        }

        if (destinationTextBox is null)
        {
            EditTextWindow etw = WindowUtilities.OpenOrActivateWindow<EditTextWindow>();
            destinationTextBox = etw.GetMainTextBox();
        }

        UpdateFrameText();
    }

    private async void GrabFrameWindow_Drop(object sender, DragEventArgs e)
    {
        // Mark the event as handled, so TextBox's native Drop handler is not called.
        e.Handled = true;
        var fileName = IsSingleFile(e);
        if (fileName is null) return;

        Activate();
        Uri fileURI = new(fileName);
        droppedImageSource = null;

        try
        {
            ResetGrabFrame();
            await Task.Delay(300);
            BitmapImage droppedImage = new(fileURI);
            droppedImageSource = droppedImage;
            FreezeToggleButton.IsChecked = true;
            FreezeGrabFrame();
        }
        catch (Exception)
        {
            UnfreezeGrabFrame();
            MessageBox.Show("Not an image");
        }

        IsDragOver = false;

        reDrawTimer.Start();
    }

    private void GrabFrameWindow_DragOver(object sender, DragEventArgs e)
    {
        IsDragOver = true;
        // As an arbitrary design decision, we only want to deal with a single file.
        e.Effects = IsSingleFile(e) != null ? DragDropEffects.Copy : DragDropEffects.None;
        // Mark the event as handled, so TextBox's native DragOver handler is not called.
        e.Handled = true;
    }

    // If the data object in args is a single file, this method will return the filename.
    // Otherwise, it returns null.
    private static string? IsSingleFile(DragEventArgs args)
    {
        // Check for files in the hovering data object.
        if (args.Data.GetDataPresent(DataFormats.FileDrop, true))
        {
            var fileNames = args.Data.GetData(DataFormats.FileDrop, true) as string[];
            // Check for a single file or folder.
            if (fileNames?.Length is 1)
            {
                // Check for a file (a directory will return false).
                if (File.Exists(fileNames[0]))
                {
                    // At this point we know there is a single file.
                    return fileNames[0];
                }
            }
        }
        return null;
    }

    private void GrabFrameWindow_DragLeave(object sender, DragEventArgs e)
    {
        IsDragOver = false;
    }

    private void OnMinimizeButtonClick(object sender, RoutedEventArgs e)
    {
        this.WindowState = WindowState.Minimized;
    }

    private void OnRestoreButtonClick(object sender, RoutedEventArgs e)
    {
        if (this.WindowState == WindowState.Maximized)
            this.WindowState = WindowState.Normal;
        else
            this.WindowState = WindowState.Maximized;

        SetRestoreState();
    }

    private void SetRestoreState()
    {
        if (WindowState == WindowState.Maximized)
            RestoreTextlock.Text = "";
        else
            RestoreTextlock.Text = "";
    }

    private void AspectRationMI_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem aspectMI)
            return;

        if (aspectMI.IsChecked is false)
            GrabFrameImage.Stretch = Stretch.Fill;
        else
            GrabFrameImage.Stretch = Stretch.Uniform;
    }

    private async void FreezeMI_Click(object sender, RoutedEventArgs e)
    {
        if (IsFreezeMode)
        {
            FreezeToggleButton.IsChecked = false;
            UnfreezeGrabFrame();
            ResetGrabFrame();
        }
        else
        {
            RectanglesCanvas.ContextMenu.IsOpen = false;
            await Task.Delay(150);
            FreezeToggleButton.IsChecked = true;
            ResetGrabFrame();
            FreezeGrabFrame();
        }

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void LoadOcrLanguages()
    {
        if (LanguagesComboBox.Items.Count > 0)
            return;

        IReadOnlyList<Language> possibleOCRLangs = OcrEngine.AvailableRecognizerLanguages;
        Language firstLang = LanguageUtilities.GetOCRLanguage();

        int count = 0;

        foreach (Language language in possibleOCRLangs)
        {
            LanguagesComboBox.Items.Add(language);

            if (language.LanguageTag == firstLang?.LanguageTag)
                LanguagesComboBox.SelectedIndex = count;

            count++;
        }

        isLanguageBoxLoaded = true;
    }

    private void LanguagesComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!isLanguageBoxLoaded || sender is not ComboBox langComboBox)
            return;

        Language? pickedLang = langComboBox.SelectedItem as Language;

        if (pickedLang != null)
        {
            Settings.Default.LastUsedLang = pickedLang.LanguageTag;
            Settings.Default.Save();
        }

        ResetGrabFrame();

        reDrawTimer.Stop();
        reDrawTimer.Start();
    }

    private void LanguagesComboBox_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.MiddleButton == MouseButtonState.Pressed)
        {
            Settings.Default.LastUsedLang = String.Empty;
            Settings.Default.Save();
        }
    }

    private void GrabFrameWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        FrameText = "";
        wordBorders.Clear();
        UpdateFrameText();
    }
}
