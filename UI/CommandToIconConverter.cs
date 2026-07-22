using System;
using System.Globalization;
using System.Windows.Data;
using RevitQuickAccess.Quick;

namespace RevitQuickAccess.UI
{
    /// <summary>Binds a command string to the small ribbon icon Revit uses for that command.</summary>
    public class CommandToIconConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => RibbonIcons.GetSmall(value as string);

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
