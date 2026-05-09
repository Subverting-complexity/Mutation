using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class GeneralSettingsPage : UserControl
{
	private readonly Settings _settings;
	private bool _suppressEvents;

	public GeneralSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		LoadValues();
	}

	private void LoadValues()
	{
		_suppressEvents = true;
		try
		{
			TxtUserInstructions.Text = _settings.UserInstructions ?? string.Empty;
		}
		finally { _suppressEvents = false; }
	}

	private void TxtUserInstructions_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_suppressEvents) return;
		_settings.UserInstructions = TxtUserInstructions.Text;
	}

	private void BtnResetUserInstructions_Click(object sender, RoutedEventArgs e)
	{
		TxtUserInstructions.Text = SettingsDefaults.UserInstructions;
	}
}
