using CognitiveSupport;
using CoreAudio;
using Deepgram;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Mutation.Ui.Services;
using Mutation.Ui.Views.SettingsUi;
using OpenAI;
using OpenAI.Audio;
using System.ClientModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace Mutation.Ui;

public partial class App : Application
{
        private Window? _window;
        internal Window? MainAppWindow => _window;
	private IHost? _host;
	private const string OpenAiHttpClientName = "openai-http-client";
	private const string AnthropicHttpClientName = "anthropic-http-client";
	private bool _isShuttingDown = false;

        public App()
        {
		// Global last-resort handlers. These log and keep the process alive wherever
		// possible: a single faulting async-void handler or an orphaned background
		// Task must not be allowed to terminate the app. Escalating these to
		// Environment.FailFast previously turned recoverable, expected background
		// exceptions (e.g. cold-start network timeouts/retries on the first
		// transcription after a reboot or update) into hard crashes.
		Application.Current.UnhandledException += OnUnhandledException;
		AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
		TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

	private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
	{
		// Keep the app alive; a faulting UI handler/async-void should not kill the
		// process. The error is logged and surfaced through normal in-app paths.
		e.Handled = true;
		ErrorLogger.LogError("Unhandled UI Exception", e.Exception);
	}

	private void OnAppDomainUnhandledException(object sender, System.UnhandledExceptionEventArgs e)
	{
		// By the time this fires the runtime is already tearing the process down and
		// there is nothing to prevent. Record what happened for diagnostics only.
		ErrorLogger.LogError("Unhandled AppDomain Exception", e.ExceptionObject as Exception);
	}

	private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
	{
		// A background Task faulted with no awaiter — common when cold-start network
		// timeouts trigger retries that abandon their in-flight requests. This is
		// non-fatal: observe it so the runtime does not escalate, then log it.
		e.SetObserved();
		ErrorLogger.LogError("Unobserved Task Exception", e.Exception);
	}

	private static Settings? TryRecoverSettings(string filePath, SettingsManager manager, Exception originalException)
	{
		string errorMessage = originalException.Message;
		const string title = "Mutation: settings file could not be loaded";

		for (int attempt = 0; attempt < 3; attempt++)
		{
			string body =
				$"Mutation could not load its settings file:\n{filePath}\n\n" +
				$"Error: {errorMessage}\n\n" +
				"Choose an action:\n" +
				"  • Yes — open the file in your default editor (fix it, then click OK to retry)\n" +
				"  • No — restore from the .bak backup (if present)\n" +
				"  • Cancel — quit Mutation";

			var choice = System.Windows.Forms.MessageBox.Show(
				body, title,
				System.Windows.Forms.MessageBoxButtons.YesNoCancel,
				System.Windows.Forms.MessageBoxIcon.Error);

			if (choice == System.Windows.Forms.DialogResult.Cancel)
				return null;

			if (choice == System.Windows.Forms.DialogResult.Yes)
			{
				try
				{
					System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
				}
				catch (Exception openEx)
				{
					System.Windows.Forms.MessageBox.Show(
						$"Could not open the file in the default editor:\n{openEx.Message}",
						title,
						System.Windows.Forms.MessageBoxButtons.OK,
						System.Windows.Forms.MessageBoxIcon.Warning);
				}
				System.Windows.Forms.MessageBox.Show(
					"Click OK after you've saved your changes to retry loading the settings.",
					title,
					System.Windows.Forms.MessageBoxButtons.OK,
					System.Windows.Forms.MessageBoxIcon.Information);
			}
			else // No → restore .bak
			{
				string backup = filePath + ".bak";
				if (!File.Exists(backup))
				{
					System.Windows.Forms.MessageBox.Show(
						$"No backup was found at:\n{backup}",
						title,
						System.Windows.Forms.MessageBoxButtons.OK,
						System.Windows.Forms.MessageBoxIcon.Warning);
					continue;
				}
				try
				{
					File.Copy(backup, filePath, overwrite: true);
				}
				catch (Exception copyEx)
				{
					System.Windows.Forms.MessageBox.Show(
						$"Could not restore the backup:\n{copyEx.Message}",
						title,
						System.Windows.Forms.MessageBoxButtons.OK,
						System.Windows.Forms.MessageBoxIcon.Error);
					continue;
				}
			}

			try
			{
				return manager.LoadAndEnsureSettings();
			}
			catch (Exception retryEx) when (retryEx is Newtonsoft.Json.JsonException or InvalidOperationException or IOException)
			{
				errorMessage = retryEx.Message;
			}
		}
		return null;
	}

	// First-run setup is needed while no LLM provider key is configured. This
	// covers a brand-new Mutation.json (keys are "<placeholder>") and any later
	// launch where the user dismissed onboarding without adding a key. Optional
	// keys (Anthropic alone, Azure OCR) are not required to clear this.
	private static bool NeedsFirstRunSetup(Settings settings)
	{
		var keys = settings.ApiKeys;
		return !IsKeyConfigured(keys?.OpenAiApiKey) && !IsKeyConfigured(keys?.AnthropicApiKey);
	}

	private static bool IsKeyConfigured(string? value)
		=> !string.IsNullOrWhiteSpace(value) && value != SettingsDefaults.PlaceholderValue;

        protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
                try
		{
			ErrorLogger.LogInfo("Startup", "OnLaunched starting");
			HostApplicationBuilder builder = Host.CreateApplicationBuilder();

			string exeDir = AppDomain.CurrentDomain.BaseDirectory;
			string mutationDir = exeDir;
			string filePath = Path.Combine(mutationDir, "Mutation.json");
			var settingsManager = new SettingsManager(filePath);
			Settings settings;
			try
			{
				settings = settingsManager.LoadAndEnsureSettings();
			}
			catch (Exception loadEx) when (loadEx is Newtonsoft.Json.JsonException or InvalidOperationException or IOException)
			{
				var recovered = TryRecoverSettings(filePath, settingsManager, loadEx);
				if (recovered is null)
					throw;
				settings = recovered;
			}
			BeepPlayer.Initialize(settings);

			builder.Services.AddSingleton<ISettingsManager>(settingsManager);
			builder.Services.AddSingleton(settings);
			builder.Services.AddSingleton<ClipboardManager>();
			builder.Services.AddSingleton<UiStateManager>();
			builder.Services.AddSingleton<MMDeviceEnumerator>(_ => new MMDeviceEnumerator(Guid.NewGuid()));
			builder.Services.AddSingleton<Mutation.Ui.Core.ICaptureDeviceChangeNotifier, Mutation.Ui.Core.MMDeviceCaptureDeviceChangeNotifier>();
			builder.Services.AddSingleton<AudioDeviceManager>();
			// AudioDeviceManager resolves the active mic's level endpoint and can
			// re-acquire fresh device references, so it is the pin service's provider.
			builder.Services.AddSingleton<Mutation.Ui.Core.ICaptureLevelEndpointProvider>(sp =>
				sp.GetRequiredService<AudioDeviceManager>());
			builder.Services.AddSingleton<Mutation.Ui.Core.MicrophoneLevelPinService>();
				// One shared instance serializes every capture-level write (slider, pin
				// toggle, record-start re-assert) onto a single background worker, so the
				// COM writes never run on the UI thread and never overlap each other.
				builder.Services.AddSingleton<Mutation.Ui.Core.MicrophoneLevelWriteCoordinator>(sp =>
					new Mutation.Ui.Core.MicrophoneLevelWriteCoordinator(
						sp.GetRequiredService<Mutation.Ui.Core.MicrophoneLevelPinService>().ApplyLevel));
			builder.Services.AddSingleton<IOcrService>(sp =>
	 new OcrService(
		  settings.AzureComputerVisionSettings?.ApiKey,
		  settings.AzureComputerVisionSettings?.Endpoint,
		  settings.AzureComputerVisionSettings?.TimeoutSeconds ?? 10));
			builder.Services.AddSingleton<OcrManager>(sp =>
					  new OcrManager(settings,
									  sp.GetRequiredService<IOcrService>(),
									  sp.GetRequiredService<ClipboardManager>()));
			builder.Services.AddSingleton<HotkeyManager>(sp =>
					  new HotkeyManager(sp.GetRequiredService<MainWindow>(), sp.GetRequiredService<Settings>()));
			builder.Services.AddSingleton<ILlmService>(sp =>
			{
				var llmSettings = settings.LlmSettings;
				string openAiKey = settings.ApiKeys?.OpenAiApiKey ?? string.Empty;
				string anthropicKey = settings.ApiKeys?.AnthropicApiKey ?? string.Empty;
				int timeoutSeconds = llmSettings?.TimeoutSeconds > 0 ? llmSettings.TimeoutSeconds : 60;
				int retryCount = llmSettings?.RetryCount ?? SettingsDefaults.Llm.RetryCount;
				if (retryCount < 0) retryCount = SettingsDefaults.Llm.RetryCount;
				var allModels = llmSettings?.Models ?? new List<LlmModelConfig>();

				var openAiModels = allModels.Where(m => m.Provider == LlmProvider.OpenAI).ToList();
				var anthropicModels = allModels.Where(m => m.Provider == LlmProvider.Anthropic).ToList();

				LlmService? openAiService = null;
				if (openAiModels.Any() && !string.IsNullOrEmpty(openAiKey) && openAiKey != SettingsDefaults.PlaceholderValue)
				{
					openAiService = new LlmService(openAiKey, openAiModels, timeoutSeconds, retryCount);
				}

				AnthropicLlmService? anthropicService = null;
				if (anthropicModels.Any() && !string.IsNullOrEmpty(anthropicKey) && anthropicKey != SettingsDefaults.PlaceholderValue)
				{
					var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
					var anthropicHttpClient = httpClientFactory.CreateClient(AnthropicHttpClientName);
					anthropicService = new AnthropicLlmService(anthropicKey, anthropicModels, anthropicHttpClient, timeoutSeconds, retryCount);
				}

				var modelProviders = allModels
					.GroupBy(m => m.Name, StringComparer.OrdinalIgnoreCase)
					.ToDictionary(g => g.Key, g => g.First().Provider, StringComparer.OrdinalIgnoreCase);

				return new CompositeLlmService(openAiService, anthropicService, modelProviders);
			});
			builder.Services.AddSingleton<TranscriptFormatter>();
			builder.Services.AddSingleton<SpeechToTextManager>();
			builder.Services.AddSingleton<Mutation.Ui.Core.AudioSessionManager>();
                        builder.Services.AddSingleton<ITextToSpeechService, TextToSpeechService>();
                        builder.Services.AddSingleton<IWavFileSpeechExporter, WavFileSpeechExporter>();
			builder.Services.AddHttpClient(OpenAiHttpClientName);
			builder.Services.AddHttpClient(AnthropicHttpClientName);
			AddSpeechToTextServices(builder, settings);
			builder.Services.AddSingleton<MainWindow>();

			_host = builder.Build();

			_window = _host.Services.GetRequiredService<MainWindow>();
			var ui = _host.Services.GetRequiredService<UiStateManager>();
			ui.Restore(_window);

			_window.Activate();

                        var preflight = ScreenCapturePreflight.TryCaptureProbe();
			if (!preflight.ok)
			{
				string title = "Screen Capture Disabled";
				string message = preflight.message ?? "Screen capture may be disabled by system policy.";
				if (_window.Content is FrameworkElement fe0 && fe0.XamlRoot is not null)
				{
					var dialog = new ContentDialog
					{
						Title = title,
						Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
						CloseButtonText = "OK",
						XamlRoot = fe0.XamlRoot
					};
					Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dialog, title);
					Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(dialog, message);
					await dialog.ShowAsync();
				}
				else
				{
					System.Windows.Forms.MessageBox.Show(message, title,
						System.Windows.Forms.MessageBoxButtons.OK,
						System.Windows.Forms.MessageBoxIcon.Warning);
				}
			}

			if (BeepPlayer.LastInitializationIssues.Count > 0)
			{
				const string title = "Custom Beep Settings Issues";
				string message = "The following issues were found with the custom beep settings:\n\n" +
										  string.Join("\n", BeepPlayer.LastInitializationIssues);

				if (_window.Content is FrameworkElement fe && fe.XamlRoot is not null)
				{
					var dialog = new ContentDialog
					{
						Title = title,
						Content = new TextBlock { Text = message, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
						CloseButtonText = "OK",
						XamlRoot = fe.XamlRoot
					};
					// Provide accessible name/help text for screen readers
					Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(dialog, title);
					Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(dialog, message);
					await dialog.ShowAsync();
				}
				else
				{
					System.Windows.Forms.MessageBox.Show(
							  message,
							  title,
							  System.Windows.Forms.MessageBoxButtons.OK,
							  System.Windows.Forms.MessageBoxIcon.Warning);
				}
			}

			var ocrMgr = _host.Services.GetRequiredService<OcrManager>();
			ocrMgr.InitializeWindow(_window);

                        var hkManager = _host.Services.GetRequiredService<HotkeyManager>();
                        // Register core, prompt, and router hotkeys, then surface any that could not be
                        // bound the same way (dialog + failure beep). AttachHotkeyManager registers the
                        // prompt and router hotkeys and RegisterCoreHotkeys registers the core ones; both
                        // return the failures they encountered.
                        var hotkeyFailures = new List<HotkeyManager.HotkeyBindingFailure>();
                        if (_window is MainWindow main)
                        {
                                hotkeyFailures.AddRange(main.AttachHotkeyManager(hkManager));
                                hotkeyFailures.AddRange(main.RegisterCoreHotkeys(hkManager));
                        }
                        var settingsSvc = _host.Services.GetRequiredService<Settings>();

			if (hotkeyFailures.Count > 0 && _window is MainWindow mainWindow)
				await mainWindow.ShowHotkeyBindingFailuresAsync(hotkeyFailures);

			_window.Closed += async (_, __) =>
			{
				// Ensure global hooks are released promptly
				try { hkManager.Dispose(); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"HotkeyManager dispose failed: {ex.Message}"); }
				// Stop background host services and exit the app
				await ShutdownAsync();
			};

			// First-run / unconfigured onboarding. Replaces opening Mutation.json
			// in Notepad: show a friendly welcome, then open the in-app Settings
			// dialog so the user can add their API keys. Runs last so the window is
			// fully live first — hotkeys are registered and the Closed/shutdown
			// handler is wired before the user is parked in modal dialogs.
			if (_window is MainWindow onboardingWindow)
			{
				if (NeedsFirstRunSetup(settings))
				{
					// No LLM provider key at all: full first-run onboarding already opens
					// Settings (on the API keys tab path), so don't also raise the per-service
					// speech warning here — it would double up the dialogs.
					await onboardingWindow.ShowFirstRunOnboardingAsync();
				}
				else
				{
					// LLM is configured, but a speech-to-text service (e.g. Deepgram) may still
					// be missing its key. Warn and open the API keys tab instead of crashing.
					var missingSpeechKeys = GetSpeechServicesMissingApiKey(settings);
					if (missingSpeechKeys.Count > 0)
						await onboardingWindow.ShowMissingSpeechServiceKeysWarningAsync(missingSpeechKeys);
				}
			}
		}
		catch (Exception ex)
		{
			ErrorLogger.LogError("Startup Error", ex);
			string logPath = ErrorLogger.PrimaryLogPath;

			string userMessage =
				$"An error occurred during startup: {ex.GetType().Name}.\n\nDetails were written to:\n{logPath}";

			bool dialogShown = false;
			try
			{
				var errorDialog = new ContentDialog
				{
					Title = "Startup Error",
					Content = new TextBlock { Text = userMessage, TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
					CloseButtonText = "OK"
				};
				Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(errorDialog, "Startup Error");
				Microsoft.UI.Xaml.Automation.AutomationProperties.SetHelpText(errorDialog, userMessage);
				if (_window is not null && _window.Content is FrameworkElement fe && fe.XamlRoot is not null)
					errorDialog.XamlRoot = fe.XamlRoot;
				else if (Microsoft.UI.Xaml.Window.Current?.Content is FrameworkElement fe2 && fe2.XamlRoot is not null)
					errorDialog.XamlRoot = fe2.XamlRoot;

				if (errorDialog.XamlRoot != null)
				{
					await errorDialog.ShowAsync();
					dialogShown = true;
				}
			}
			catch
			{
				// Ignore dialog errors, fallback below
			}
			if (!dialogShown)
			{
				System.Windows.Forms.MessageBox.Show(
					userMessage,
					"Startup Error",
					System.Windows.Forms.MessageBoxButtons.OK,
					System.Windows.Forms.MessageBoxIcon.Error
				);
			}

			// A failed startup leaves the app half-initialized with no
			// Window.Closed handler attached, so the process would otherwise
			// linger after the user dismisses the Startup Error dialog.
			await ShutdownAsync();
		}
	}

