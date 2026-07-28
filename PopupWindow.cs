using System;
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
        private CancellationTokenSource _cancellation;
        private string _translatedText = "";
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

        public PopupWindow()
        {
            Title = "鲨译 Sharkey";
            Width = 480;
            Height = 520;
            MinWidth = 370;
            MinHeight = 330;
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

            _hideTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _hideTimer.Tick += delegate { _hideTimer.Stop(); Hide(); };
            MouseEnter += delegate { _hideTimer.Stop(); };
            MouseLeave += delegate
            {
                if (IsVisible && !IsKeyboardFocusWithin) _hideTimer.Start();
            };
            GotKeyboardFocus += delegate { _hideTimer.Stop(); };
            LostKeyboardFocus += delegate
            {
                if (IsVisible && !IsMouseOver) _hideTimer.Start();
            };
            StateChanged += delegate { UpdateExpandGlyph(); };

            var card = new Border
            {
                Background = Brush("#F7FFFFFF"),
                CornerRadius = new CornerRadius(20),
                BorderBrush = Brush("#D9FFFFFF"),
                BorderThickness = new Thickness(1),
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    BlurRadius = 32,
                    ShadowDepth = 7,
                    Opacity = .19,
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
            Grid.SetRow(header, 0);
            root.Children.Add(header);

            var body = new Grid { Margin = new Thickness(18, 14, 18, 15) };
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star),
                MinHeight = 64
            });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(2, GridUnitType.Star),
                MinHeight = 105
            });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            body.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _modelChipHost = BuildModelChip();
            Grid.SetRow(_modelChipHost, 0);
            body.Children.Add(_modelChipHost);

            TextBlock sourceLabel = SectionLabel("原文");
            Grid.SetRow(sourceLabel, 1);
            body.Children.Add(sourceLabel);
            var sourceCard = new Border
            {
                Background = Paper,
                BorderBrush = Brush("#E0EDF2"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(3),
                Margin = new Thickness(0, 6, 0, 12)
            };
            _source = MakeSelectableTextBox(13, Muted, 20);
            _source.Background = Paper;
            _source.Padding = new Thickness(8, 5, 8, 5);
            sourceCard.Child = _source;
            Grid.SetRow(sourceCard, 2);
            body.Children.Add(sourceCard);

            TextBlock translationLabel = SectionLabel("译文");
            Grid.SetRow(translationLabel, 3);
            body.Children.Add(translationLabel);

            var translationCard = new Border
            {
                Background = Brush("#F9FFFFFF"),
                BorderBrush = Brush("#D9EAF0"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(8, 5, 3, 5),
                Margin = new Thickness(0, 6, 0, 0)
            };
            _translation = MakeSelectableTextBox(17, Navy, 27);
            _translation.FontWeight = FontWeights.Medium;
            _translation.Background = Brushes.Transparent;
            translationCard.Child = _translation;
            Grid.SetRow(translationCard, 4);
            body.Children.Add(translationCard);

            _progress = new ProgressBar
            {
                IsIndeterminate = true,
                Height = 3,
                Margin = new Thickness(0, 9, 0, 0),
                Foreground = Cyan,
                Background = Brush("#E1F2F5")
            };
            Grid.SetRow(_progress, 5);
            body.Children.Add(_progress);

            DockPanel actions = BuildActions();
            Grid.SetRow(actions, 6);
            body.Children.Add(actions);

            Grid.SetRow(body, 1);
            root.Children.Add(body);
            _resizeGrip = BuildResizeGrip();
            Grid.SetRowSpan(_resizeGrip, 2);
            root.Children.Add(_resizeGrip);
            Content = card;
        }

        public void Translate(
            string text, int x, int y, AppSettings settings, TranslationClient client)
        {
            BeginTranslation(
                text, x, y, settings, client, false, "", true);
        }

        public void TranslateOcr(
            OcrRecognitionResult result, int x, int y,
            AppSettings settings, TranslationClient client)
        {
            string suffix = "  ·  " + result.Engine;
            if (!string.IsNullOrEmpty(result.Language))
                suffix += " (" + result.Language + ")";
            BeginTranslation(
                result.Text, x, y, settings, client, true, suffix, true);
        }

        private async void BeginTranslation(
            string text, int x, int y, AppSettings settings,
            TranslationClient client, bool ocrMode, string ocrMetaSuffix,
            bool reposition)
        {
            if (_cancellation != null) _cancellation.Cancel();
            _cancellation = new CancellationTokenSource();
            CancellationTokenSource activeCancellation = _cancellation;
            _activeSettings = settings;
            _activeClient = client;
            _ocrMode = ocrMode;
            _ocrMetaSuffix = ocrMetaSuffix ?? "";
            RefreshModelSelector(settings);
            _hideTimer.Stop();
            _source.Text = text;
            _source.IsReadOnly = !ocrMode;
            _source.ScrollToHome();
            SetOcrActionsVisible(ocrMode);
            _translation.Text = "";
            _translation.Foreground = Navy;
            _translatedText = "";
            _meta.Text = ProviderName(settings.Provider) + "  ·  " +
                         LanguageName(settings.TargetLanguage) +
                         _ocrMetaSuffix;
            _progress.Visibility = Visibility.Visible;
            if (reposition) PositionNear(x, y);
            if (!IsVisible)
            {
                Opacity = 0;
                Show();
                BeginAnimation(OpacityProperty,
                    new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
            }

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
                    });
                DiagnosticLog.Write(
                    "Translation succeeded; provider=" + result.Provider +
                    "; characters=" + result.Text.Length);
                _translatedText = result.Text;
                _translation.Text = result.Text;
                _translation.ScrollToHome();
                _meta.Text = result.Provider +
                    (string.IsNullOrEmpty(result.DetectedLanguage)
                        ? ""
                        : "  ·  " + result.DetectedLanguage) +
                    " → " + LanguageName(settings.TargetLanguage) +
                    _ocrMetaSuffix;
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                DiagnosticLog.Write("Translation failed; type=" + ex.GetType().Name);
                _translation.Text = ex.Message;
                _translation.Foreground = Brush("#C23D4B");
            }
            finally
            {
                _progress.Visibility = Visibility.Collapsed;
                if (!string.IsNullOrEmpty(_translatedText))
                    _translation.Foreground = Navy;
                _hideTimer.Stop();
                if (!IsMouseOver && !IsKeyboardFocusWithin) _hideTimer.Start();
            }
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
                0,
                0,
                _activeSettings,
                _activeClient,
                _ocrMode,
                _ocrMetaSuffix,
                false);
        }

        private void RequestModelSettings()
        {
            _hideTimer.Stop();
            Hide();
            var handler = ModelSettingsRequested;
            if (handler != null) handler(this, EventArgs.Empty);
        }

        private Border BuildHeader()
        {
            var header = new Border
            {
                Background = new LinearGradientBrush(
                    Color.FromArgb(244, 15, 89, 116),
                    Color.FromArgb(226, 34, 163, 179),
                    16),
                CornerRadius = new CornerRadius(19, 19, 0, 0),
                Padding = new Thickness(15, 9, 11, 9),
                Cursor = Cursors.SizeAll
            };
            header.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                Point point = e.GetPosition(header);
                if (point.X >= header.ActualWidth - 76) return;
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
                "收起");
            _collapseButton.Click += delegate
            {
                _hideTimer.Stop();
                _modelPopup.IsOpen = false;
                Hide();
            };
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

            var logo = new Image
            {
                Width = 36,
                Height = 36,
                Margin = new Thickness(0, 0, 9, 0),
                Stretch = Stretch.Uniform,
                Source = new BitmapImage(new Uri(
                    "pack://application:,,,/Sharkey;component/assets/shark-logo.png"))
            };
            DockPanel.SetDock(logo, Dock.Left);
            panel.Children.Add(logo);

            var identity = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            identity.Children.Add(new TextBlock
            {
                Text = "鲨译",
                Foreground = Brushes.White,
                FontSize = 17,
                FontWeight = FontWeights.Bold
            });
            _meta = new TextBlock
            {
                Text = "正在翻译…",
                Foreground = Brush("#BFEAF1"),
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
            var actions = new DockPanel { Margin = new Thickness(0, 11, 0, 0) };
            _copyTranslation = MakePrimaryButton("复制译文");
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
                    edited, 0, 0, _activeSettings, _activeClient,
                    true, _ocrMetaSuffix, false);
            };
            _recapture = MakeSecondaryButton("重新框选");
            _recapture.Click += delegate
            {
                _hideTimer.Stop();
                Hide();
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
            if (message != WmNcHitTest || WindowState != WindowState.Normal)
                return IntPtr.Zero;

            long packed = lParam.ToInt64();
            int screenX = unchecked((short)(packed & 0xFFFF));
            int screenY = unchecked((short)((packed >> 16) & 0xFFFF));
            Point point = PointFromScreen(new Point(screenX, screenY));
            const double edge = 8;
            bool left = point.X <= edge;
            bool right = point.X >= ActualWidth - edge;
            bool top = point.Y <= edge;
            bool bottom = point.Y >= ActualHeight - edge;

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
        }

        private void PositionNear(int x, int y)
        {
            var screen = System.Windows.Forms.Screen.FromPoint(
                new System.Drawing.Point(x, y)).WorkingArea;
            double currentWidth = ActualWidth > 0 ? ActualWidth : Width;
            double currentHeight = ActualHeight > 0 ? ActualHeight : Height;
            Left = Math.Max(
                screen.Left + 10, Math.Min(x + 16, screen.Right - currentWidth - 10));
            Top = y + 20;
            if (Top + currentHeight > screen.Bottom)
                Top = Math.Max(screen.Top + 10, y - currentHeight - 15);
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
                Stroke = Brushes.White,
                StrokeThickness = 1.55,
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
                Foreground = Brushes.White,
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand,
                Padding = new Thickness(8),
                Template = RoundedButtonTemplate(15)
            };
            button.MouseEnter += delegate
            {
                button.Background = Brush("#2AFFFFFF");
            };
            button.MouseLeave += delegate
            {
                button.Background = Brushes.Transparent;
            };
            button.PreviewMouseLeftButtonDown += delegate
            {
                button.Background = Brush("#3DFFFFFF");
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

        private static Button MakePrimaryButton(string text)
        {
            return new Button
            {
                Content = text,
                MinWidth = 104,
                Height = 34,
                Padding = new Thickness(14, 5, 14, 5),
                Background = Ocean,
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
        }

        private static Button MakeSecondaryButton(string text)
        {
            return new Button
            {
                Content = text,
                MinWidth = 70,
                Height = 34,
                Margin = new Thickness(0, 0, 6, 0),
                Padding = new Thickness(9, 5, 9, 5),
                Background = Brush("#EFF7FA"),
                Foreground = Ocean,
                BorderBrush = Brush("#C9E1E8"),
                BorderThickness = new Thickness(1),
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
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
                case "zh-Hans": return "简体中文";
                case "zh-Hant": return "繁體中文";
                case "en": return "English";
                case "ja": return "日本語";
                case "ko": return "한국어";
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
