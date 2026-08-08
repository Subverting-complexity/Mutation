using CognitiveSupport;
using Microsoft.UI.Xaml;
using Mutation.Ui.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using WinRT.Interop;

namespace Mutation.Ui.Services;

public class HotkeyManager : IDisposable
{
	private readonly IntPtr _hwnd;

	// Which shortcuts are taken, which id carries which callback, and which ids a refresh
	// has to release. Extracted so those rules can be tested without a window handle — see
	// HotkeyRegistrationTable.
	private readonly HotkeyRegistrationTable _registrations;

	private readonly Settings _settings;
	private static SynchronizationContext? s_uiCtx;
	private IntPtr _prevWndProc;
	private WndProcDelegate? _newWndProc;
	private GCHandle _wndProcHandle;

        public sealed record HotkeyRegistrationResult(HotKeyRouterSettings.HotKeyRouterMap Map, string NormalizedHotkey, bool Success, int Id, string? ErrorMessage);

        // A single hotkey that could not be bound, described in user-facing terms so it can be
        // shown in a dialog the same way router failures are. Description is a human label for
        // the action (e.g. "Start/stop dictation") or the prompt name; Hotkey is the configured
        // text; Reason explains why it failed (parse error or registration conflict).
        public sealed record HotkeyBindingFailure(string Description, string Hotkey, string Reason);

        // Pure classification of a configured hotkey string. Returns a failure when the text is
        // absent (and not allowed) or cannot be parsed. Registration conflicts are detected later,
        // during the actual RegisterHotKey call. Kept static and side-effect-free so it is unit
        // testable without a window handle.
        public static HotkeyBindingFailure? ClassifyConfiguredHotkey(string description, string? hotkeyText, bool allowEmpty)
        {
                if (string.IsNullOrWhiteSpace(hotkeyText))
                        return allowEmpty ? null : new HotkeyBindingFailure(description, string.Empty, "Enter a hotkey.");

                string? error = HotkeyValidator.Validate(hotkeyText, allowSendKeysSyntax: false);
                return error is null ? null : new HotkeyBindingFailure(description, hotkeyText.Trim(), error);
        }

        // Builds the bulleted message body shown to the user for a set of binding failures.
        public static string BuildFailureMessage(IReadOnlyList<HotkeyBindingFailure> failures)
        {
                if (failures is null || failures.Count == 0)
                        return string.Empty;

                var lines = failures.Select(f =>
                {
                        var reason = string.IsNullOrWhiteSpace(f.Reason) ? "Unknown error." : f.Reason;
                        return string.IsNullOrWhiteSpace(f.Hotkey)
                                ? $"• {f.Description}: {reason}"
                                : $"• {f.Description} ({f.Hotkey}): {reason}";
                });
                return string.Join(Environment.NewLine, lines);
        }

        // Projects the failed entries of a router registration pass into the shared failure shape
        // (Description = "from -> to") so router, core, and prompt failures render identically.
        public static IReadOnlyList<HotkeyBindingFailure> ToBindingFailures(IReadOnlyList<HotkeyRegistrationResult> results)
        {
                if (results is null || results.Count == 0)
                        return Array.Empty<HotkeyBindingFailure>();

                var failures = new List<HotkeyBindingFailure>();
                foreach (var r in results)
                {
                        if (r.Success)
                                continue;
                        var from = string.IsNullOrWhiteSpace(r.Map?.FromHotKey) ? "(empty)" : r.Map!.FromHotKey!;
                        var to = string.IsNullOrWhiteSpace(r.Map?.ToHotKey) ? "(empty)" : r.Map!.ToHotKey!;
                        var reason = string.IsNullOrWhiteSpace(r.ErrorMessage) ? "Unknown error." : r.ErrorMessage!;
                        failures.Add(new HotkeyBindingFailure($"{from} → {to}", string.Empty, reason));
                }
                return failures;
        }

	private const int WM_HOTKEY = 0x0312;
	private const int GWLP_WNDPROC = -4;

	// Modifier flags live in HotkeyModifiers so their composition — including
	// MOD_NOREPEAT — can be tested without a window handle.