	private async System.Threading.Tasks.Task ShutdownAsync()
	{
		if (_isShuttingDown)
			return;
		_isShuttingDown = true;
		try
		{
			if (_host is not null)
			{
				try
				{
					await _host.StopAsync(TimeSpan.FromSeconds(2));
				}
				catch { }
				try
				{
					_host.Dispose();
				}
				catch { }
				_host = null;
			}
		}
		finally
		{
			// End the WinUI message loop, then guarantee process termination.
			// Application.Exit() alone is not enough: NAudio callbacks, COM
			// device-enumerator listeners, SAPI synthesizer threads, and the
			// HotkeyManager's WndProc subclass can keep the process alive.
			try { Exit(); } catch { }
			Environment.Exit(0);
		}
	}

	private static void AddSpeechToTextServices(HostApplicationBuilder builder, Settings settings)
	{
		builder.Services.AddSingleton<ISpeechToTextService[]>(sp =>
		{
			List<ISpeechToTextService> services = new();
			var sttSettings = settings.SpeechToTextSettings?.Services ?? Array.Empty<SpeechToTextServiceSettings>();
			foreach (var serviceSettings in sttSettings)
			{
				switch (serviceSettings.Provider)
				{
					case SpeechToTextProviders.OpenAi:
					{
						// A missing key would make the OpenAI client constructor throw and crash
						// startup. Skip the service instead; the user is warned and steered to the
						// API keys tab (see GetSpeechServicesMissingApiKey / OnLaunched).
						string apiKey = ResolveServiceApiKey(settings.ApiKeys?.OpenAiApiKey, serviceSettings.ApiKey);
						if (string.IsNullOrEmpty(apiKey))
							break;
						services.Add(CreateWhisperSpeechToTextService(builder, serviceSettings, apiKey, sp));
						break;
					}
					case SpeechToTextProviders.Deepgram:
					{
						// Same rationale as OpenAI above: ClientFactory.CreateListenRESTClient throws
						// on an empty key, which previously surfaced as a fatal startup error dialog.
						string apiKey = ResolveServiceApiKey(settings.ApiKeys?.DeepgramApiKey, serviceSettings.ApiKey);
						if (string.IsNullOrEmpty(apiKey))
							break;
						services.Add(CreateDeepgramSpeechToTextService(builder, serviceSettings, apiKey));
						break;
					}
					default:
						throw new NotSupportedException($"The SpeechToText service '{serviceSettings.Provider}' is not supported.");
				}
			}
			return services.ToArray();
		});
	}

