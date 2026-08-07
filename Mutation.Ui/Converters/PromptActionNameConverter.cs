using Microsoft.UI.Xaml.Data;
using Mutation.Ui.Services;
using System;

namespace Mutation.Ui.Converters;

/// <summary>
/// Turns a prompt's name into the accessible name for one of its action buttons. The verb
/// comes from the binding's <c>ConverterParameter</c>, so one converter serves Run, Edit and
/// Delete: <c>{Binding Name, Converter={StaticResource PromptActionNameConverter},
/// ConverterParameter='Delete'}</c> reads out as "Delete prompt 'Summarise'".
/// </summary>
public class PromptActionNameConverter : IValueConverter
{
	public object Convert(object value, Type targetType, object parameter, string language)
		=> PromptActionNames.Build(parameter as string, value as string);

	public object ConvertBack(object value, Type targetType, object parameter, string language)
		=> throw new NotSupportedException("Accessible names are display-only.");
}
