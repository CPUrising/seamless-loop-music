using System;
using System.Globalization;
using System.Windows.Data;

namespace seamless_loop_music.UI.Converters
{
    public class ProgressMarkerPositionConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values == null || values.Length < 2)
                return 0d;

            if (!(values[0] is double width) || width <= 0)
                return 0d;

            if (!(values[1] is double fraction))
                return 0d;

            if (double.IsNaN(fraction) || double.IsInfinity(fraction))
                return 0d;

            fraction = Math.Max(0d, Math.Min(1d, fraction));
            var markerWidth = 2d;
            return Math.Max(0d, (width - markerWidth) * fraction);
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