	// Names of configured speech-to-text services that have no resolvable API key.
	// These are skipped during service creation (so startup no longer crashes), and
	// the names are surfaced in the missing-key warning that opens the API keys tab.
	private static List<string> GetSpeechServicesMissingApiKey(Settings settings)
	{
		var missing = new List<string>();
		var sttSettings = settings.SpeechToTextSettings?.Services ?? Array.Empty<SpeechToTextServiceSettings>();
		foreach (var serviceSettings in sttSettings)
		{
			string? rootKey = serviceSettings.Provider switch
			{
				SpeechToTextProviders.OpenAi => settings.ApiKeys?.OpenAiApiKey,
				SpeechToTextProviders.Deepgram => settings.ApiKeys?.DeepgramApiKey,
				_ => null,
			};

			// Only providers that require a key participate; unknown/None providers are ignored.
			if (serviceSettings.Provider != SpeechToTextProviders.OpenAi
				&& serviceSettings.Provider != SpeechToTextProviders.Deepgram)
				continue;

			string apiKey = ResolveServiceApiKey(rootKey, serviceSettings.ApiKey);
			if (string.IsNullOrEmpty(apiKey))
			{
				string name = string.IsNullOrWhiteSpace(serviceSettings.Name)
					? serviceSettings.Provider.ToString()
					: serviceSettings.Name!;
				missing.Add(name);
			}
		}

		return missing;
	}

