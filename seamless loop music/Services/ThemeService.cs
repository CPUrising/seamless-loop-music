using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace seamless_loop_music.Services
{
    public interface IThemeService
    {
        bool IsDarkTheme { get; }
        void ApplyTheme(bool isDarkTheme);
    }

    public class ThemeService : IThemeService
    {
        private bool _hasAppliedTheme;

        public bool IsDarkTheme { get; private set; } = true;

        public void ApplyTheme(bool isDarkTheme)
        {
            var application = Application.Current;
            if (application == null)
            {
                IsDarkTheme = isDarkTheme;
                return;
            }

            if (!application.Dispatcher.CheckAccess())
            {
                application.Dispatcher.Invoke(() => ApplyTheme(isDarkTheme));
                return;
            }

            if (_hasAppliedTheme && IsDarkTheme == isDarkTheme && HasPalette(application, isDarkTheme))
            {
                return;
            }

            var colorsDictionary = application.Resources.MergedDictionaries
                .FirstOrDefault(dictionary => dictionary.Source != null &&
                    dictionary.Source.OriginalString.EndsWith("UI/Themes/Colors.xaml", StringComparison.OrdinalIgnoreCase));

            if (colorsDictionary == null)
            {
                throw new InvalidOperationException("Could not find the UI/Themes/Colors.xaml resource dictionary in Application.Current.Resources.");
            }

            var paletteDictionary = colorsDictionary.MergedDictionaries.FirstOrDefault(dictionary =>
                dictionary.Source != null &&
                (dictionary.Source.OriginalString.EndsWith("Palettes/Dark.xaml", StringComparison.OrdinalIgnoreCase) ||
                 dictionary.Source.OriginalString.EndsWith("Palettes/Light.xaml", StringComparison.OrdinalIgnoreCase)));

            if (paletteDictionary == null)
            {
                throw new InvalidOperationException("Could not find the Dark.xaml or Light.xaml palette merged by UI/Themes/Colors.xaml.");
            }

            var palettePath = isDarkTheme ? "Dark.xaml" : "Light.xaml";
            paletteDictionary.Source = new Uri(
                $"pack://application:,,,/{typeof(ThemeService).Assembly.GetName().Name};component/UI/Themes/Palettes/{palettePath}",
                UriKind.Absolute);
            UpdateSharedBrushes(colorsDictionary, paletteDictionary);

            var paletteHelper = new PaletteHelper();
            var theme = paletteHelper.GetTheme();
            theme.SetBaseTheme(isDarkTheme ? BaseTheme.Dark : BaseTheme.Light);
            theme.SetPrimaryColor(isDarkTheme ? Color.FromRgb(0xD1, 0xBC, 0xFF) : Color.FromRgb(0x62, 0x46, 0xA8));
            theme.SetSecondaryColor(isDarkTheme ? Color.FromRgb(0x68, 0xD8, 0xC8) : Color.FromRgb(0x00, 0x7F, 0x73));
            paletteHelper.SetTheme(theme);

            IsDarkTheme = isDarkTheme;
            _hasAppliedTheme = true;
        }

        private static void UpdateSharedBrushes(ResourceDictionary colorsDictionary, ResourceDictionary paletteDictionary)
        {
            foreach (var key in colorsDictionary.Keys.OfType<string>().Where(key => key.StartsWith("Brush", StringComparison.Ordinal)))
            {
                if (!(colorsDictionary[key] is SolidColorBrush))
                {
                    throw new InvalidOperationException($"Theme resource '{key}' is not a SolidColorBrush.");
                }

                var colorKey = "Color" + key.Substring("Brush".Length);
                if (!paletteDictionary.Contains(colorKey))
                {
                    continue;
                }

                if (!(paletteDictionary[colorKey] is Color color))
                {
                    throw new InvalidOperationException($"Palette resource '{colorKey}' is not a System.Windows.Media.Color.");
                }

                colorsDictionary[key] = new SolidColorBrush(color);
            }
        }

        private static bool HasPalette(Application application, bool isDarkTheme)
        {
            var expectedPalette = isDarkTheme ? "Palettes/Dark.xaml" : "Palettes/Light.xaml";
            return application.Resources.MergedDictionaries
                .Where(dictionary => dictionary.Source != null &&
                    dictionary.Source.OriginalString.EndsWith("UI/Themes/Colors.xaml", StringComparison.OrdinalIgnoreCase))
                .SelectMany(dictionary => dictionary.MergedDictionaries)
                .Any(dictionary => dictionary.Source != null &&
                    dictionary.Source.OriginalString.EndsWith(expectedPalette, StringComparison.OrdinalIgnoreCase));
        }
    }
}
