using System;
using System.Globalization;
using System.Net;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace GlobalTranslator
{
    internal enum TranslationPopupMode
    {
        Selection,
        Ocr
    }

    internal sealed class PopupWindow : Window
    {
        private const int WmNcHitTest = 0x0084;
        private const int HtLeft = 10;
        private const int HtRight = 11;
        private const int HtTop = 12;
        private const int HtTopLeft = 13;
        private const int HtTopRight = 14;
        private const int HtBottom = 15;
        private const int HtBottomLeft = 16;
        private const int HtBottomRight = 17;

        private static readonly Brush Navy = Brush("#0B2942");
        private static readonly Brush Ocean = Brush("#087EA4");
        private static readonly Brush Cyan = Brush("#27C3D6");
        private static readonly Brush Muted = Brush("#64778A");
        private static readonly Brush Paper = Brush("#F7FBFD");

        private readonly TextBox _source;
        private readonly TextBox _translation;
        private TextBlock _meta;
        private readonly ProgressBar _progress;
        private readonly DispatcherTimer _hideTimer;
        private readonly DispatcherTimer _loadingTimer;
        private readonly DispatcherTimer _noticeTimer;
        private readonly PopupDismissMonitor _outsideMonitor;
        private CancellationTokenSource _cancellation;
        private string _translatedText = "";
        private string _currentText = "";
        private int _anchorX;
        private int _anchorY;
        private NativeMethods.RECT _anchorRect;
        private bool _hasAnchorRect;
        private bool _sourceExpanded;
        private bool _suppressSourceChanges;
        private bool _ocrDirty;
        private bool _isPinned;
        private bool _manualPinned;
        private bool _editProtected;
        private bool _manualLayout;
        private bool _draggingDivider;
        private GridSplitter _divider;
        private bool? _dividerIsVertical;
        private TextBlock _translationLabel;
        private Border _headerModelSlot;
        private TextBlock _brandTitle;
        private readonly DispatcherTimer _resizeTimer;
        private bool _isDismissing;
        private int _presentationVersion;
        private Border _header;
        private DockPanel _actions;
        private bool _isConfiguringSize;
        private double _lastOcrWidth = 560;
        private double _lastOcrHeight = 480;
        private double _ocrSourceShare = 45;
        private bool _hasOcrSize;
        private TranslationPopupMode _popupMode;
        private Grid _body;
        private Grid _contentGrid;
        private Grid _sourcePanel;
        private Grid _translationPanel;
        private Border _sourceCard;
        private Border _translationCard;
        private TextBlock _sourceSummary;
        private TextBlock _sourceState;
        private StackPanel _errorPanel;
        private TextBlock _errorText;
        private Button _retryButton;
        private Button _openSettingsButton;
        private Button _pinButton;
        private System.Windows.Shapes.Path _pinGlyph;
        private TextBlock _loadingText;
        private Button _copySource;
        private Button _retranslate;
        private Button _recapture;
        private Button _copyTranslation;
        private FrameworkElement _modelChipHost;
        private Button _modelButton;
        private TextBlock _modelButtonText;
        private Border _modelBadge;
        private TextBlock _modelBadgeText;
        private Popup _modelPopup;
        private Border _modelPopupCard;
        private StackPanel _modelMenuItems;
        private int _availableModelCount;
        private Button _collapseButton;
        private Button _expandButton;
        private System.Windows.Shapes.Path _expandGlyph;
        private FrameworkElement _resizeGrip;
        private AppSettings _activeSettings;
        private TranslationClient _activeClient;
        private bool _ocrMode;
        private string _ocrMetaSuffix = "";

        public event EventHandler OcrRecaptureRequested;
        public event EventHandler ModelSettingsRequested;
        public event EventHandler SettingsRequested;
        public event EventHandler VisibilityChanged;
        public event EventHandler Dismissed;

        public PopupWindow()
        {
            Title = "鲨译 Sharkey";
            Width = 480;
            Height = 230;
            MinWidth = 360;
            MinHeight = 160;
            SizeToContent = SizeToContent.Manual;
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            ResizeMode = ResizeMode.CanResize;
            SourceInitialized += delegate
            {
                HwndSource source = HwndSource.FromHwnd(
                    new WindowInteropHelper(this).Handle);
                if (source != null) source.AddHook(WindowProc);
            };

            _hideTimer = new DispatcherTimer();
            _resizeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(240) };
            _resizeTimer.Tick += delegate
            {
                _resizeTimer.Stop();
                if (IsVisible && !_isDismissing && !_manualLayout)
                    UpdateResultHeight(_translatedText);
            };
            _loadingTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(150)
            };
            _loadingTimer.Tick += delegate
            {
                _loadingTimer.Stop();
                if (!_isDismissing)
                {
                    _progress.Visibility = Visibility.Visible;
                    _loadingText.Visibility = Visibility.Visible;
                    _loadingText.Text = "正在翻译…";
                }
            };
            _noticeTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _noticeTimer.Tick += delegate
            {
                _noticeTimer.Stop();
                Dismiss();
            };
            _outsideMonitor = new PopupDismissMonitor(Dispatcher);
            _outsideMonitor.MouseDown += delegate(object sender, PopupMouseEventArgs args)
            {
                if (!IsVisible || _isPinned || _modelPopup == null ||
                    _modelPopup.IsOpen || IsPointInsideWindow(args.X, args.Y))
                    return;
                Dismiss();
            };
            IsVisibleChanged += delegate
            {
                if (IsVisible)
                {
                    _outsideMonitor.Start();
                    RaiseVisibilityChanged();
                }
                else
                {
                    _outsideMonitor.Stop();
                    RaiseVisibilityChanged();
                }
            };
            StateChanged += delegate
            {
                UpdateExpandGlyph();
                UpdateContentLayout();
            };
            SizeChanged += delegate
            {
                if (!_isConfiguringSize && _ocrMode &&
                    WindowState == WindowState.Normal && ActualWidth >= 420)
                {
                    _lastOcrWidth = ActualWidth;
                    _lastOcrHeight = ActualHeight;
                    _hasOcrSize = true;
                }
                UpdateContentLayout();
            };
            Closed += delegate
            {
                _outsideMonitor.Stop();
                _resizeTimer.Stop();
                _loadingTimer.Stop();
                _noticeTimer.Stop();
            };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Escape) return;
                if (HandleEscape()) e.Handled = true;
            };

            var card = new Border
            {
                Background = Brush("#F7FCFD"),
                CornerRadius = new CornerRadius(18),
                BorderBrush = Brush("#D2DDE7EA"),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 26,
                    ShadowDepth = 5,
                    Opacity = .16,
                    Color = Color.FromRgb(11, 57, 72)
                }
            };
            var root = new Grid();
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            card.Child = root;

            Border header = BuildHeader();
            _header = header;
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            _body = new Grid { Margin = new Thickness(16, 12, 16, 13) };
            _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _body.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star),
                MinHeight = 90
            });
            _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _modelChipHost = BuildModelChip();
            Grid.SetRow(_modelChipHost, 0);
            _body.Children.Add(_modelChipHost);

            _sourcePanel = new Grid();
            _sourcePanel.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            _sourcePanel.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            _sourcePanel.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            TextBlock sourceLabel = SectionLabel("原文");
            Grid.SetRow(sourceLabel, 0);
            _sourcePanel.Children.Add(sourceLabel);
            _sourceCard = new Border
            {
                Background = Paper,
                BorderBrush = Brush("#D7E6EA"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(11),
                Padding = new Thickness(3),
                Margin = new Thickness(0, 6, 0, 12)
            };
            var sourceContent = new Grid();
            _sourceSummary = new TextBlock
            {
                Foreground = Muted,
                FontSize = 12.5,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(8, 6, 8, 6),
                Cursor = Cursors.Hand
            };
            _source = MakeSelectableTextBox(13, Muted, 20);
            _source.Background = Paper;
            _source.Padding = new Thickness(8, 5, 10, 9);
            _source.Visibility = Visibility.Collapsed;
            _source.TextChanged += delegate
            {
                _sourceSummary.Text = CompactSourceText(_source.Text);
                if (_ocrMode && !_suppressSourceChanges && _source.IsFocused)
                    SetOcrDirty(true);
            };
            sourceContent.Children.Add(_sourceSummary);
            sourceContent.Children.Add(_source);
            _sourceCard.Child = sourceContent;
            _sourceCard.MouseLeftButtonUp += delegate
            {
                if (!_ocrMode) ToggleSourceExpanded();
            };
            Grid.SetRow(_sourceCard, 1);
            _sourcePanel.Children.Add(_sourceCard);
            _sourceState = new TextBlock
            {
                FontSize = 10.5,
                Foreground = Brush("#A46717"),
                Margin = new Thickness(6, -8, 0, 5),
                Visibility = Visibility.Collapsed
            };
            Grid.SetRow(_sourceState, 2);
            _sourcePanel.Children.Add(_sourceState);

            _translationPanel = new Grid();
            _translationPanel.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            _translationPanel.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            TextBlock translationLabel = SectionLabel("译文");
            _translationLabel = translationLabel;
            Grid.SetRow(translationLabel, 0);
            _translationPanel.Children.Add(translationLabel);
            _translationCard = new Border
            {
                Background = Brush("#FCFEFE"),
                BorderBrush = Brush("#D5E5E9"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 5, 3, 5),
                Margin = new Thickness(0, 6, 0, 0)
            };
            var translationContent = new Grid();
            _translation = MakeSelectableTextBox(17, Navy, 29);
            _translation.FontWeight = FontWeights.Medium;
            _translation.Background = Brushes.Transparent;
            // Leave enough space below the last baseline for Latin
            // descenders such as g, j, p, q and y.  The bottom padding is
            // part of the scroll extent, so the final line remains visible
            // even when the text box is scrolled all the way down.
            _translation.Padding = new Thickness(1, 4, 10, 10);
            _translation.MinHeight = 54;
            translationContent.Children.Add(_translation);
            _errorPanel = BuildErrorPanel();
            _errorPanel.Visibility = Visibility.Collapsed;
            translationContent.Children.Add(_errorPanel);
            _translationCard.Child = translationContent;
            Grid.SetRow(_translationCard, 1);
            _translationPanel.Children.Add(_translationCard);

            _contentGrid = new Grid();
            _contentGrid.Children.Add(_sourcePanel);
            _contentGrid.Children.Add(_translationPanel);
            Grid.SetRow(_contentGrid, 1);
            _body.Children.Add(_contentGrid);

            _progress = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 3,
                Margin = new Thickness(0, 9, 0, 0),
                Foreground = Cyan,
                Background = Brush("#E3F1F3")
            };
            _loadingText = new TextBlock
            {
                Text = "正在翻译…",
                FontSize = 11,
                Foreground = Muted,
                Margin = new Thickness(0, 6, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var progressPanel = new StackPanel();
            progressPanel.Children.Add(_progress);
            progressPanel.Children.Add(_loadingText);
            Grid.SetRow(progressPanel, 2);
            _body.Children.Add(progressPanel);

            DockPanel actions = BuildActions();
            _actions = actions;
            Grid.SetRow(actions, 3);
            _body.Children.Add(actions);

            Grid.SetRow(_body, 1);
            root.Children.Add(_body);
            _resizeGrip = BuildResizeGrip();
            Grid.SetRowSpan(_resizeGrip, 2);
            root.Children.Add(_resizeGrip);
            Content = card;
            UpdateContentLayout();
        }

        public void Translate(
            string text, int x, int y, AppSettings settings, TranslationClient client)
        {
            BeginTranslation(
                text,
                x,
                y,
                new NativeMethods.RECT(),
                settings,
                client,
                false,
                "",
                true);
        }

        public void TranslateOcr(
            OcrRecognitionResult result, int x, int y,
            AppSettings settings, TranslationClient client)
        {
            TranslateOcrAtBounds(
                result,
                new NativeMethods.RECT
                {
                    Left = x,
                    Top = y,
                    Right = x,
                    Bottom = y
                },
                settings,
                client);
        }

        public void TranslateOcrAtBounds(
            OcrRecognitionResult result,
            NativeMethods.RECT screenBounds,
            AppSettings settings,
            TranslationClient client)
        {
            string suffix = "  ·  " + result.Engine;
            if (!string.IsNullOrEmpty(result.Language))
                suffix += " (" + result.Language + ")";
            if (result.UsedAi && !string.IsNullOrWhiteSpace(result.Warning))
                suffix += "  ·  AI 提示";
            else if (!result.UsedAi && !string.IsNullOrWhiteSpace(result.Warning))
                suffix += "  ·  本地回退";
            else if (!result.UsedAi && result.IsLowQuality)
                suffix += "  ·  可能需要重新框选";
            BeginTranslation(
                result.Text,
                screenBounds.Left,
                screenBounds.Bottom,
                screenBounds,
                settings,
                client,
                true,
                suffix,
                true);
        }

        private async void BeginTranslation(
            string text, int x, int y, NativeMethods.RECT screenBounds,
            AppSettings settings, TranslationClient client,
            bool ocrMode, string ocrMetaSuffix, bool reposition)
        {
            if (_cancellation != null) _cancellation.Cancel();
            _presentationVersion++;
            _cancellation = new CancellationTokenSource();
            CancellationTokenSource activeCancellation = _cancellation;
            _isDismissing = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            _activeSettings = settings;
            _activeClient = client;
            _ocrMode = ocrMode;
            _popupMode = ocrMode
                ? TranslationPopupMode.Ocr
                : TranslationPopupMode.Selection;
            _resizeTimer.Stop();
            if (reposition)
            {
                _manualLayout = false;
                _ocrSourceShare = 45;
                SetOcrDirty(false);
            }
            ApplyReadingFont(settings.PopupFontSize);
            // F8 is a quick-reading card: keep the source out of the visual
            // layout entirely.  OCR (F9) remains the detailed workspace and
            // is the only mode that shows the editable source panel.
            _sourcePanel.Visibility = ocrMode
                ? Visibility.Visible
                : Visibility.Collapsed;
            _translationPanel.Visibility = Visibility.Visible;
            _copyTranslation.Visibility = Visibility.Visible;
            if (!ocrMode && WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            _ocrMetaSuffix = ocrMetaSuffix ?? "";
            _currentText = text ?? "";
            _anchorX = x;
            _anchorY = y;
            _anchorRect = screenBounds;
            _hasAnchorRect = screenBounds.Right > screenBounds.Left &&
                             screenBounds.Bottom > screenBounds.Top;
            _sourceExpanded = ocrMode;
            _noticeTimer.Stop();
            RefreshModelSelector(settings);
            _hideTimer.Stop();
            _loadingTimer.Stop();
            _suppressSourceChanges = true;
            _source.Text = _currentText;
            _suppressSourceChanges = false;
            _source.IsReadOnly = !ocrMode;
            _source.Visibility = ocrMode
                ? Visibility.Visible
                : Visibility.Collapsed;
            _sourceSummary.Visibility = ocrMode
                ? Visibility.Collapsed
                : Visibility.Visible;
            _source.ScrollToHome();
            SetOcrActionsVisible(ocrMode);
            _translation.Text = "";
            _translation.Foreground = Navy;
            _translatedText = "";
            _copyTranslation.Content = "复制译文";
            _errorPanel.Visibility = Visibility.Collapsed;
            _translation.Visibility = Visibility.Visible;
            _loadingText.Visibility = Visibility.Collapsed;
            _meta.Text = ProviderName(settings.Provider) + "  ·  → " +
                         LanguageName(SmartTargetResolver.Resolve(
                             _currentText,
                             settings.TargetLanguageMode,
                             settings.TargetLanguage)) +
                         _ocrMetaSuffix;
            if (!ocrMode)
                _meta.Text = (settings.Provider == "ModelApi" ? "" : ProviderName(settings.Provider) + " · ") + "→ " + LanguageName(SmartTargetResolver.Resolve(
                    _currentText, settings.TargetLanguageMode, settings.TargetLanguage));
            _meta.ToolTip = ProviderName(settings.Provider);
            UpdateContentLayout();
            if (reposition) ConfigureModeSize("");
            // Keep fast requests visually quiet. The timer reveals the
            // progress line only after a request takes noticeable time.
            _progress.Visibility = Visibility.Collapsed;
            if (!IsVisible)
            {
                BeginAnimation(OpacityProperty, null);
                if (!ocrMode)
                {
                    // Create and position the hidden HWND before its first frame.
                    new WindowInteropHelper(this).EnsureHandle();
                    if (reposition) PositionNear(x, y, screenBounds);
                    Opacity = 1;
                    Show();
                }
                else
                {
                    Opacity = 0;
                    Show();
                    if (reposition) PositionNear(x, y, screenBounds);
                    AnimateOpacity(0, 1, 140);
                }
            }
            else
            {
                if (reposition) PositionNear(x, y, screenBounds);
                Opacity = 1;
            }
            UpdateContentLayout();
            _loadingTimer.Start();

            try
            {
                TranslationResult result = await client.TranslateAsync(
                    text,
                    settings,
                    activeCancellation.Token,
                    delegate(string partial)
                    {
                        if (activeCancellation.IsCancellationRequested ||
                            !ReferenceEquals(_cancellation, activeCancellation))
                            return;
                        string cleanPartial =
                            TranslationClient.CleanTranslationText(
                                partial);
                        _translatedText = cleanPartial;
                        _translation.Text = cleanPartial;
                        if (!_resizeTimer.IsEnabled) _resizeTimer.Start();
                    });
                if (activeCancellation.IsCancellationRequested ||
                    !ReferenceEquals(_cancellation, activeCancellation))
                    return;
                DiagnosticLog.Write(
                    "Translation succeeded; provider=" + result.Provider +
                    "; characters=" + result.Text.Length);
                _translatedText = result.Text;
                _translation.Text = result.Text;
                // Edits made while a request is running still need protection.
                if (ocrMode && string.Equals(_source.Text, text, StringComparison.Ordinal))
                    SetOcrDirty(false);
                _translation.ScrollToHome();
                _translation.Visibility = Visibility.Visible;
                _errorPanel.Visibility = Visibility.Collapsed;
                _meta.Text = FormatResultMeta(result, settings) +
                             (result.FromCache ? "  ·  本次缓存" : "") +
                             _ocrMetaSuffix;
                if (!ocrMode)
                    _meta.Text = (settings.Provider == "ModelApi" ? "" : ProviderName(settings.Provider) + " · ") +
                        (string.IsNullOrWhiteSpace(result.DetectedLanguage) ? "" : LanguageName(result.DetectedLanguage) + " ") +
                        "→ " + LanguageName(result.EffectiveTargetLanguage);
                _meta.ToolTip = FormatResultMeta(result, settings) + (result.FromCache ? " · 本次缓存" : "");
                UpdateResultHeight(result.Text);
                ScheduleRenderedContentFit();
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                if (activeCancellation.IsCancellationRequested ||
                    !ReferenceEquals(_cancellation, activeCancellation))
                    return;
                DiagnosticLog.Write("Translation failed; type=" + ex.GetType().Name);
                ShowTranslationError(ex);
            }
            finally
            {
                if (ReferenceEquals(_cancellation, activeCancellation))
                {
                    _loadingTimer.Stop();
                    _resizeTimer.Stop();
                    _progress.Visibility = Visibility.Collapsed;
                    if (!string.IsNullOrEmpty(_translatedText))
                        _translation.Foreground = Navy;
                    _loadingText.Visibility = Visibility.Collapsed;
                }
            }
        }

        private void ConfigureModeSize(string content)
        {
            if (WindowState == WindowState.Maximized) return;
            _isConfiguringSize = true;
            try
            {
                if (_popupMode == TranslationPopupMode.Ocr)
                {
                    MinWidth = 420;
                    MinHeight = 340;
                    Width = _hasOcrSize ? _lastOcrWidth : 560;
                    Height = _hasOcrSize ? _lastOcrHeight : 480;
                    _hasOcrSize = true;
                }
                else
                {
                    MinWidth = 360;
                    MinHeight = 160;
                    Width = 440;
                    Height = EstimateCompactHeight(content);
                }
            }
            finally
            {
                _isConfiguringSize = false;
            }
        }

        private double EstimateCompactHeight(string content)
        {
            double textWidth = Math.Max(
                220,
                (Width > 0 ? Width : 440) - 70);
            double textHeight = MeasureTextHeight(
                _translation,
                content,
                textWidth,
                TextBlock.GetLineHeight(_translation));

            // Measure fixed chrome rather than guessing its combined height.
            // The reserved status row prevents slow requests moving the footer.
            _header.Measure(new Size(Math.Max(1, Width - 2), double.PositiveInfinity));
            _actions.Measure(new Size(Math.Max(1, Width - 34), double.PositiveInfinity));
            double chromeHeight = _header.DesiredSize.Height + _actions.DesiredSize.Height +
                _body.Margin.Top + _body.Margin.Bottom + 34 +
                _translationCard.Padding.Top + _translationCard.Padding.Bottom + 2;
            double desired = chromeHeight + Math.Max(54, textHeight);
            return Math.Max(190, Math.Min(GetCompactHeightLimit(), desired));
        }

        private static double MeasureTextHeight(
            TextBox box, string content, double textWidth, double lineHeight)
        {
            string value = string.IsNullOrEmpty(content) ? " " : content;
            try
            {
                var formatted = new FormattedText(
                    value,
                    CultureInfo.CurrentUICulture,
                    FlowDirection.LeftToRight,
                    new Typeface(
                        box.FontFamily,
                        box.FontStyle,
                        box.FontWeight,
                        box.FontStretch),
                    box.FontSize,
                    box.Foreground ?? Navy,
                    1.0);
                formatted.MaxTextWidth = Math.Max(80, textWidth);
                formatted.LineHeight = lineHeight;
                return formatted.Height + box.Padding.Top +
                       box.Padding.Bottom + 4;
            }
            catch
            {
                int charactersPerLine = Math.Max(
                    8,
                    (int)(Math.Max(80, textWidth) /
                          Math.Max(6, box.FontSize * .65)));
                int lines = Math.Max(
                    1,
                    (value.Length + charactersPerLine - 1) /
                    charactersPerLine);
                return lines * lineHeight + box.Padding.Top +
                       box.Padding.Bottom + 4;
            }
        }

        private double GetCompactHeightLimit()
        {
            return GetWorkingHeightLimit(240, 620);
        }

        private double GetOcrHeightLimit()
        {
            return GetWorkingHeightLimit(360, 820);
        }

        private double GetWorkingHeightLimit(double minimum, double hardLimit)
        {
            try
            {
                int x = _anchorX;
                int y = _anchorY;
                var screen = System.Windows.Forms.Screen.FromPoint(
                    new System.Drawing.Point(x, y)).WorkingArea;
                uint dpi = NativeMethods.GetMonitorDpi(x, y);
                if (dpi < 96) dpi = 96;
                double workingHeight = screen.Height / (dpi / 96.0);
                return Math.Max(
                    minimum,
                    Math.Min(hardLimit, workingHeight - 20));
            }
            catch
            {
                return hardLimit;
            }
        }

        private void UpdateResultHeight(string translatedText)
        {
            if (_manualLayout) return;
            if (_popupMode == TranslationPopupMode.Ocr)
                UpdateOcrHeight(translatedText);
            else
                UpdateCompactHeight(translatedText);
        }

        private void UpdateCompactHeight(string content)
        {
            if (_popupMode != TranslationPopupMode.Selection ||
                WindowState == WindowState.Maximized)
                return;
            _isConfiguringSize = true;
            try
            {
                Height = Math.Max(Height, EstimateCompactHeight(content));
                if (IsVisible)
                {
                    ClampCurrentPosition();
                }
            }
            finally { _isConfiguringSize = false; }
        }

        private void UpdateOcrHeight(string translatedText)
        {
            if (_popupMode != TranslationPopupMode.Ocr ||
                WindowState == WindowState.Maximized)
                return;

            double windowWidth = Width > 0 ? Width : 560;
            bool split = windowWidth >= 760;
            double contentWidth = Math.Max(320, windowWidth - 64);
            double sourceWidth = split
                ? Math.Max(180, contentWidth * .45 - 24)
                : Math.Max(260, contentWidth - 24);
            double translationWidth = split
                ? Math.Max(220, contentWidth * .55 - 24)
                : Math.Max(260, contentWidth - 24);
            double sourceHeight = MeasureTextHeight(
                _source,
                _source.Text,
                sourceWidth,
                TextBlock.GetLineHeight(_source));
            double translationHeight = MeasureTextHeight(
                _translation,
                translatedText,
                translationWidth,
                TextBlock.GetLineHeight(_translation));

            if (!split && !_manualLayout)
            {
                double combined = sourceHeight + translationHeight;
                if (combined > 0)
                {
                    _ocrSourceShare = Math.Max(
                        30,
                        Math.Min(50, sourceHeight / combined * 100));
                }
            }

            bool hasModelChip = _modelChipHost != null &&
                                _modelChipHost.Visibility == Visibility.Visible;
            double chromeHeight = split
                ? (hasModelChip ? 180 : 139)
                : (hasModelChip ? 206 : 165);
            double desired = split
                ? chromeHeight + Math.Max(sourceHeight, translationHeight)
                : chromeHeight + sourceHeight + translationHeight;
            double target = Math.Max(
                Height,
                Math.Min(GetOcrHeightLimit(), desired));

            _isConfiguringSize = true;
            try
            {
                Height = Math.Min(GetOcrHeightLimit(), target);
                UpdateContentLayout();
                if (IsVisible)
                {
                    ClampCurrentPosition();
                }
            }
            finally { _isConfiguringSize = false; }
        }

        private void ScheduleRenderedContentFit()
        {
            CancellationTokenSource request = _cancellation;
            Dispatcher.BeginInvoke(
                DispatcherPriority.Loaded,
                new Action(delegate
                {
                    if (ReferenceEquals(request, _cancellation)) EnsureRenderedContentFit(0);
                }));
        }

        private void EnsureRenderedContentFit(int pass)
        {
            if (!IsVisible || _manualLayout || _isDismissing || WindowState == WindowState.Maximized ||
                string.IsNullOrEmpty(_translatedText))
                return;

            UpdateLayout();
            ScrollViewer translationScroll = GetTextScrollViewer(_translation);
            if (translationScroll == null) return;
            double translationOverflow = Math.Max(
                0,
                translationScroll.ExtentHeight -
                translationScroll.ViewportHeight);
            double growth;
            double limit;

            if (_popupMode == TranslationPopupMode.Ocr)
            {
                ScrollViewer sourceScroll = GetTextScrollViewer(_source);
                double sourceOverflow = sourceScroll == null
                    ? 0
                    : Math.Max(
                        0,
                        sourceScroll.ExtentHeight -
                        sourceScroll.ViewportHeight);
                bool split = WindowState == WindowState.Maximized ||
                             ActualWidth >= 760;
                if (split)
                {
                    growth = Math.Max(
                        sourceOverflow,
                        translationOverflow);
                }
                else
                {
                    double sourceFraction = Math.Max(
                        .01,
                        _ocrSourceShare / 100.0);
                    double translationFraction = Math.Max(
                        .01,
                        1 - sourceFraction);
                    growth = Math.Max(
                        sourceOverflow / sourceFraction,
                        translationOverflow / translationFraction);
                }
                limit = GetOcrHeightLimit();
            }
            else
            {
                growth = translationOverflow;
                limit = GetCompactHeightLimit();
            }

            // A small reserve protects the final baseline and scrollbar from
            // fractional-DPI rounding.  It is only added when the rendered
            // control reports real overflow.
            if (growth <= 1 || Height >= limit - 1) return;
            double nextHeight = Math.Min(limit, Height + growth + 14);
            if (nextHeight <= Height + .5) return;

            _isConfiguringSize = true;
            try
            {
                Height = nextHeight;
                UpdateContentLayout();
                UpdateLayout();
                ClampCurrentPosition();
            }
            finally { _isConfiguringSize = false; }

            if (pass < 2 && nextHeight < limit - 1)
            {
                CancellationTokenSource request = _cancellation;
                Dispatcher.BeginInvoke(
                    DispatcherPriority.Loaded,
                    new Action(delegate
                    {
                        if (ReferenceEquals(request, _cancellation)) EnsureRenderedContentFit(pass + 1);
                    }));
            }
        }

        private static ScrollViewer GetTextScrollViewer(TextBox box)
        {
            if (box == null) return null;
            box.ApplyTemplate();
            return box.Template == null
                ? null
                : box.Template.FindName("PART_ContentHost", box)
                    as ScrollViewer;
        }

        private void UpdateContentLayout()
        {
            if (_contentGrid == null || _sourcePanel == null ||
                _translationPanel == null)
                return;
            if (_draggingDivider) return;
            bool showSource = _popupMode == TranslationPopupMode.Ocr;
            _body.RowDefinitions[1].MinHeight = showSource ? 90 : 0;
            _body.RowDefinitions[2].Height = showSource ? GridLength.Auto : new GridLength(34);
            _contentGrid.ClipToBounds = !showSource;
            _translation.MinHeight = showSource ? 54 : 0;
            _translationLabel.Visibility = showSource ? Visibility.Visible : Visibility.Collapsed;
            _translationCard.BorderThickness = new Thickness(showSource ? 1 : 0);
            _translationCard.Background = showSource ? Brushes.White : Brushes.Transparent;
            _translation.Background = showSource ? Brushes.White : Brushes.Transparent;
            _translationCard.Margin = new Thickness(0, showSource ? 6 : 0, 0, 0);
            if (_modelChipHost != null && _headerModelSlot != null)
            {
                _brandTitle.Visibility = showSource ? Visibility.Visible : Visibility.Collapsed;
                if (showSource && _headerModelSlot.Child != null)
                {
                    _headerModelSlot.Child = null;
                    _body.Children.Add(_modelChipHost);
                }
                else if (!showSource && _body.Children.Contains(_modelChipHost))
                {
                    _body.Children.Remove(_modelChipHost);
                    _headerModelSlot.Child = _modelChipHost;
                }
                _modelButton.MinWidth = showSource ? 190 : 0;
                _modelButton.MaxWidth = showSource ? 315 : 145;
                _modelButton.Height = showSource ? 32 : 28;
                _modelChipHost.Margin = showSource ? new Thickness(0, 0, 0, 8) : new Thickness(4, 0, 4, 0);
            }
            if (_divider == null)
            {
                _divider = new GridSplitter
                {
                    Background = Brushes.Transparent,
                    ResizeBehavior = GridResizeBehavior.PreviousAndNext,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    VerticalAlignment = VerticalAlignment.Stretch,
                    Focusable = true,
                    ToolTip = "拖动调整原文与译文空间；聚焦后可用方向键调整",
                    KeyboardIncrement = 10
                };
                _divider.MouseEnter += delegate { _divider.Background = Brush("#CCE2E9"); };
                _divider.MouseLeave += delegate { _divider.Background = Brushes.Transparent; };
                _divider.DragStarted += delegate { _draggingDivider = true; _manualLayout = true; };
                _divider.DragCompleted += delegate { RememberDividerShare(); };
                _divider.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
                {
                    if (e.Key == Key.Left || e.Key == Key.Right || e.Key == Key.Up || e.Key == Key.Down)
                    {
                        _manualLayout = true;
                        Dispatcher.BeginInvoke(new Action(RememberDividerShare));
                    }
                };
                _contentGrid.Children.Add(_divider);
            }
            _divider.Visibility = showSource ? Visibility.Visible : Visibility.Collapsed;
            // Keep the visual tree consistent even when a resize/state change
            // occurs between translation requests (or while the popup is
            // being constructed).
            _sourcePanel.Visibility = showSource
                ? Visibility.Visible
                : Visibility.Collapsed;
            bool split = showSource &&
                         (WindowState == WindowState.Maximized ||
                          ActualWidth >= 760);
            if (_dividerIsVertical != split)
            {
                var hitArea = new FrameworkElementFactory(typeof(Grid));
                hitArea.SetValue(Panel.BackgroundProperty, new TemplateBindingExtension(Control.BackgroundProperty));
                var grip = new FrameworkElementFactory(typeof(Border));
                grip.SetValue(Border.BackgroundProperty, Brush("#BDD4DD"));
                grip.SetValue(Border.CornerRadiusProperty, new CornerRadius(2));
                grip.SetValue(FrameworkElement.WidthProperty, split ? 3.0 : 36.0);
                grip.SetValue(FrameworkElement.HeightProperty, split ? 36.0 : 3.0);
                grip.SetValue(FrameworkElement.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                grip.SetValue(FrameworkElement.VerticalAlignmentProperty, VerticalAlignment.Center);
                hitArea.AppendChild(grip);
                _divider.Template = new ControlTemplate(typeof(GridSplitter)) { VisualTree = hitArea };
                _dividerIsVertical = split;
            }
            _contentGrid.RowDefinitions.Clear();
            _contentGrid.ColumnDefinitions.Clear();
            if (split)
            {
                _contentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(_ocrSourceShare, GridUnitType.Star), MinWidth = 90
                });
                _contentGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                _contentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(100 - _ocrSourceShare, GridUnitType.Star), MinWidth = 90
                });
                _contentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star)
                });
                _sourcePanel.Margin = new Thickness(0, 0, 7, 0);
                _translationPanel.Margin = new Thickness(7, 0, 0, 0);
                Grid.SetRow(_sourcePanel, 0);
                Grid.SetColumn(_sourcePanel, 0);
                Grid.SetRow(_translationPanel, 0);
                Grid.SetColumn(_translationPanel, 2);
                Grid.SetRow(_divider, 0);
                Grid.SetColumn(_divider, 1);
                _divider.ResizeDirection = GridResizeDirection.Columns;
                _divider.Cursor = Cursors.SizeWE;
            }
            else if (!showSource)
            {
                // Selection mode presents only the translated result.  Use a
                // single star-sized row so there is no empty source row or
                // residual spacing above the translation card.
                _contentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
                _contentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(1, GridUnitType.Star)
                });
                _sourcePanel.Margin = new Thickness(0);
                _translationPanel.Margin = new Thickness(0);
                Grid.SetRow(_translationPanel, 0);
                Grid.SetColumn(_translationPanel, 0);
            }
            else
            {
                _contentGrid.ColumnDefinitions.Add(new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
                _contentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(
                        _ocrSourceShare,
                        GridUnitType.Star), MinHeight = 60
                });
                _contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) });
                _contentGrid.RowDefinitions.Add(new RowDefinition
                {
                    Height = new GridLength(
                        100 - _ocrSourceShare,
                        GridUnitType.Star), MinHeight = 60
                });
                _sourcePanel.Margin = new Thickness(0);
                _translationPanel.Margin = new Thickness(0);
                Grid.SetRow(_sourcePanel, 0);
                Grid.SetColumn(_sourcePanel, 0);
                Grid.SetRow(_translationPanel, 2);
                Grid.SetColumn(_translationPanel, 0);
                Grid.SetRow(_divider, 1);
                Grid.SetColumn(_divider, 0);
                _divider.ResizeDirection = GridResizeDirection.Rows;
                _divider.Cursor = Cursors.SizeNS;
            }
        }

        private void RememberDividerShare()
        {
            _draggingDivider = false;
            bool split = _divider.ResizeDirection == GridResizeDirection.Columns;
            double first = split ? _contentGrid.ColumnDefinitions[0].ActualWidth : _contentGrid.RowDefinitions[0].ActualHeight;
            double second = split ? _contentGrid.ColumnDefinitions[2].ActualWidth : _contentGrid.RowDefinitions[2].ActualHeight;
            if (first + second > 0) _ocrSourceShare = Math.Max(10, Math.Min(90, first * 100 / (first + second)));
        }

        private void ApplyReadingFont(string preset)
        {
            double size = preset == "Small" ? 15 : preset == "Large" ? 20 : 17;
            _translation.FontSize = size;
            _source.FontSize = size - 3;
            TextBlock.SetLineHeight(_translation, Math.Ceiling(size * 1.65));
            TextBlock.SetLineHeight(_source, Math.Ceiling((size - 3) * 1.55));
        }

        private void ToggleSourceExpanded()
        {
            if (_ocrMode) return;
            _sourceExpanded = !_sourceExpanded;
            _sourceSummary.Visibility = _sourceExpanded
                ? Visibility.Collapsed
                : Visibility.Visible;
            _source.Visibility = _sourceExpanded
                ? Visibility.Visible
                : Visibility.Collapsed;
            _sourceCard.ToolTip = _sourceExpanded
                ? "点击收起原文"
                : "点击展开完整原文";
            UpdateCompactHeight(_currentText);
        }

        private void SetOcrDirty(bool dirty)
        {
            _ocrDirty = dirty;
            if (_sourceState != null)
            {
                _sourceState.Text = dirty ? "已修改 · 已自动固定" : "";
                _sourceState.Visibility = dirty
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            if (_sourceCard != null)
                _sourceCard.BorderBrush = dirty
                    ? Brush("#E8C98E")
                    : Brush("#D7E6EA");
            _editProtected = dirty;
            _isPinned = _manualPinned || _editProtected;
            UpdatePinGlyph();
        }

        private StackPanel BuildErrorPanel()
        {
            var panel = new StackPanel
            {
                Margin = new Thickness(5, 7, 8, 7),
                VerticalAlignment = VerticalAlignment.Center
            };
            _errorText = new TextBlock
            {
                Foreground = Brush("#A23D4A"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(_errorText);
            var buttons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _retryButton = MakeSecondaryButton("重试");
            _retryButton.Click += delegate { RetryCurrentTranslation(); };
            _openSettingsButton = MakeSecondaryButton("打开设置");
            _openSettingsButton.Click += delegate
            {
                DismissImmediately();
                var handler = SettingsRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            buttons.Children.Add(_retryButton);
            buttons.Children.Add(_openSettingsButton);
            panel.Children.Add(buttons);
            return panel;
        }

        private void ShowTranslationError(Exception error)
        {
            _translation.Text = "";
            _translation.Visibility = Visibility.Collapsed;
            _copyTranslation.Visibility = Visibility.Collapsed;
            _errorText.Text = FriendlyError(error);
            _errorPanel.Visibility = Visibility.Visible;
            _meta.Text = "翻译失败  ·  " + ProviderName(
                _activeSettings == null ? "" : _activeSettings.Provider);
        }

        private static string FriendlyError(Exception error)
        {
            string message = error == null ? "未知错误。" : error.Message;
            if (message.IndexOf("密钥", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("API 地址", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("配置", StringComparison.OrdinalIgnoreCase) >= 0)
                return "当前翻译服务尚未配置，请检查 API 地址和密钥。";
            if (message.IndexOf("超时", StringComparison.OrdinalIgnoreCase) >= 0 ||
                error is TimeoutException)
                return "连接翻译服务超时，请稍后重试。";
            if (message.IndexOf("401", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("403", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0 ||
                message.IndexOf("额度", StringComparison.OrdinalIgnoreCase) >= 0)
                return "翻译服务拒绝了请求，请检查密钥、权限或额度。";
            if (message.Length > 150) message = message.Substring(0, 150) + "…";
            return "翻译失败：" + message;
        }

        private void RetryCurrentTranslation()
        {
            string text = _ocrMode ? (_source.Text ?? "") : _currentText;
            if (string.IsNullOrWhiteSpace(text) ||
                _activeSettings == null || _activeClient == null)
                return;
            BeginTranslation(
                text,
                _anchorX,
                _anchorY,
                _anchorRect,
                _activeSettings,
                _activeClient,
                _ocrMode,
                _ocrMetaSuffix,
                false);
        }

        public bool IsPinned { get { return _isPinned; } }

        public void SetPinned(bool pinned)
        {
            _manualPinned = pinned;
            _isPinned = _manualPinned || _editProtected;
            UpdatePinGlyph();
        }

        private void TogglePinned()
        {
            SetPinned(!_manualPinned);
        }

        public void Dismiss()
        {
            if (_isDismissing) return;
            bool wasVisible = IsVisible;
            _hideTimer.Stop();
            _loadingTimer.Stop();
            _noticeTimer.Stop();
            if (_cancellation != null) _cancellation.Cancel();
            if (_modelPopup != null) _modelPopup.IsOpen = false;
            if (!wasVisible) return;
            _isDismissing = true;
            int presentation = ++_presentationVersion;
            if (ShouldAnimate())
            {
                var animation = new DoubleAnimation
                {
                    From = Opacity,
                    To = 0,
                    Duration = TimeSpan.FromMilliseconds(120),
                    FillBehavior = FillBehavior.Stop
                };
                animation.Completed += delegate
                {
                    if (_isDismissing && presentation == _presentationVersion) FinishDismiss();
                };
                BeginAnimation(OpacityProperty, animation);
            }
            else FinishDismiss();
        }

        public void DismissImmediately()
        {
            _presentationVersion++;
            bool wasVisible = IsVisible;
            _hideTimer.Stop();
            _loadingTimer.Stop();
            _noticeTimer.Stop();
            if (_cancellation != null) _cancellation.Cancel();
            if (_modelPopup != null) _modelPopup.IsOpen = false;
            BeginAnimation(OpacityProperty, null);
            _isDismissing = false;
            Opacity = 1;
            if (wasVisible)
            {
                Hide();
                var handler = Dismissed;
                if (handler != null) handler(this, EventArgs.Empty);
            }
        }

        public bool HandleEscape()
        {
            if (_modelPopup != null && _modelPopup.IsOpen)
            {
                _modelPopup.IsOpen = false;
                return true;
            }
            Dismiss();
            return true;
        }

        public void ShowTransientNotice(string message, int x, int y)
        {
            if (_cancellation != null) _cancellation.Cancel();
            _noticeTimer.Stop();
            _loadingTimer.Stop();
            _isDismissing = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            _ocrMode = false;
            _popupMode = TranslationPopupMode.Selection;
            _currentText = "";
            _translatedText = "";
            _ocrMetaSuffix = "";
            _modelChipHost.Visibility = Visibility.Collapsed;
            _sourcePanel.Visibility = Visibility.Collapsed;
            _translationPanel.Visibility = Visibility.Visible;
            _sourceState.Visibility = Visibility.Collapsed;
            _translation.Visibility = Visibility.Visible;
            _translation.Foreground = Brush("#7A5B1A");
            _translation.Text = message ?? "操作未完成。";
            _errorPanel.Visibility = Visibility.Collapsed;
            _progress.Visibility = Visibility.Collapsed;
            _loadingText.Visibility = Visibility.Collapsed;
            _meta.Text = "提示";
            SetOcrActionsVisible(false);
            _copyTranslation.Visibility = Visibility.Collapsed;
            if (WindowState == WindowState.Maximized)
                WindowState = WindowState.Normal;
            MinWidth = 300;
            MinHeight = 120;
            Width = 360;
            Height = 150;
            UpdateContentLayout();
            if (!IsVisible)
            {
                Opacity = 0;
                BeginAnimation(OpacityProperty, null);
                Show();
                PositionNear(x, y, new NativeMethods.RECT());
                AnimateOpacity(0, 1, 120);
            }
            else
            {
                PositionNear(x, y, new NativeMethods.RECT());
                Opacity = 1;
            }
            _noticeTimer.Start();
        }

        private void FinishDismiss()
        {
            _isDismissing = false;
            BeginAnimation(OpacityProperty, null);
            Opacity = 1;
            if (IsVisible) Hide();
            var handler = Dismissed;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private bool ShouldAnimate()
        {
            try { return SystemParameters.ClientAreaAnimation; }
            catch { return true; }
        }

        private void AnimateOpacity(double from, double to, int milliseconds)
        {
            if (!ShouldAnimate())
            {
                Opacity = to;
                return;
            }
            BeginAnimation(OpacityProperty,
                new DoubleAnimation(
                    from,
                    to,
                    TimeSpan.FromMilliseconds(milliseconds)));
        }

        private void RaiseVisibilityChanged()
        {
            var handler = VisibilityChanged;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private bool IsPointInsideWindow(int x, int y)
        {
            NativeMethods.RECT rect;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero && NativeMethods.GetWindowRect(handle, out rect))
                return x >= rect.Left && x < rect.Right &&
                       y >= rect.Top && y < rect.Bottom;
            return IsMouseOver;
        }

        private FrameworkElement BuildModelChip()
        {
            var buttonContent = new Grid();
            buttonContent.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            buttonContent.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
            buttonContent.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            _modelBadgeText = new TextBlock
            {
                Text = "AI",
                Foreground = Ocean,
                FontSize = 9,
                FontWeight = FontWeights.Bold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            _modelBadge = new Border
            {
                Width = 19,
                Height = 19,
                CornerRadius = new CornerRadius(9.5),
                Background = Brush("#D9F2F7"),
                Margin = new Thickness(0, 0, 8, 0),
                Child = _modelBadgeText
            };
            buttonContent.Children.Add(_modelBadge);
            _modelButtonText = new TextBlock
            {
                Text = "选择可用模型",
                Foreground = Navy,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(_modelButtonText, 1);
            buttonContent.Children.Add(_modelButtonText);
            var chevron = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 1,2 L 5,6 L 9,2"),
                Stroke = Ocean,
                StrokeThickness = 1.5,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 10,
                Height = 7,
                Stretch = Stretch.Uniform,
                Margin = new Thickness(10, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center,
                SnapsToDevicePixels = true
            };
            Grid.SetColumn(chevron, 2);
            buttonContent.Children.Add(chevron);

            _modelButton = new Button
            {
                Height = 32,
                MinWidth = 190,
                MaxWidth = 315,
                Padding = new Thickness(8, 4, 11, 4),
                Background = Brush("#E8F8FCFD"),
                BorderBrush = Brush("#B8D8EAF0"),
                BorderThickness = new Thickness(1),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
                Content = buttonContent,
                Template = RoundedButtonTemplate(16)
            };
            _modelButton.Effect =
                new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 9,
                    ShadowDepth = 1,
                    Opacity = .1,
                    Color = Color.FromRgb(6, 70, 91)
                };
            _modelButton.MouseEnter += delegate
            {
                _modelButton.Background = Brush("#FAFFFFFF");
            };
            _modelButton.MouseLeave += delegate
            {
                _modelButton.Background = Brush("#E8F8FCFD");
            };
            _modelButton.Click += delegate
            {
                if (_availableModelCount == 0)
                {
                    RequestModelSettings();
                    return;
                }
                _modelPopup.HorizontalOffset =
                    Math.Min(
                        0,
                        _modelButton.ActualWidth -
                        _modelPopupCard.Width);
                _modelPopup.IsOpen = !_modelPopup.IsOpen;
            };

            _modelMenuItems = new StackPanel();
            var menuScroll = new ScrollViewer
            {
                Content = _modelMenuItems,
                MaxHeight = 310,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility =
                    ScrollBarVisibility.Disabled
            };
            _modelPopupCard = new Border
            {
                Width = 310,
                Background = Brush("#FCFFFFFF"),
                BorderBrush = Brush("#BFD7E7EC"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(16),
                Padding = new Thickness(7),
                Child = menuScroll,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 24,
                    ShadowDepth = 6,
                    Opacity = .2,
                    Color = Color.FromRgb(5, 45, 63)
                }
            };
            _modelPopup = new Popup
            {
                AllowsTransparency = true,
                StaysOpen = false,
                Placement = PlacementMode.Bottom,
                PlacementTarget = _modelButton,
                VerticalOffset = 6,
                PopupAnimation = PopupAnimation.Fade,
                Child = _modelPopupCard
            };

            var host = new Grid
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 0, 0, 9)
            };
            host.Children.Add(_modelButton);
            host.Children.Add(_modelPopup);
            return host;
        }

        private void RefreshModelSelector(AppSettings settings)
        {
            bool visible = settings != null &&
                string.Equals(
                    settings.Provider,
                    "ModelApi",
                    StringComparison.OrdinalIgnoreCase);
            _modelChipHost.Visibility =
                visible ? Visibility.Visible : Visibility.Collapsed;
            if (!visible)
            {
                _modelPopup.IsOpen = false;
                return;
            }

            _modelPopup.IsOpen = false;
            _modelMenuItems.Children.Clear();
            _availableModelCount = 0;
            PopupModelChoice currentChoice = null;
            string[] vendors = {
                "Custom", "DeepSeek", "MiMo", "Qwen"
            };
            foreach (string vendor in vendors)
            {
                ModelConnectionSettings connection =
                    settings.GetModelConnection(vendor);
                if (!IsPopupModelAvailable(vendor, connection))
                    continue;
                var choice = new PopupModelChoice(
                    vendor,
                    connection.Model);
                _availableModelCount++;
                bool isCurrent = string.Equals(
                    vendor,
                    settings.ModelVendor,
                    StringComparison.OrdinalIgnoreCase);
                if (isCurrent)
                    currentChoice = choice;
                _modelMenuItems.Children.Add(
                    BuildModelMenuItem(choice, isCurrent));
            }

            if (_availableModelCount > 0)
            {
                _modelMenuItems.Children.Add(new Border
                {
                    Height = 1,
                    Background = Brush("#DCEAEF"),
                    Margin = new Thickness(8, 6, 8, 5)
                });
            }
            var manage = new Button
            {
                Content = new TextBlock
                {
                    Text = "管理模型…",
                    Foreground = Ocean,
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold
                },
                Tag = "ManageModels",
                Height = 36,
                Padding = new Thickness(11, 6, 11, 6),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Cursor = Cursors.Hand,
                Template = RoundedButtonTemplate(10)
            };
            manage.MouseEnter += delegate
            {
                manage.Background = Brush("#EAF7FA");
            };
            manage.MouseLeave += delegate
            {
                manage.Background = Brushes.Transparent;
            };
            manage.Click += delegate
            {
                _modelPopup.IsOpen = false;
                RequestModelSettings();
            };
            _modelMenuItems.Children.Add(manage);

            if (_availableModelCount == 0)
                SetModelChipState(
                    "没有可用模型 · 去设置",
                    true);
            else if (currentChoice == null)
                SetModelChipState(
                    "当前配置不可用 · 请选择模型",
                    true);
            else
                SetModelChipState(
                    currentChoice.ToString(),
                    false);
        }

        private Button BuildModelMenuItem(
            PopupModelChoice choice, bool selected)
        {
            var content = new Grid();
            content.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(
                new ColumnDefinition
                {
                    Width = new GridLength(1, GridUnitType.Star)
                });
            content.ColumnDefinitions.Add(
                new ColumnDefinition { Width = GridLength.Auto });

            var badge = new Border
            {
                Width = 28,
                Height = 28,
                CornerRadius = new CornerRadius(14),
                Background = selected
                    ? Brush("#CDEEF4")
                    : Brush("#EDF6F8"),
                Margin = new Thickness(0, 0, 10, 0),
                Child = new TextBlock
                {
                    Text = PopupVendorBadge(choice.Vendor),
                    Foreground = Ocean,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            };
            content.Children.Add(badge);

            var labels = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center
            };
            labels.Children.Add(new TextBlock
            {
                Text = PopupVendorName(choice.Vendor),
                Foreground = Navy,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold
            });
            labels.Children.Add(new TextBlock
            {
                Text = choice.Model,
                Foreground = Muted,
                FontSize = 10.5,
                Margin = new Thickness(0, 1, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxWidth = 205
            });
            Grid.SetColumn(labels, 1);
            content.Children.Add(labels);

            var check = new System.Windows.Shapes.Path
            {
                Data = Geometry.Parse("M 1,5 L 4,8 L 10,1"),
                Stroke = Ocean,
                StrokeThickness = 1.8,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                Width = 12,
                Height = 10,
                Stretch = Stretch.Uniform,
                Visibility = selected
                    ? Visibility.Visible
                    : Visibility.Hidden,
                Margin = new Thickness(10, 0, 3, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(check, 2);
            content.Children.Add(check);

            var item = new Button
            {
                Tag = choice,
                Height = 50,
                Margin = new Thickness(0, 1, 0, 1),
                Padding = new Thickness(9, 5, 9, 5),
                Background = selected
                    ? Brush("#E9F7FA")
                    : Brushes.Transparent,
                BorderThickness = new Thickness(0),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Cursor = Cursors.Hand,
                ToolTip = choice.ToString(),
                Content = content,
                Template = RoundedButtonTemplate(11)
            };
            item.MouseEnter += delegate
            {
                item.Background = Brush("#E3F4F7");
            };
            item.MouseLeave += delegate
            {
                item.Background = selected
                    ? Brush("#E9F7FA")
                    : Brushes.Transparent;
            };
            PopupModelChoice selectedChoice = choice;
            item.Click += delegate
            {
                _modelPopup.IsOpen = false;
                ApplyPopupModelChoice(selectedChoice);
            };
            return item;
        }

        private void SetModelChipState(
            string text, bool warning)
        {
            _modelButtonText.Text = text;
            _modelButton.ToolTip = text;
            _modelBadgeText.Text = warning ? "!" : "AI";
            _modelBadge.Background = warning
                ? Brush("#FFF0D2")
                : Brush("#D9F2F7");
            _modelBadgeText.Foreground = warning
                ? Brush("#A46717")
                : Ocean;
            _modelButton.BorderBrush = warning
                ? Brush("#E8C98E")
                : Brush("#B8D8EAF0");
        }

        private static bool IsPopupModelAvailable(
            string vendor, ModelConnectionSettings connection)
        {
            return connection != null &&
                   connection.IsUsable(vendor);
        }

        private static bool IsLocalModelEndpoint(string baseUrl)
        {
            Uri uri;
            if (!Uri.TryCreate(
                    baseUrl,
                    UriKind.Absolute,
                    out uri))
                return false;
            string host = uri.Host;
            if (string.Equals(
                    host,
                    "localhost",
                    StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(
                    ".localhost",
                    StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith(
                    ".local",
                    StringComparison.OrdinalIgnoreCase) ||
                host.IndexOf('.') < 0)
                return true;

            IPAddress address;
            if (!IPAddress.TryParse(host, out address))
                return false;
            if (IPAddress.IsLoopback(address))
                return true;
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                return bytes[0] == 10 ||
                       (bytes[0] == 172 &&
                        bytes[1] >= 16 &&
                        bytes[1] <= 31) ||
                       (bytes[0] == 192 &&
                        bytes[1] == 168) ||
                       (bytes[0] == 169 &&
                        bytes[1] == 254);
            }
            return address.IsIPv6LinkLocal ||
                   address.IsIPv6SiteLocal;
        }

        private void ApplyPopupModelChoice(PopupModelChoice choice)
        {
            if (choice == null ||
                _activeSettings == null ||
                _activeClient == null)
                return;
            ModelConnectionSettings connection =
                _activeSettings.GetModelConnection(choice.Vendor);
            if (!IsPopupModelAvailable(choice.Vendor, connection))
            {
                RefreshModelSelector(_activeSettings);
                return;
            }
            _activeSettings.Provider = "ModelApi";
            _activeSettings.ModelVendor = choice.Vendor;
            _activeSettings.ModelBaseUrl = connection.BaseUrl;
            _activeSettings.ModelName = connection.Model;
            _activeSettings.ModelApiKey = connection.ApiKey;
            try { _activeSettings.Save(); }
            catch (Exception saveError)
            {
                DiagnosticLog.Write(
                    "Popup model selection save failed; type=" +
                    saveError.GetType().Name);
            }

            string text = _source.Text ?? "";
            if (string.IsNullOrWhiteSpace(text)) return;
            BeginTranslation(
                text,
                _anchorX,
                _anchorY,
                _anchorRect,
                _activeSettings,
                _activeClient,
                _ocrMode,
                _ocrMetaSuffix,
                false);
        }

        private void RequestModelSettings()
        {
            DismissImmediately();
            var handler = ModelSettingsRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private Border BuildHeader()
        {
            var header = new Border
            {
                Background = Brush("#F0FAFB"),
                BorderBrush = Brush("#D5E8EB"),
                BorderThickness = new Thickness(0, 0, 0, 1),
                CornerRadius = new CornerRadius(18, 18, 0, 0),
                Padding = new Thickness(12, 7, 10, 7),
                Cursor = Cursors.SizeAll
            };
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                Point point = e.GetPosition(header);
                if (point.X >= header.ActualWidth - 145) return;
                if (e.ClickCount == 2)
                {
                    ToggleMaximize();
                    return;
                }
                if (e.ChangedButton == MouseButton.Left &&
                    WindowState == WindowState.Normal)
                    DragMove();
            };

            var panel = new DockPanel();
            header.Child = panel;

            _collapseButton = MakeGhostHeaderButton(
                "M 2,4 L 7,9 L 12,4",
                "收起 (Esc)");
            _collapseButton.Click += delegate { Dismiss(); };
            DockPanel.SetDock(_collapseButton, Dock.Right);
            panel.Children.Add(_collapseButton);

            _expandButton = MakeGhostHeaderButton(
                ExpandGeometry(),
                "放大");
            _expandGlyph =
                _expandButton.Content as System.Windows.Shapes.Path;
            _expandButton.Click += delegate { ToggleMaximize(); };
            DockPanel.SetDock(_expandButton, Dock.Right);
            panel.Children.Add(_expandButton);

            _pinButton = MakeGhostHeaderButton(PinGeometry(), "固定");
            _pinGlyph = _pinButton.Content as System.Windows.Shapes.Path;
            _pinButton.Click += delegate { TogglePinned(); };
            _pinButton.MouseLeave += delegate { UpdatePinGlyph(); };
            DockPanel.SetDock(_pinButton, Dock.Right);
            panel.Children.Add(_pinButton);

            _headerModelSlot = new Border { VerticalAlignment = VerticalAlignment.Center };
            DockPanel.SetDock(_headerModelSlot, Dock.Right);
            panel.Children.Add(_headerModelSlot);

            var logo = new Image
            {
                Width = 28,
                Height = 28,
                Margin = new Thickness(0, 0, 8, 0),
                Stretch = Stretch.Uniform,
                Source = new BitmapImage(new Uri(
                    "pack://application:,,,/Sharkey;component/assets/shark-logo.png"))
            };
            DockPanel.SetDock(logo, Dock.Left);
            panel.Children.Add(logo);

            var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            _brandTitle = new TextBlock
            {
                Text = "鲨译",
                Foreground = Navy,
                FontSize = 15.5,
                FontWeight = FontWeights.SemiBold
            };
            identity.Children.Add(_brandTitle);
            _meta = new TextBlock
            {
                Text = "正在翻译…",
                Foreground = Muted,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            identity.Children.Add(_meta);
            panel.Children.Add(identity);
            return header;
        }

        private DockPanel BuildActions()
        {
            var actions = new DockPanel { Margin = new Thickness(0, 9, 0, 0) };
            _copyTranslation = MakeSecondaryButton("复制译文");
            _copyTranslation.Click += delegate
            {
                if (!string.IsNullOrEmpty(_translatedText))
                {
                    Clipboard.SetText(_translatedText);
                    _copyTranslation.Content = "已复制";
                    var reset = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.2)
                    };
                    reset.Tick += delegate
                    {
                        reset.Stop();
                        _copyTranslation.Content = "复制译文";
                    };
                    reset.Start();
                }
            };
            DockPanel.SetDock(_copyTranslation, Dock.Right);
            actions.Children.Add(_copyTranslation);

            var ocrActions = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left
            };
            _copySource = MakeSecondaryButton("复制原文");
            _copySource.Click += delegate
            {
                if (!string.IsNullOrEmpty(_source.Text))
                    Clipboard.SetText(_source.Text);
            };
            _retranslate = MakeSecondaryButton("重新翻译");
            _retranslate.Click += delegate
            {
                string edited = (_source.Text ?? "").Trim();
                if (edited.Length == 0) return;
                BeginTranslation(
                    edited,
                    _anchorX,
                    _anchorY,
                    _anchorRect,
                    _activeSettings,
                    _activeClient,
                    true,
                    _ocrMetaSuffix,
                    false);
            };
            _recapture = MakeSecondaryButton("重新框选");
            _recapture.Click += delegate
            {
                DismissImmediately();
                var handler = OcrRecaptureRequested;
                if (handler != null) handler(this, EventArgs.Empty);
            };
            ocrActions.Children.Add(_copySource);
            ocrActions.Children.Add(_retranslate);
            ocrActions.Children.Add(_recapture);
            actions.Children.Add(ocrActions);
            return actions;
        }

        private void SetOcrActionsVisible(bool visible)
        {
            Visibility state = visible ? Visibility.Visible : Visibility.Collapsed;
            _copySource.Visibility = state;
            _retranslate.Visibility = state;
            _recapture.Visibility = state;
        }

        private IntPtr WindowProc(
            IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // WM_SIZING only represents interactive sizing, not our auto-fit.
            if (message == 0x0214) _manualLayout = true;
            if (message != WmNcHitTest || WindowState != WindowState.Normal)
                return IntPtr.Zero;

            long packed = lParam.ToInt64();
            int screenX = unchecked((short)(packed & 0xFFFF));
            int screenY = unchecked((short)((packed >> 16) & 0xFFFF));
            NativeMethods.RECT windowRect;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (!NativeMethods.GetWindowRect(handle, out windowRect))
                return IntPtr.Zero;
            double scale = NativeMethods.GetWindowDpi(handle) / 96.0;
            int edge = Math.Max(8, (int)Math.Round(8 * scale));
            bool left = screenX - windowRect.Left <= edge;
            bool right = windowRect.Right - screenX <= edge;
            bool top = screenY - windowRect.Top <= edge;
            bool bottom = windowRect.Bottom - screenY <= edge;

            int hit = 0;
            if (left && top) hit = HtTopLeft;
            else if (right && top) hit = HtTopRight;
            else if (left && bottom) hit = HtBottomLeft;
            else if (right && bottom) hit = HtBottomRight;
            else if (left) hit = HtLeft;
            else if (right) hit = HtRight;
            else if (top) hit = HtTop;
            else if (bottom) hit = HtBottom;

            if (hit == 0) return IntPtr.Zero;
            handled = true;
            return new IntPtr(hit);
        }

        private void ToggleMaximize()
        {
            WindowState = WindowState == WindowState.Maximized
                ? WindowState.Normal
                : WindowState.Maximized;
            UpdateExpandGlyph();
        }

        private void UpdateExpandGlyph()
        {
            if (_expandGlyph == null ||
                _expandButton == null)
                return;
            bool maximized =
                WindowState == WindowState.Maximized;
            _expandGlyph.Data = maximized
                ? RestoreGeometry()
                : ExpandGeometry();
            _expandButton.ToolTip =
                maximized ? "还原" : "放大";
            UpdatePinGlyph();
        }

        private void UpdatePinGlyph()
        {
            if (_pinGlyph == null || _pinButton == null) return;
            _pinGlyph.Stroke = _isPinned ? Ocean : Muted;
            _pinButton.ToolTip = _manualPinned ? "取消手动固定" :
                _editProtected ? "编辑保护中；点击可保持手动固定" : "固定";
        }

        private void ClampCurrentPosition()
        {
            if (!IsVisible || WindowState != WindowState.Normal) return;
            IntPtr handle = new WindowInteropHelper(this).Handle;
            NativeMethods.RECT rect;
            if (!NativeMethods.GetWindowRect(handle, out rect)) return;
            var screen = System.Windows.Forms.Screen.FromHandle(handle).WorkingArea;
            int left = Math.Max(screen.Left + 10, Math.Min(rect.Left, screen.Right - (rect.Right - rect.Left) - 10));
            int top = Math.Max(screen.Top + 10, Math.Min(rect.Top, screen.Bottom - (rect.Bottom - rect.Top) - 10));
            if (left != rect.Left || top != rect.Top)
                NativeMethods.SetWindowPos(handle, IntPtr.Zero, left, top, 0, 0,
                    0x0001 | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_NOZORDER);
        }

        private void PositionNear(
            int x, int y, NativeMethods.RECT screenBounds)
        {
            if (WindowState == WindowState.Maximized) return;
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(x, y)).WorkingArea;
            uint targetDpi = NativeMethods.GetMonitorDpi(x, y);
            if (targetDpi == 0)
                targetDpi = NativeMethods.GetWindowDpi(
                    new WindowInteropHelper(this).Handle);
            double dpi = targetDpi;
            if (dpi < 96) dpi = 96;
            double scale = dpi / 96.0;
            int width = (int)Math.Ceiling(
                (Width > 0 ? Width : ActualWidth) * scale);
            int height = (int)Math.Ceiling(
                (Height > 0 ? Height : ActualHeight) * scale);
            // A compact card should never be placed beyond a very small
            // work area (remote desktop sessions and taskbar-heavy layouts
            // can be shorter than the normal 360 DIP maximum).
            width = Math.Min(width, Math.Max(1, screen.Width - 20));
            height = Math.Min(height, Math.Max(1, screen.Height - 20));
            int left;
            int top;
            bool hasBounds = screenBounds.Right > screenBounds.Left &&
                             screenBounds.Bottom > screenBounds.Top;
            if (hasBounds)
            {
                left = screenBounds.Right + 14;
                top = screenBounds.Top;
                if (left + width > screen.Right - 10)
                    left = screenBounds.Left - width - 14;
                if (top + height > screen.Bottom - 10)
                    top = screenBounds.Bottom - height;
            }
            else
            {
                left = x + 16;
                top = y + 16;
                if (top + height > screen.Bottom - 10)
                    top = y - height - 16;
            }
            left = Math.Max(screen.Left + 10,
                Math.Min(left, screen.Right - width - 10));
            top = Math.Max(screen.Top + 10,
                Math.Min(top, screen.Bottom - height - 10));
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle != IntPtr.Zero &&
                NativeMethods.SetWindowPos(
                    handle,
                    IntPtr.Zero,
                    left,
                    top,
                    width,
                    height,
                    NativeMethods.SWP_NOACTIVATE |
                    NativeMethods.SWP_NOZORDER))
                return;
            // The native path above keeps negative and mixed-DPI monitor
            // coordinates in physical pixels.  The WPF fallback is useful
            // before an HWND exists or when a restricted desktop rejects the
            // positioning call.
            Left = left / scale;
            Top = top / scale;
        }

        private static string CompactSourceText(string text)
        {
            string value = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
            if (value.Length > 180) value = value.Substring(0, 180) + "…";
            return value;
        }

        private static string FormatResultMeta(
            TranslationResult result, AppSettings settings)
        {
            string target = result == null ||
                            string.IsNullOrEmpty(result.EffectiveTargetLanguage)
                ? (settings == null ? "" : settings.TargetLanguage)
                : result.EffectiveTargetLanguage;
            string source = result == null ? "" : result.DetectedLanguage;
            string direction = string.IsNullOrWhiteSpace(source)
                ? "→ " + LanguageName(target)
                : LanguageName(source) + " → " + LanguageName(target);
            return (result == null ? "鲨译" : result.Provider) +
                   "  ·  " + direction;
        }

        private static TextBox MakeSelectableTextBox(
            double fontSize, Brush foreground, double lineHeight)
        {
            var box = new TextBox
            {
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                AcceptsReturn = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                BorderThickness = new Thickness(0),
                Background = Brushes.White,
                Foreground = foreground,
                FontSize = fontSize,
                Padding = new Thickness(1, 3, 7, 3),
                Cursor = Cursors.IBeam
            };
            TextBlock.SetLineHeight(box, lineHeight);
            return box;
        }

        private static TextBlock SectionLabel(string text)
        {
            return new TextBlock
            {
                Text = text,
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = Ocean
            };
        }

        private static FrameworkElement BuildResizeGrip()
        {
            var grip = new Canvas
            {
                Width = 16,
                Height = 16,
                IsHitTestVisible = false,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 3, 3),
                Opacity = .48
            };
            double[] points = { 3, 11, 7, 7, 11, 3 };
            for (int i = 0; i < points.Length; i += 2)
            {
                var dot = new Ellipse
                {
                    Width = 2.4,
                    Height = 2.4,
                    Fill = Brush("#769AA8")
                };
                Canvas.SetLeft(dot, points[i]);
                Canvas.SetTop(dot, points[i + 1]);
                grip.Children.Add(dot);
            }
            return grip;
        }

        private static Button MakeGhostHeaderButton(
            string geometry, string tooltip)
        {
            return MakeGhostHeaderButton(
                Geometry.Parse(geometry), tooltip);
        }

        private static Button MakeGhostHeaderButton(
            Geometry geometry, string tooltip)
        {
            var glyph = new System.Windows.Shapes.Path
            {
                Data = geometry,
                Stroke = Muted,
                StrokeThickness = 1.45,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                StrokeLineJoin = PenLineJoin.Round,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform,
                SnapsToDevicePixels = true
            };
            var button = new Button
            {
                Content = glyph,
                ToolTip = tooltip,
                Width = 30,
                Height = 30,
                Margin = new Thickness(4, 0, 0, 0),
                Foreground = Muted,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(8),
                Template = RoundedButtonTemplate(15)
            };
            button.MouseEnter += delegate
            {
                button.Background = Brush("#DCEEF1");
                glyph.Stroke = Ocean;
            };
            button.MouseLeave += delegate
            {
                button.Background = Brushes.Transparent;
                glyph.Stroke = Muted;
            };
            button.PreviewMouseLeftButtonDown += delegate
            {
                button.Background = Brush("#C9E6EA");
            };
            return button;
        }

        private static Geometry ExpandGeometry()
        {
            return Geometry.Parse(
                "M 1,5 L 1,1 L 5,1 " +
                "M 9,1 L 13,1 L 13,5 " +
                "M 13,9 L 13,13 L 9,13 " +
                "M 5,13 L 1,13 L 1,9");
        }

        private static Geometry RestoreGeometry()
        {
            return Geometry.Parse(
                "M 4,1 L 13,1 L 13,10 L 10,10 " +
                "M 1,4 L 10,4 L 10,13 L 1,13 Z");
        }

        private static Geometry PinGeometry()
        {
            return Geometry.Parse(
                "M 5,2 L 11,2 L 10,7 L 13,10 L 9,10 L 9,14 " +
                "L 7,14 L 7,10 L 3,10 L 6,7 Z");
        }

        private static Button MakePrimaryButton(string text)
        {
            return new Button
            {
                Content = text,
                MinWidth = 96,
                Height = 32,
                Padding = new Thickness(12, 5, 12, 5),
                Background = Ocean,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Cursor = Cursors.Hand,
                Template = RoundedButtonTemplate(10)
            };
        }

        private static Button MakeSecondaryButton(string text)
        {
            return new Button
            {
                Content = text,
                MinWidth = 70,
                Height = 32,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(9, 5, 9, 5),
                Background = Brush("#EFF7FA"),
                Foreground = Ocean,
                BorderBrush = Brush("#C9E1E8"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                FontSize = 12,
                Cursor = Cursors.Hand,
                Template = RoundedButtonTemplate(9)
            };
        }

        private static ControlTemplate RoundedButtonTemplate(
            double cornerRadius)
        {
            var border = new FrameworkElementFactory(typeof(Border));
            border.SetBinding(
                Border.BackgroundProperty,
                new Binding("Background")
                {
                    RelativeSource =
                        new RelativeSource(
                            RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderBrushProperty,
                new Binding("BorderBrush")
                {
                    RelativeSource =
                        new RelativeSource(
                            RelativeSourceMode.TemplatedParent)
                });
            border.SetBinding(
                Border.BorderThicknessProperty,
                new Binding("BorderThickness")
                {
                    RelativeSource =
                        new RelativeSource(
                            RelativeSourceMode.TemplatedParent)
                });
            border.SetValue(
                Border.CornerRadiusProperty,
                new CornerRadius(cornerRadius));
            var presenter =
                new FrameworkElementFactory(typeof(ContentPresenter));
            presenter.SetBinding(
                ContentPresenter.ContentProperty,
                new Binding("Content")
                {
                    RelativeSource =
                        new RelativeSource(
                            RelativeSourceMode.TemplatedParent)
                });
            presenter.SetBinding(
                ContentPresenter.MarginProperty,
                new Binding("Padding")
                {
                    RelativeSource =
                        new RelativeSource(
                            RelativeSourceMode.TemplatedParent)
                });
            presenter.SetValue(
                ContentPresenter.VerticalAlignmentProperty,
                VerticalAlignment.Center);
            presenter.SetValue(
                ContentPresenter.HorizontalAlignmentProperty,
                HorizontalAlignment.Stretch);
            border.AppendChild(presenter);
            return new ControlTemplate(typeof(Button))
            {
                VisualTree = border
            };
        }

        private static SolidColorBrush Brush(string hex)
        {
            return (SolidColorBrush)new BrushConverter().ConvertFromString(hex);
        }

        private static string LanguageName(string code)
        {
            switch (code)
            {
                case "zh-Hans":
                case "zh-CN":
                case "zh-SG": return "简体中文";
                case "zh-Hant":
                case "zh-TW":
                case "zh-HK": return "繁體中文";
                case "en":
                case "en-US":
                case "en-GB": return "English";
                case "ja":
                case "ja-JP": return "日本語";
                case "ko":
                case "ko-KR": return "한국어";
                case "fr": return "Français";
                case "de": return "Deutsch";
                case "es": return "Español";
                default: return code;
            }
        }

        private static string ProviderName(string provider)
        {
            if (provider == "GoogleFree") return "Google 免费";
            if (provider == "MicrosoftFree") return "Microsoft 免费";
            if (provider == "Microsoft") return "Microsoft 官方 API";
            if (provider == "Google") return "Google Cloud API";
            if (provider == "ModelApi") return "AI 模型 API";
            return provider;
        }

        private static string PopupVendorName(string vendor)
        {
            if (vendor == "DeepSeek") return "DeepSeek";
            if (vendor == "MiMo") return "小米 MiMo";
            if (vendor == "Qwen") return "Qwen";
            return "自定义";
        }

        private static string PopupVendorBadge(string vendor)
        {
            if (vendor == "DeepSeek") return "D";
            if (vendor == "MiMo") return "M";
            if (vendor == "Qwen") return "Q";
            return "AI";
        }

        private sealed class PopupModelChoice
        {
            public readonly string Vendor;
            public readonly string Model;

            public PopupModelChoice(
                string vendor, string model)
            {
                Vendor = vendor;
                Model = model;
            }

            public override string ToString()
            {
                return PopupVendorName(Vendor) +
                       " · " + Model;
            }
        }
    }
}