	// RegisterHotKey/UnregisterHotKey live in Win32HotkeyPlatform, behind IHotkeyPlatform.
	// The SendInput structs live in KeyboardInput, where their size — which is what Windows
	// validates — can be asserted in a test.
	[DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newProc);
	[DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
	[DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);

	private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	public HotkeyManager(Window window, Settings settings)
	{
		_hwnd = WindowNative.GetWindowHandle(window);
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_registrations = new HotkeyRegistrationTable(new Win32HotkeyPlatform(_hwnd), Log);
		_newWndProc = WndProc;
		_wndProcHandle = GCHandle.Alloc(_newWndProc);
		_prevWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
		// Capture UI thread context so we can run SendKeys fallback on STA with a message pump
		s_uiCtx ??= SynchronizationContext.Current;
	}

	        public int RegisterHotkey(Hotkey hotkey, Action callback)
	        {
	                return _registrations.Register(hotkey, callback, HotkeyRegistrationTable.HotkeyGroup.Core).Id;
	        }

	        // Registers a core (non-router) hotkey and returns a failure description when the
	        // configured text cannot be parsed or the shortcut cannot be bound. Returns null on
	        // success. This replaces the old fire-and-forget TryRegister that swallowed both the
	        // parse exception and the registration conflict to Debug output only.
	        public HotkeyBindingFailure? TryRegisterCore(string description, string? hotkeyText, Action callback)
	        {
	                var parseFailure = ClassifyConfiguredHotkey(description, hotkeyText, allowEmpty: true);
	                if (parseFailure is not null)
	                {
	                        Log($"Core hotkey '{hotkeyText}' ({description}) invalid: {parseFailure.Reason}");
	                        return parseFailure;
	                }

	                var attempt = _registrations.Register(Hotkey.Parse(hotkeyText!), callback, HotkeyRegistrationTable.HotkeyGroup.Core);
	                if (attempt.Success)
	                        return null;

	                return new HotkeyBindingFailure(description, hotkeyText!.Trim(),
	                        attempt.ErrorMessage ?? "The shortcut could not be registered.");
	        }

        public IReadOnlyList<HotkeyRegistrationResult> RegisterRouterHotkeys()
        {
                if (_settings.HotKeyRouterSettings is null)
                        return Array.Empty<HotkeyRegistrationResult>();

                return RegisterRouterHotkeys(_settings.HotKeyRouterSettings.Mappings);
        }

	        public IReadOnlyList<HotkeyRegistrationResult> RegisterRouterHotkeys(IEnumerable<HotKeyRouterSettings.HotKeyRouterMap> mappings)
        {
                var results = new List<HotkeyRegistrationResult>();

                foreach (var map in mappings)
                {
                        if (string.IsNullOrWhiteSpace(map.FromHotKey) || string.IsNullOrWhiteSpace(map.ToHotKey))
                        {
                                results.Add(new HotkeyRegistrationResult(map, map.FromHotKey ?? string.Empty, false, -1, "Both hotkeys must be configured."));
                                continue;
                        }

			                        try
			                        {
			                                var hotkey = Hotkey.Parse(map.FromHotKey!);
			                                var attempt = _registrations.Register(hotkey, () => SendHotkeyAfterDelay(map.ToHotKey!, PostOperationHotkey.FailureDelayMs), HotkeyRegistrationTable.HotkeyGroup.Router);
                                if (attempt.Success)
                                {
                                        Log($"Router registered: From='{map.FromHotKey}' -> To='{map.ToHotKey}', id={attempt.Id}");
                                        results.Add(new HotkeyRegistrationResult(map, attempt.NormalizedHotkey, true, attempt.Id, null));
                                }
                                else
                                {
                                        Log($"Router registration FAILED: From='{map.FromHotKey}' -> To='{map.ToHotKey}' ({attempt.ErrorMessage})");
                                        results.Add(new HotkeyRegistrationResult(map, attempt.NormalizedHotkey, false, attempt.Id, attempt.ErrorMessage ?? "The shortcut could not be registered."));
                                }
                        }
                        catch (Exception ex)
                        {
                                Log($"Router registration FAILED: From='{map.FromHotKey}' -> To='{map.ToHotKey}' ({ex.Message})");
                                results.Add(new HotkeyRegistrationResult(map, map.FromHotKey ?? string.Empty, false, -1, ex.Message));
                        }
                }

                return results;
        }

        public IReadOnlyList<HotkeyRegistrationResult> RefreshRouterHotkeys()
        {
                if (_settings.HotKeyRouterSettings is null)
                {
                        ClearRouterHotkeys();
                        return Array.Empty<HotkeyRegistrationResult>();
                }

                return RefreshRouterHotkeys(_settings.HotKeyRouterSettings.Mappings);
        }

        public IReadOnlyList<HotkeyRegistrationResult> RefreshRouterHotkeys(IEnumerable<HotKeyRouterSettings.HotKeyRouterMap> mappings)
        {
                ClearRouterHotkeys();
                return RegisterRouterHotkeys(mappings);
        }

        private void ClearRouterHotkeys()
        {
                _registrations.ClearGroup(HotkeyRegistrationTable.HotkeyGroup.Router);
        }

        // Registers each prompt's optional hotkey and returns the ones that could not be bound,
        // labelled by prompt name, so the caller can surface them the same way router and core
        // failures are surfaced.
        public IReadOnlyList<HotkeyBindingFailure> RegisterPromptHotkeys(IEnumerable<LlmSettings.LlmPrompt> prompts, Action<LlmSettings.LlmPrompt> callback)
        {
            ClearPromptHotkeys();
            var failures = new List<HotkeyBindingFailure>();
            if (prompts == null) return failures;

            foreach (var prompt in prompts)
            {
                if (string.IsNullOrWhiteSpace(prompt.Hotkey)) continue;

                string description = string.IsNullOrWhiteSpace(prompt.Name) ? "(unnamed prompt)" : prompt.Name;

                var parseFailure = ClassifyConfiguredHotkey(description, prompt.Hotkey, allowEmpty: true);
                if (parseFailure is not null)
                {
                    Log($"Prompt hotkey invalid: {description} ({prompt.Hotkey}) - {parseFailure.Reason}");
                    failures.Add(parseFailure);
                    continue;
                }

                var attempt = _registrations.Register(Hotkey.Parse(prompt.Hotkey), () => callback(prompt), HotkeyRegistrationTable.HotkeyGroup.Prompt);
                if (!attempt.Success)
                {
                    Log($"Prompt hotkey registration FAILED: {description} ({prompt.Hotkey}) - {attempt.ErrorMessage}");
                    failures.Add(new HotkeyBindingFailure(description, prompt.Hotkey.Trim(),
                        attempt.ErrorMessage ?? "The shortcut could not be registered."));
                }
            }

            return failures;
        }

        public void ClearPromptHotkeys()
        {
            _registrations.ClearGroup(HotkeyRegistrationTable.HotkeyGroup.Prompt);
        }

        public void UnregisterAll()
        {
                _registrations.ClearAll();
        }

        // Clears ALL registrations (core + router + prompt) so the caller can
        // re-register everything from a (possibly mutated) Settings instance.
        public void ClearAllForRebind()
        {
                UnregisterAll();
        }

	private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == WM_HOTKEY)
		{
			int id = wParam.ToInt32();
			Log($"WM_HOTKEY received: id={id}");
			_registrations.Dispatch(id);
			return IntPtr.Zero;
		}
		return CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
	}

	public static void SendHotkeyAfterDelay(string? hotkey, int delayMs)
	{
		if (string.IsNullOrWhiteSpace(hotkey))
			return;

		_ = Task.Run(async () =>
		{
			await Task.Delay(delayMs).ConfigureAwait(false);
			Log($"SendHotkeyAfterDelay firing: '{hotkey}'");
			SendHotkey(hotkey);
		});
	}

	/// <summary>
	/// Sends <paramref name="hotkey"/> — one chord, or a comma-separated sequence — to
	/// whatever window has the keyboard focus.
	/// </summary>
	/// <returns>
	/// True only when every chord was confirmed delivered through SendInput. The
	/// WinForms SendKeys fallback still runs as a best effort, but it cannot report
	/// whether the target window took the keystrokes, so a run that needed it returns
	/// false. Callers that announce the result must treat that as "could not confirm
	/// delivery": for a blind user a false success is worse than a false alarm, because
	/// nothing else in the session will ever contradict it (issue #232).
	/// </returns>
	public static bool SendHotkey(string hotkey)
	{
		if (string.IsNullOrWhiteSpace(hotkey))
			return true;

		// Quick override for diagnostics: set MUTATION_FORCE_SENDKEYS=1 to bypass SendInput
		if (Environment.GetEnvironmentVariable("MUTATION_FORCE_SENDKEYS") == "1")
		{
			try
			{
				string mappedHotkey = SendKeysMapper.Map(hotkey);
				Log($"ENV override: Fallback SendKeys: '{mappedHotkey}' (from '{hotkey}')");
				SendKeysOnUiThread(mappedHotkey);
				return false;
			}
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SendKeys env override failed: {ex.Message}"); }
		}

		// Support sequences like "Ctrl+C, Ctrl+V"
		var parts = hotkey.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0)
			return true;

		bool allSentViaInput = true;
		foreach (var part in parts)
		{
			try
			{
				var hk = ResolveChord(part);
				if (hk is null)
				{
					Log($"'{part}' is not a chord SendInput can express, falling back to SendKeys mapping.");
					allSentViaInput = false;
					break;
				}

				Log($"Sending via SendInput: '{part}'");
				bool ok = SendHotkeyViaSendInput(hk);
				if (!ok)
				{
					Log($"SendInput failed for '{part}', falling back to SendKeys mapping.");
					allSentViaInput = false;
					break;
				}
				// Small gap between chords. Thread.Sleep is acceptable here because:
				// - This runs on a background thread via Task.Run
				// - The delay is very short (25ms)
				// - Converting to async would add complexity without significant benefit
					Thread.Sleep(AppConstants.HotkeyChordDelayMs);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"SendInput parse failed for chord: {ex.Message}");
				allSentViaInput = false;
				break;
			}
		}

		if (!allSentViaInput)
		{
			// Fallback: try WinForms SendKeys mapping for complex/unsupported chords
			try
			{
				string mappedHotkey = SendKeysMapper.Map(hotkey);
				Log($"Fallback SendKeys: '{mappedHotkey}' (from '{hotkey}')");
				SendKeysOnUiThread(mappedHotkey);
			}
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SendKeys fallback failed: {ex.Message}"); }
		}

		return allSentViaInput;
	}

	/// <summary>
	/// The chord <paramref name="text"/> spells, however the user wrote it, or null when it
	/// is something only the WinForms fallback can express.
	/// </summary>
	/// <remarks>
	/// Both spellings are accepted because both are on screen: the hotkey editors take
	/// "CTRL+DELETE", while the two "send this afterwards" boxes have always also accepted
	/// SendKeys notation, and settings saved years ago still hold "^{DEL}". Reading the
	/// second form here is what puts it on the SendInput path, where a failure to deliver
	/// can be seen (PR #328).
	/// </remarks>
	internal static Hotkey? ResolveChord(string text)
	{
		if (Hotkey.TryParse(text, out var parsed))
			return parsed;

		return SendKeysChord.TryParse(text, out var fromSendKeys) ? fromSendKeys : null;
	}

	private static void SendKeysOnUiThread(string mapped)
	{
		try
		{
			if (string.IsNullOrEmpty(mapped)) return;
			if (s_uiCtx is null || SynchronizationContext.Current == s_uiCtx)
			{
				System.Windows.Forms.SendKeys.SendWait(mapped);
				return;
			}
			// Post asynchronously; no need to wait/block.
			_ = PostSendKeysAsync(mapped);
		}
		catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SendKeysOnUiThread failed: {ex.Message}"); }
	}

	private static Task PostSendKeysAsync(string mapped)
	{
		var tcs = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
		try
		{
			s_uiCtx!.Post(_ =>
			{
				try
				{
					System.Windows.Forms.SendKeys.SendWait(mapped);
					tcs.SetResult(null);
				}
				catch (Exception ex)
				{
					// Do not rethrow; log if desired.
					Log($"SendKeys (fallback) failed: {ex.Message}");
					if (!tcs.Task.IsCompleted) tcs.SetException(ex);
				}
			}, null);
		}
		catch (Exception ex)
		{
			Log($"Failed to post SendKeys: {ex.Message}");
			if (!tcs.Task.IsCompleted) tcs.SetException(ex);
		}
		return tcs.Task;
	}

	/// <summary>
	/// Types <paramref name="text"/> into whatever window has the keyboard focus.
	/// </summary>
	/// <returns>
	/// True when Windows accepted every keystroke. False when it accepted fewer than
	/// were submitted — which is what happens when the foreground application runs with
	/// higher privileges than Mutation and Windows drops the input silently. The caller
	/// has to tell the user, or they are left believing their dictation landed in an
	/// application that never received a character (issue #232).
	/// </returns>
	public static bool SendText(string text)
	{
		if (string.IsNullOrEmpty(text))
			return true;

		List<KeyboardInput.INPUT> inputs = new();
		foreach (char c in text)
		{
			inputs.Add(KeyboardInput.UnicodeDown(c));
			inputs.Add(KeyboardInput.UnicodeUp(c));
		}
		uint submitted = (uint)inputs.Count;
		uint sent = KeyboardInput.Send(inputs.ToArray());
		if (sent == submitted)
			return true;

		Log($"SendText delivered {sent}/{submitted} keystrokes, GetLastError={Marshal.GetLastWin32Error()}");
		return false;
	}

	private static bool SendHotkeyViaSendInput(Hotkey hotkey)
	{
		// Wait until user releases modifier keys from the original chord to avoid contamination
		WaitForModifierRelease(timeoutMs: AppConstants.ModifierReleaseTimeoutMs);

		var inputs = new List<KeyboardInput.INPUT>();

		bool needCtrl = hotkey.Control;
		bool needShift = hotkey.Shift;
		bool needAlt = hotkey.Alt;
		bool needWin = hotkey.Win;

		// If any physical modifiers are still down and not needed for the target chord, release them first
		if (!needShift && (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0)
			inputs.Add(KeyUp(VK_SHIFT));
		if (!needAlt && (GetAsyncKeyState(VK_MENU) & 0x8000) != 0)
			inputs.Add(KeyUp(VK_MENU));
		if (!needWin && (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0)
			inputs.Add(KeyUp(VK_LWIN));
		if (!needCtrl && (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0)
			inputs.Add(KeyUp(VK_CONTROL));

		if (inputs.Count > 0)
		{
			var preSent = KeyboardInput.Send(inputs.ToArray());
			Log($"Pre-release injected {preSent}/{inputs.Count} modifier key-ups.");
			inputs.Clear();
			// Brief delay after releasing modifiers. Thread.Sleep is acceptable here
			// as this runs on a background thread and the delay is minimal.
			Thread.Sleep(AppConstants.ModifierReleaseDelayMs);
		}

		// Press modifiers (Ctrl, Shift, Alt) down in canonical order
		if (needCtrl) inputs.Add(KeyDown(VK_CONTROL));
		if (needShift) inputs.Add(KeyDown(VK_SHIFT));
		if (needAlt) inputs.Add(KeyDown(VK_MENU));
		if (needWin) inputs.Add(KeyDown(VK_LWIN));

		inputs.Add(KeyDown((ushort)hotkey.Key));
		inputs.Add(KeyUp((ushort)hotkey.Key));

		if (hotkey.Win) inputs.Add(KeyUp(VK_LWIN));
		if (hotkey.Alt) inputs.Add(KeyUp(VK_MENU));
		if (hotkey.Shift) inputs.Add(KeyUp(VK_SHIFT));
		if (hotkey.Control) inputs.Add(KeyUp(VK_CONTROL));

		var count = (uint)inputs.Count;
		var sent = KeyboardInput.Send(inputs.ToArray());
		bool ok = sent == count && count > 0;
		if (!ok)
		{
			int err = Marshal.GetLastWin32Error();
			Log($"SendInput returned {sent}/{count}, GetLastError={err}");
		}
		return ok;
	}

	private const ushort VK_CONTROL = KeyboardInput.VkControl;
	private const ushort VK_SHIFT = KeyboardInput.VkShift;
	private const ushort VK_MENU = KeyboardInput.VkAlt;
	private const ushort VK_LWIN = KeyboardInput.VkLeftWindows;

	private static KeyboardInput.INPUT KeyDown(ushort vk) => KeyboardInput.KeyDown(vk);

	private static KeyboardInput.INPUT KeyUp(ushort vk) => KeyboardInput.KeyUp(vk);

	private static void WaitForModifierRelease(int timeoutMs)
	{
		var sw = System.Diagnostics.Stopwatch.StartNew();
		while (sw.ElapsedMilliseconds < timeoutMs)
		{
			// High-order bit set means key is down
			bool ctrlDown = (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0;
			bool shiftDown = (GetAsyncKeyState(VK_SHIFT) & 0x8000) != 0;
			bool altDown = (GetAsyncKeyState(VK_MENU) & 0x8000) != 0;
			bool winDown = (GetAsyncKeyState(VK_LWIN) & 0x8000) != 0;

			if (!(ctrlDown || shiftDown || altDown || winDown))
				break;

			// Thread.Sleep is acceptable in this polling loop as it runs on a
			// background thread and keeps CPU usage low while waiting.
			Thread.Sleep(AppConstants.ModifierReleaseDelayMs);
		}
	}

	private static readonly string LogFile = Path.Combine(Path.GetTempPath(), "Mutation.Hotkey.log");
	private const long MaxLogFileSize = 100 * 1024; // 100 KB max log size

	// Off-UI-thread logging: WndProc-bound callers enqueue here without blocking;
	// a single background consumer drains the channel and performs file I/O.
	private static readonly System.Threading.Channels.Channel<string> s_logChannel =
		System.Threading.Channels.Channel.CreateUnbounded<string>(
			new System.Threading.Channels.UnboundedChannelOptions
			{
				SingleReader = true,
				SingleWriter = false,
				AllowSynchronousContinuations = false,
			});

	private static readonly Task s_logConsumer = Task.Run(LogConsumerLoopAsync);

	private static async Task LogConsumerLoopAsync()
	{
		await foreach (var line in s_logChannel.Reader.ReadAllAsync().ConfigureAwait(false))
		{
			try
			{
				if (File.Exists(LogFile))
				{
					var fileInfo = new FileInfo(LogFile);
					if (fileInfo.Length > MaxLogFileSize)
					{
						string backupPath = LogFile + ".old";
						if (File.Exists(backupPath))
							File.Delete(backupPath);
						File.Move(LogFile, backupPath);
					}
				}
				File.AppendAllText(LogFile, line);
			}
			catch (Exception ex)
			{
				System.Diagnostics.Debug.WriteLine($"HotkeyManager.Log writer failed: {ex.Message}");
			}
		}
	}

	private static void Log(string message)
	{
		var line = $"{DateTime.Now:HH:mm:ss.fff} {message}{Environment.NewLine}";
		s_logChannel.Writer.TryWrite(line);
	}

	public void Dispose()
	{
		UnregisterAll();
		// Restore the original window procedure BEFORE freeing the delegate handle
		// to prevent access violation if WndProc is called while being collected
		if (_prevWndProc != IntPtr.Zero)
		{
			SetWindowLongPtr(_hwnd, GWLP_WNDPROC, _prevWndProc);
			_prevWndProc = IntPtr.Zero;
		}

		// Free the GCHandle only after the window procedure has been restored
		if (_wndProcHandle.IsAllocated)
			_wndProcHandle.Free();
		_newWndProc = null;
	}
}