	// Resolve the API key for a speech service: the per-service key is an optional
	// override that wins when set; otherwise the central root-level key is used.
	// A placeholder value counts as "not set" so it never shadows a real root key.
	private static string ResolveServiceApiKey(string? rootKey, string? overrideKey)
	{
		string ov = overrideKey?.Trim() ?? string.Empty;
		if (ov.Length > 0 && ov != SettingsDefaults.PlaceholderValue)
			return ov;

		string root = rootKey?.Trim() ?? string.Empty;
		return root == SettingsDefaults.PlaceholderValue ? string.Empty : root;
	}

	private static ISpeechToTextService CreateWhisperSpeechToTextService(HostApplicationBuilder builder, SpeechToTextServiceSettings serviceSettings, string apiKey, IServiceProvider sp)
	{
		string baseDomain = serviceSettings.BaseDomain?.Trim() ?? string.Empty;
		string modelId = serviceSettings.ModelId ?? string.Empty;

		AudioClient audioClient;
		if (!string.IsNullOrEmpty(baseDomain))
		{
			if (!baseDomain.EndsWith("/v1") && !baseDomain.EndsWith("/v1/"))
			{
				baseDomain = baseDomain.TrimEnd('/') + "/v1/";
			}
			var options = OpenAiClientOptionsFactory.Create(new Uri(baseDomain));
			var client = new OpenAIClient(new ApiKeyCredential(apiKey), options);
			audioClient = client.GetAudioClient(modelId);
		}
		else
		{
			audioClient = new AudioClient(modelId, new ApiKeyCredential(apiKey), OpenAiClientOptionsFactory.Create());
		}

		return new OpenAiSpeechToTextService(
				  serviceSettings.Name ?? string.Empty,
				  audioClient,
				  serviceSettings.TimeoutSeconds > 0 ? serviceSettings.TimeoutSeconds : 10);
	}

	private static ISpeechToTextService CreateDeepgramSpeechToTextService(HostApplicationBuilder builder, SpeechToTextServiceSettings serviceSettings, string apiKey)
	{
		Deepgram.Clients.Interfaces.v1.IListenRESTClient deepgramClient = ClientFactory.CreateListenRESTClient(apiKey);

		return new DeepgramSpeechToTextService(
				  serviceSettings.Name ?? string.Empty,
				  deepgramClient,
				  serviceSettings.ModelId ?? string.Empty,
				  serviceSettings.TimeoutSeconds > 0 ? serviceSettings.TimeoutSeconds : 10);
	}
}
