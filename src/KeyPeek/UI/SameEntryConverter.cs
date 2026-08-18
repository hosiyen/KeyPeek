using System.Globalization;
using System.Windows.Data;

namespace KeyPeek.UI;

/// <summary>
/// True when a row's entry IS the window's selected entry. Reference equality on purpose:
/// EntryVm is a record, and two distinct rows can be value-equal (the same shortcut listed
/// in an app card and in the system rail), which would light up both.
/// </summary>
public sealed class SameEntryConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is not null && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
