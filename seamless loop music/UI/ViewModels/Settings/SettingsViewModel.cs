using System.Collections.ObjectModel;
using System.Linq;
using Prism.Events;
using Prism.Mvvm;
using Prism.Regions;
using seamless_loop_music.Events;
using seamless_loop_music.Services;

namespace seamless_loop_music.UI.ViewModels.Settings
{
    public class SettingsViewModel : BindableBase, INavigationAware
    {
        private readonly IRegionManager _regionManager;
        private readonly IEventAggregator _eventAggregator;

        private SettingsSectionNavItem _selectedSection;

        public ObservableCollection<SettingsSectionNavItem> Sections { get; private set; }

        public SettingsSectionNavItem SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (SetProperty(ref _selectedSection, value) && value != null)
                {
                    _regionManager.RequestNavigate("SettingsContentRegion", value.ViewName);
                }
            }
        }

        public SettingsViewModel(IRegionManager regionManager, IEventAggregator eventAggregator)
        {
            _regionManager = regionManager;
            _eventAggregator = eventAggregator;

            BuildSections();

            // 语言切换时重建菜单文字，同时尽量保留当前选中的设置页。
            _eventAggregator.GetEvent<LanguageChangedEvent>().Subscribe(_ =>
            {
                var currentView = SelectedSection?.ViewName;
                BuildSections();
                SelectedSection = Sections.FirstOrDefault(s => s.ViewName == currentView) ?? Sections.FirstOrDefault();
            }, ThreadOption.UIThread);
        }

        public void OnNavigatedTo(NavigationContext navigationContext)
        {
            SelectedSection = SelectedSection ?? Sections.FirstOrDefault();
        }

        public bool IsNavigationTarget(NavigationContext navigationContext) => true;
        public void OnNavigatedFrom(NavigationContext navigationContext) { }

        private void BuildSections()
        {
            var loc = LocalizationService.Instance;
            Sections = new ObservableCollection<SettingsSectionNavItem>
            {
                new SettingsSectionNavItem
                {
                    Title = loc["SettingsGeneral"],
                    Description = loc["SettingsGeneralSub"],
                    Icon = "⚙",
                    ViewName = "SettingsGeneralView"
                },
                new SettingsSectionNavItem
                {
                    Title = loc["SettingsMusic"],
                    Description = loc["SettingsMusicSub"],
                    Icon = "🎵",
                    ViewName = "SettingsMusicView"
                },
                new SettingsSectionNavItem
                {
                    Title = loc["SettingsData"],
                    Description = loc["SettingsDataSub"],
                    Icon = "🗄",
                    ViewName = "SettingsDataView"
                }
            };
            RaisePropertyChanged(nameof(Sections));
        }
    }
}
