using System;
using System.ComponentModel;
using System.Globalization;
using System.Resources;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Data;

namespace seamless_loop_music.Services
{
    public class LocalizationService : INotifyPropertyChanged
    {
        private static readonly LocalizationService _instance = new LocalizationService();
        public static LocalizationService Instance => _instance;

        private readonly ResourceManager _resourceManager = Properties.Resources.ResourceManager;
        private CultureInfo _currentCulture = CultureInfo.CurrentUICulture;

        public string this[string key]
        {
            get
            {
                if (string.IsNullOrEmpty(key)) return "";
                return _resourceManager.GetString(key, _currentCulture) ?? $"[{key}]";
            }
        }

        public CultureInfo CurrentCulture
        {
            get => _currentCulture;
            set
            {
                if (!Equals(_currentCulture, value))
                {
                    _currentCulture = value;
                    Properties.Resources.Culture = value;
                    OnPropertyChanged(null); // Notify all properties changed
                    OnPropertyChanged("Item[]"); // Specifically notify indexer for WPF bindings
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class LocExtension : Binding
    {
        public LocExtension(string key) : base("[" + key + "]")
        {
            Source = LocalizationService.Instance;
            Mode = BindingMode.OneWay;
        }
    }
}
