namespace MessengerMaui;

public class FirstLetterConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string str && !string.IsNullOrEmpty(str))
        {
            return str[0].ToString().ToUpper();
        }
        return "?";
    }

    public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}