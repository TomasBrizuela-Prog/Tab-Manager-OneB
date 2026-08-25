using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OneBlack.Contenedor
{
    /// <summary>Convierte bool → Visibility (true=Visible, false=Collapsed).</summary>
    public class BoolAVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type t, object p, CultureInfo c)
            => (value is bool b && b) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object value, Type t, object p, CultureInfo c)
            => value is Visibility v && v == Visibility.Visible;
    }
}