using CognitiveSupport;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Mutation.Ui.Views.SettingsUi.Pages;

public sealed partial class OcrSettingsPage : UserControl
{
	private const double BytesPerMb = 1024.0 * 1024.0;

	private readonly Settings _settings;
	private bool _suppressEvents;

	public OcrSettingsPage(Settings settings)
	{
		_settings = settings;
		InitializeComponent();
		LoadValues();

		TxtApiKey.RegisterPropertyChangedCallback(Controls.SecretBox.SecretProperty, (_, _) =>
		{
			if (_suppressEvents) return;
			(_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).ApiKey = TxtApiKey.Secret;
		});

		HkSendAfterOcr.HotkeyCommitted += (_, value) =>
		{
			(_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).SendHotkeyAfterOcrOperation =
				string.IsNullOrWhiteSpace(value) ? null : value;
		};
	}

	private void LoadValues()
	{
		_suppressEvents = true;
		try
		{
			var ocr = _settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings();
			TxtApiKey.Secret = ocr.ApiKey ?? string.Empty;
			TxtEndpoint.Text = ocr.Endpoint ?? string.Empty;
			NbTimeoutSeconds.Value = ocr.TimeoutSeconds > 0 ? ocr.TimeoutSeconds : SettingsDefaults.Ocr.TimeoutSeconds;
			NbFreeTierPageLimit.Value = ocr.FreeTierPageLimit > 0 ? ocr.FreeTierPageLimit : SettingsDefaults.Ocr.FreeTierPageLimit;
			NbMaxParallelDocuments.Value = ocr.MaxParallelDocuments > 0 ? ocr.MaxParallelDocuments : SettingsDefaults.Ocr.MaxParallelDocuments;
			NbMaxParallelRequests.Value = ocr.MaxParallelRequests > 0 ? ocr.MaxParallelRequests : SettingsDefaults.Ocr.MaxParallelRequests;
			NbMaxDocumentSizeMb.Value = ocr.MaxDocumentBytes is > 0
				? System.Math.Round(ocr.MaxDocumentBytes.Value / BytesPerMb, 1)
				: 0;
			ToggleUseFreeTier.IsOn = ocr.UseFreeTier;
			ToggleInvert.IsOn = ocr.InvertScreenshot;
			TogglePasteOcrText.IsOn = ocr.PasteOcrTextIntoActiveApplication;
			ToggleNudgePointer.IsOn = ocr.NudgePointerDuringCapture;
			NbNudgeInterval.Value = ocr.PointerNudgeIntervalMilliseconds > 0
				? ocr.PointerNudgeIntervalMilliseconds
				: SettingsDefaults.Ocr.PointerNudgeIntervalMilliseconds;
			NbNudgeDuration.Value = ocr.PointerNudgeDurationMilliseconds > 0
				? ocr.PointerNudgeDurationMilliseconds
				: SettingsDefaults.Ocr.PointerNudgeDurationMilliseconds;
			HkSendAfterOcr.Hotkey = ocr.SendHotkeyAfterOcrOperation ?? string.Empty;
		}
		finally { _suppressEvents = false; }
	}

	private void TxtEndpoint_TextChanged(object sender, TextChangedEventArgs e)
	{
		if (_suppressEvents) return;
		(_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).Endpoint = TxtEndpoint.Text;
	}

	private void NbTimeoutSeconds_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
		WriteInt(args.NewValue, v => (_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).TimeoutSeconds = v);

	private void NbFreeTierPageLimit_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
		WriteInt(args.NewValue, v => (_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).FreeTierPageLimit = v);

	private void NbMaxParallelDocuments_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
		WriteInt(args.NewValue, v => (_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).MaxParallelDocuments = v);

	private void NbMaxParallelRequests_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
		WriteInt(args.NewValue, v => (_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).MaxParallelRequests = v);

	private void NbMaxDocumentSizeMb_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(args.NewValue)) return;
		// 0 (or less) stores 0 = "no limit"; otherwise convert MB -> bytes.
		long bytes = args.NewValue <= 0 ? 0 : (long)System.Math.Round(args.NewValue * BytesPerMb);
		(_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).MaxDocumentBytes = bytes;
	}

	private void ToggleUseFreeTier_Toggled(object sender, RoutedEventArgs e)
	{
		if (_suppressEvents) return;
		(_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).UseFreeTier = ToggleUseFreeTier.IsOn;
	}

	private void ToggleInvert_Toggled(object sender, RoutedEventArgs e)
	{
		if (_suppressEvents) return;
		(_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).InvertScreenshot = ToggleInvert.IsOn;
	}

	private void TogglePasteOcrText_Toggled(object sender, RoutedEventArgs e)
	{
		if (_suppressEvents) return;
		(_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).PasteOcrTextIntoActiveApplication = TogglePasteOcrText.IsOn;
	}

	private void ToggleNudgePointer_Toggled(object sender, RoutedEventArgs e)
	{
		if (_suppressEvents) return;
		(_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).NudgePointerDuringCapture = ToggleNudgePointer.IsOn;
	}

	private void NbNudgeInterval_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
		WriteInt(args.NewValue, v => (_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).PointerNudgeIntervalMilliseconds = v);

	private void NbNudgeDuration_ValueChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) =>
		WriteInt(args.NewValue, v => (_settings.AzureComputerVisionSettings ??= new AzureComputerVisionSettings()).PointerNudgeDurationMilliseconds = v);

	private void WriteInt(double value, System.Action<int> apply)
	{
		if (_suppressEvents) return;
		if (double.IsNaN(value)) return;
		apply((int)value);
	}

	private void BtnResetTimeout_Click(object sender, RoutedEventArgs e) =>
		NbTimeoutSeconds.Value = SettingsDefaults.Ocr.TimeoutSeconds;
	private void BtnResetFreeTier_Click(object sender, RoutedEventArgs e) =>
		NbFreeTierPageLimit.Value = SettingsDefaults.Ocr.FreeTierPageLimit;
	private void BtnResetMaxDocs_Click(object sender, RoutedEventArgs e) =>
		NbMaxParallelDocuments.Value = SettingsDefaults.Ocr.MaxParallelDocuments;
	private void BtnResetMaxReqs_Click(object sender, RoutedEventArgs e) =>
		NbMaxParallelRequests.Value = SettingsDefaults.Ocr.MaxParallelRequests;
	private void BtnResetMaxDocSize_Click(object sender, RoutedEventArgs e) =>
		NbMaxDocumentSizeMb.Value = SettingsDefaults.Ocr.MaxDocumentBytes / BytesPerMb;
	private void BtnResetNudgeInterval_Click(object sender, RoutedEventArgs e) =>
		NbNudgeInterval.Value = SettingsDefaults.Ocr.PointerNudgeIntervalMilliseconds;
	private void BtnResetNudgeDuration_Click(object sender, RoutedEventArgs e) =>
		NbNudgeDuration.Value = SettingsDefaults.Ocr.PointerNudgeDurationMilliseconds;
}
