using System;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Prism.Events;
using seamless_loop_music.Events;
using seamless_loop_music.Services;
using seamless_loop_music.UI.Views;
using seamless_loop_music.Models;

namespace seamless_loop_music
{
    public partial class MainWindow : Window
    {
        private readonly IAppStateService _appState;
        private readonly INotifyIconService _notifyIconService;
        private readonly IPlayerService _playerService;
        private readonly IEventAggregator _eventAggregator;
        private bool _onboardingShownThisSession;
        private bool _closeDialogOpen;

        public static readonly DependencyProperty MaximizedPaddingProperty =
            DependencyProperty.Register("MaximizedPadding", typeof(Thickness), typeof(MainWindow), new PropertyMetadata(new Thickness(0)));

        public Thickness MaximizedPadding
        {
            get { return (Thickness)GetValue(MaximizedPaddingProperty); }
            set { SetValue(MaximizedPaddingProperty, value);}
        }
        private void CalculateMaximizedPadding()
        {
            double screenHeight = SystemParameters.PrimaryScreenHeight;

            // 获取工作区高度（不包含任务栏）
            double workAreaHeight = SystemParameters.WorkArea.Height;

            // 计算任务栏高度
            double taskBarHeight = screenHeight - workAreaHeight;
            this.MaximizedPadding = new Thickness(8, 8, 8, taskBarHeight + 8);
        }
        public MainWindow(ITaskbarService taskbarService, 
            IAppStateService appState,
            INotifyIconService notifyIconService, 
            IPlayerService playerService,
            IEventAggregator eventAggregator)
        {
            InitializeComponent();
            DataContext = this;
            _appState = appState;
            _notifyIconService = notifyIconService;
            _playerService = playerService;
            _eventAggregator = eventAggregator;

            taskbarService.Initialize(this.MainTaskbarItemInfo, this);

            // 订阅事件以动态更新标题
            _eventAggregator.GetEvent<TrackLoadedEvent>().Subscribe(OnTrackLoaded, ThreadOption.UIThread);
            _eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(OnLanguageChanged, ThreadOption.UIThread);

            UpdateTitle();

            this.Loaded += MainWindow_Loaded;
            this.Closing += MainWindow_Closing;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(ShowOnboardingIfNeeded), DispatcherPriority.ApplicationIdle);
        }

        private void ShowOnboardingIfNeeded()
        {
            if (_onboardingShownThisSession)
            {
                return;
            }

            try
            {
                if (_playerService.GetMusicFolders().Any())
                {
                    return;
                }

                _onboardingShownThisSession = true;
                var onboardingWindow = new OnboardingWindow(_playerService, _eventAggregator) { Owner = this };
                onboardingWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Onboarding Error] {ex.Message}");
            }
        }



        private void OnTrackLoaded(MusicTrack track)
        {
            UpdateTitle();
        }

        private void OnLanguageChanged(System.Globalization.CultureInfo culture)
        {
            UpdateTitle();
        }

        private void UpdateTitle()
        {
            dynamic loc = this.FindResource("Loc");
            string appTitle = loc != null ? loc["AppTitle"] : LocalizationService.Instance["AppTitle"];

            if (_playerService.CurrentTrack != null && !string.IsNullOrEmpty(_playerService.CurrentTrack.Title))
            {
                this.Title = $"{_playerService.CurrentTrack.Title} - {appTitle}";
            }
            else
            {
                this.Title = appTitle;
            }
        }

        protected override void OnStateChanged(System.EventArgs e)
        {
            base.OnStateChanged(e);
            if (this.WindowState == WindowState.Maximized)
            {
                CalculateMaximizedPadding();
            }
        }

        private async void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            if (!_appState.IsExiting)
            {
                if (_appState.ExitBehavior == AppExitBehavior.MinimizeToTray)
                {
                    e.Cancel = true;
                    _notifyIconService.HideMainWindow();
                    return;
                }

                if (_appState.ExitBehavior == AppExitBehavior.Ask)
                {
                    e.Cancel = true;
                    ShowExitBehaviorDialog();
                    return;
                }
            }

            await _appState.SaveCurrentStateAsync();
            _notifyIconService.Dispose();
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void BtnMaximize_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void ShowExitBehaviorDialog()
        {
            if (_closeDialogOpen)
            {
                return;
            }

            _closeDialogOpen = true;
            var dialog = new CloseDialog { Owner = this };
            dialog.Closed += async (_, __) =>
            {
                _closeDialogOpen = false;

                if (dialog.Result == CloseAction.None)
                {
                    return;
                }

                if (dialog.RememberChoice)
                {
                    _appState.ExitBehavior = dialog.Result == CloseAction.MinimizeToTray
                        ? AppExitBehavior.MinimizeToTray
                        : AppExitBehavior.Exit;
                    await _appState.SaveCurrentStateAsync();
                }

                if (dialog.Result == CloseAction.MinimizeToTray)
                {
                    _notifyIconService.HideMainWindow();
                    return;
                }

                _appState.IsExiting = true;
                Close();
            };

            dialog.Show();
        }
    }
}
