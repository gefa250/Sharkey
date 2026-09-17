using System;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace GlobalTranslator
{
    internal sealed class UpdateWindow : Window
    {
        private static readonly Brush Navy = Brush("#0B2942");
        private static readonly Brush Ocean = Brush("#087EA4");
        private static readonly Brush Muted = Brush("#65798B");
        private static readonly Brush Line = Brush("#DCE9EF");

        private readonly UpdateInfo _update;
        private readonly UpdateService _service;
        private readonly Button _install;
        private readonly Button _later;
        private readonly TextBlock _status;
        private CancellationTokenSource _cancellation;
        private bool _installing;

        public UpdateWindow(
            UpdateInfo update,
            UpdateService service)
        {
            if (update == null) throw new ArgumentNullException("update");
            if (service == null) throw new ArgumentNullException("service");
            _update = update;
            _service = service;

            Title = "鲨译更新";
            Width = 520;
            Height = 420;
            MinWidth = 440;
            MinHeight = 340;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = Brushes.White;
            FontFamily = new FontFamily("Microsoft YaHei UI");

            var root = new Grid { Margin = new Thickness(22) };
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = new GridLength(1, GridUnitType.Star)
            });
            root.RowDefinitions.Add(new RowDefinition
            {
                Height = GridLength.Auto
            });

            var heading = new StackPanel();
            heading.Children.Add(new TextBlock
            {
                Text = "发现新版本 " + update.Version,
                FontSize = 20,
                FontWeight = FontWeights.SemiBold,
                Foreground = Navy
            });
            heading.Children.Add(new TextBlock
            {
                Text = "当前版本 " + VersionInfo.SemanticVersion,
                FontSize = 11.5,
                Foreground = Muted,
                Margin = new Thickness(0, 5, 0, 14)
            });
            root.Children.Add(heading);

            var notes = new TextBox
            {
                Text = string.IsNullOrWhiteSpace(update.Notes)
                    ? "该版本没有附加更新说明。"
                    : update.Notes,
                IsReadOnly = true,
                IsReadOnlyCaretVisible = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = Line,
                BorderThickness = new Thickness(1),
                Padding = new Thickness(12),
                Background = Brush("#F7FAFB"),
                Foreground = Navy,
                FontSize = 12
            };
            Grid.SetRow(notes, 1);
            root.Children.Add(notes);

            var footer = new Grid
            {
                Margin = new Thickness(0, 16, 0, 0)
            };
            footer.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = new GridLength(1, GridUnitType.Star)
            });
            footer.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            footer.ColumnDefinitions.Add(new ColumnDefinition
            {
                Width = GridLength.Auto
            });
            _status = new TextBlock
            {
                Text = "下载后会校验文件并自动重启鲨译。",
                Foreground = Muted,
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                TextWrapping = TextWrapping.Wrap
            };
            footer.Children.Add(_status);
            _later = MakeButton("稍后", false);
            _later.Margin = new Thickness(12, 0, 8, 0);
            _later.Click += delegate { Close(); };
            Grid.SetColumn(_later, 1);
            footer.Children.Add(_later);
            _install = MakeButton("立即更新", true);
            _install.Click += InstallClick;
            Grid.SetColumn(_install, 2);
            footer.Children.Add(_install);
            Grid.SetRow(footer, 2);
            root.Children.Add(footer);
            Content = root;
        }

        private async void InstallClick(
            object sender,
            RoutedEventArgs e)
        {
            if (_installing) return;
            _installing = true;
            _install.IsEnabled = false;
            _later.IsEnabled = false;
            _cancellation = new CancellationTokenSource();
            try
            {
                var progress = new Progress<int>(delegate(int value)
                {
                    _status.Text = "正在下载更新… " + value + "%";
                });
                string downloaded = await _service.DownloadAndVerifyAsync(
                    _update,
                    progress,
                    _cancellation.Token);
                _status.Text = "校验完成，正在重启…";
                UpdateService.LaunchUpdater(downloaded);
                Application.Current.Shutdown();
            }
            catch (OperationCanceledException)
            {
                _status.Text = "更新已取消。";
            }
            catch (Exception error)
            {
                _status.Text = "更新失败：" + error.Message;
                _install.IsEnabled = true;
                _later.IsEnabled = true;
                _installing = false;
            }
        }

        protected override void OnClosing(
            System.ComponentModel.CancelEventArgs e)
        {
            if (_installing && _cancellation != null)
                _cancellation.Cancel();
            base.OnClosing(e);
        }

        private static Button MakeButton(
            string text,
            bool primary)
        {
            return new Button
            {
                Content = text,
                Width = primary ? 104 : 78,
                Height = 38,
                Background = primary ? Ocean : Brushes.White,
                Foreground = primary ? Brushes.White : Navy,
                BorderBrush = primary ? Ocean : Line,
                BorderThickness = new Thickness(1),
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                Cursor = Cursors.Hand
            };
        }

        private static Brush Brush(string color)
        {
            return (Brush)new BrushConverter().ConvertFromString(color);
        }
    }
}
