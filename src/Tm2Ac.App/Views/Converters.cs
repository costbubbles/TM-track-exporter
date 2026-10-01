using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Tm2Ac.App.Views;

/// <summary>
/// Binds a RadioButton chip to an enum/int?/string property: checked when the value's text equals the parameter ("" = null).
/// Checking it writes the parameter back.
/// </summary>
public sealed class EqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.Equals(value?.ToString() ?? "", parameter?.ToString() ?? "", StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not true)
        {
            return Binding.DoNothing;
        }

        var text = parameter?.ToString() ?? "";
        var type = Nullable.GetUnderlyingType(targetType) ?? targetType;
        if (text.Length == 0)
        {
            return null;
        }

        return type.IsEnum ? Enum.Parse(type, text) : System.Convert.ChangeType(text, type, CultureInfo.InvariantCulture);
    }
}

/// <summary>True when two numbers are (nearly) equal: highlights the scale preset matching the current scale.</summary>
public sealed class NumbersEqualConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is float a && values[1] is float b && MathF.Abs(a - b) < 0.001f;

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        [.. targetTypes.Select(_ => Binding.DoNothing)];
}

/// <summary>Visible when the value is non-null and not an empty string or collection.</summary>
public sealed class HasValueConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var has = value switch
        {
            null => false,
            string s => s.Length > 0,
            System.Collections.ICollection c => c.Count > 0,
            _ => true,
        };
        return (has ^ (parameter as string == "invert")) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Issue severity → colour brush key in the theme.</summary>
public sealed class SeverityBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Application.Current.Resources[value?.ToString() switch
        {
            "Block" => "Error",
            "Warn" => "Warn",
            _ => "TextDim",
        }];

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
