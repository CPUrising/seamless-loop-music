using System.Globalization;
using System.Linq;
using System.Collections.Generic;
using Prism.Events;
using Prism.Mvvm;
using seamless_loop_music.Events;
using seamless_loop_music.Services;

namespace seamless_loop_music.UI.ViewModels.Settings
{
    public class SettingsGeneralViewModel : BindableBase
    {
        private readonly IAppStateService _appState;
        private readonly IThemeService _themeService;
        private bool _isInitializing = true;
        private CultureInfo _selectedCulture;
        private ExitBehaviorOption _selectedExitBehavior;
        private bool _isDarkTheme;

        public CultureInfo[] SupportedCultures => LocalizationService.Instance.SupportedCultures;
        public IReadOnlyList<ExitBehaviorOption> ExitBehaviorOptions { get; private set; }

        public CultureInfo SelectedCulture
        {
            get => _selectedCulture;
            set
            {
                if (SetProperty(ref _selectedCulture, value) && value != null && !_isInitializing)
                {
                    LocalizationService.Instance.CurrentCulture = value;
                    _ = _appState.SaveCurrentStateAsync();
                }
            }
        }

        public ExitBehaviorOption SelectedExitBehavior
        {
            get => _selectedExitBehavior;
            set
            {
                if (SetProperty(ref _selectedExitBehavior, value) && value != null && !_isInitializing)
                {
                    _appState.ExitBehavior = value.Behavior;
                    _ = _appState.SaveCurrentStateAsync();
                }
            }
        }

        public bool IsDarkTheme
        {
            get => _isDarkTheme;
            set
            {
                if (SetProperty(ref _isDarkTheme, value) && !_isInitializing)
                {
                    _themeService.ApplyTheme(value);
                    _appState.IsDarkTheme = value;
                    _ = _appState.SaveThemePreferenceAsync();
                }
            }
        }

        public SettingsGeneralViewModel(IAppStateService appState, IThemeService themeService, IEventAggregator eventAggregator)
        {
            _appState = appState;
            _themeService = themeService;

            var current = LocalizationService.Instance.CurrentCulture;
            _selectedCulture = SupportedCultures.FirstOrDefault(c => c.Name == current.Name) ?? SupportedCultures[0];
            _isDarkTheme = _appState.IsDarkTheme;
            BuildExitBehaviorOptions();
            _isInitializing = false;

            eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(_ =>
            {
                var currentBehavior = SelectedExitBehavior?.Behavior ?? _appState.ExitBehavior;
                BuildExitBehaviorOptions(currentBehavior);
                RaisePropertyChanged(nameof(SupportedCultures));
                RaisePropertyChanged(nameof(ExitBehaviorOptions));
            }, ThreadOption.UIThread);
        }

        private void BuildExitBehaviorOptions(AppExitBehavior? behaviorToSelect = null)
        {
            var loc = LocalizationService.Instance;
            ExitBehaviorOptions = new[]
            {
                new ExitBehaviorOption(AppExitBehavior.Ask, loc["ExitBehaviorAsk"]),
                new ExitBehaviorOption(AppExitBehavior.MinimizeToTray, loc["ExitBehaviorMinimizeToTray"]),
                new ExitBehaviorOption(AppExitBehavior.Exit, loc["ExitBehaviorExit"])
            };
            var target = behaviorToSelect ?? _appState.ExitBehavior;
            _selectedExitBehavior = ExitBehaviorOptions.FirstOrDefault(o => o.Behavior == target) ?? ExitBehaviorOptions[0];
            RaisePropertyChanged(nameof(SelectedExitBehavior));
        }
    }

    public class ExitBehaviorOption
    {
        public AppExitBehavior Behavior { get; }
        public string DisplayName { get; }

        public ExitBehaviorOption(AppExitBehavior behavior, string displayName)
        {
            Behavior = behavior;
            DisplayName = displayName;
        }
    }
}
