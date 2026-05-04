using CognitiveSupport;
using Microsoft.UI.Xaml;
using Mutation.Ui.Core;
using System;
using System.Collections.Generic;
using System.ComponentModel;
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
	private readonly Dictionary<int, Action> _callbacks = new();
	// Track a normalized representation of each registered hotkey so we can avoid
	// attempting duplicate registrations (which Windows will report as a failure
	// even though one instance is already active). This prevents false positives
	// in the FailedRegistrations list when router hotkeys are refreshed.
	private readonly HashSet<string> _registeredHotkeys = new(StringComparer.Ordinal);
    private readonly List<int> _routerIds = new();
    private readonly Dictionary<int, string> _routerNormalizedHotkeys = new();
    private readonly List<string> _routerFailedRegistrations = new();

    private readonly List<int> _promptIds = new();
    private readonly Dictionary<int, string> _promptNormalizedHotkeys = new();

	private readonly List<string> _coreFailedRegistrations = new();
	private readonly Settings _settings;
	private static SynchronizationContext? s_uiCtx;
	private int _id;
	private IntPtr _prevWndProc;
	private WndProcDelegate? _newWndProc;
	private GCHandle _wndProcHandle;

		public List<string> FailedRegistrations { get; } = new(); // aggregate (router + core)
		public IReadOnlyList<string> CoreFailedRegistrations => _coreFailedRegistrations; // only core (non-router) failures

        public sealed record HotkeyRegistrationResult(HotKeyRouterSettings.HotKeyRouterMap Map, string NormalizedHotkey, bool Success, int Id, string? ErrorMessage);

        private readonly struct HotkeyRegistrationAttempt
        {
                public HotkeyRegistrationAttempt(string normalizedHotkey, int id, bool success, string? errorMessage)
                {
                        NormalizedHotkey = normalizedHotkey;
                        Id = id;
                        Success = success;
                        ErrorMessage = errorMessage;
                }

                public string NormalizedHotkey { get; }
                public int Id { get; }
                public bool Success { get; }
                public string? ErrorMessage { get; }
        }

	private const int WM_HOTKEY = 0x0312;
	private const int GWLP_WNDPROC = -4;

	private const uint MOD_ALT = 0x1;
	private const uint MOD_CONTROL = 0x2;
	private const uint MOD_SHIFT = 0x4;
	private const uint MOD_WIN = 0x8;
	private const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll", SetLastError = true)] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);
	[DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);
	[DllImport("user32.dll")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr newProc);
	[DllImport("user32.dll")] static extern IntPtr CallWindowProc(IntPtr lpPrevWndFunc, IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
	[DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
	[DllImport("user32.dll")] static extern short GetAsyncKeyState(int vKey);
	[DllImport("user32.dll")] static extern uint MapVirtualKey(uint uCode, uint uMapType);

	private const uint MAPVK_VK_TO_VSC = 0x0;

	private const int INPUT_KEYBOARD = 1;
	private const uint KEYEVENTF_KEYUP = 0x0002;
	private const uint KEYEVENTF_UNICODE = 0x0004;
	private const uint KEYEVENTF_SCANCODE = 0x0008;
	private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;

	[StructLayout(LayoutKind.Sequential)]
	private struct INPUT
	{
		public uint type;
		public INPUTUNION U;
	}

	[StructLayout(LayoutKind.Explicit)]
	private struct INPUTUNION
	{
		[FieldOffset(0)] public KEYBDINPUT ki;
	}

	[StructLayout(LayoutKind.Sequential)]
	private struct KEYBDINPUT
	{
		public ushort wVk;
		public ushort wScan;
		public uint dwFlags;
		public uint time;
		public IntPtr dwExtraInfo;
	}

	private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

	public HotkeyManager(Window window, Settings settings)
	{
		_hwnd = WindowNative.GetWindowHandle(window);
		_settings = settings ?? throw new ArgumentNullException(nameof(settings));
		_newWndProc = WndProc;
		_wndProcHandle = GCHandle.Alloc(_newWndProc);
		_prevWndProc = SetWindowLongPtr(_hwnd, GWLP_WNDPROC, Marshal.GetFunctionPointerForDelegate(_newWndProc));
		// Capture UI thread context so we can run SendKeys fallback on STA with a message pump
		s_uiCtx ??= SynchronizationContext.Current;
	}

	        public int RegisterHotkey(Hotkey hotkey, Action callback)
	        {
	                var attempt = RegisterHotkeyInternal(hotkey, callback, isRouter: false);
	                return attempt.Id;
	        }

	        private HotkeyRegistrationAttempt RegisterHotkeyInternal(Hotkey hotkey, Action callback, bool isRouter)
        {
                string norm = NormalizeHotkey(hotkey);
                if (_registeredHotkeys.Contains(norm))
                {
                        Log($"Duplicate hotkey detected: {norm}");
	                        if (isRouter)
	                                _routerFailedRegistrations.Add(hotkey.ToString());
	                        else
	                        {
	                                FailedRegistrations.Add(hotkey.ToString());
	                                _coreFailedRegistrations.Add(hotkey.ToString());
	                        }
                        return new HotkeyRegistrationAttempt(norm, -1, success: false, errorMessage: "The shortcut is already registered.");
                }

                int id = Interlocked.Increment(ref _id);
                uint mods = (hotkey.Alt ? MOD_ALT : 0) |
                                                                                          (hotkey.Control ? MOD_CONTROL : 0) |
                                                                                          (hotkey.Shift ? MOD_SHIFT : 0) |
                                                                                          (hotkey.Win ? MOD_WIN : 0);
				bool success = RegisterHotKey(_hwnd, id, mods, (uint)hotkey.Key);
                if (success)
                {
                        _callbacks[id] = callback;
                        _registeredHotkeys.Add(norm);
                        Log($"Hotkey registered: {norm} (id={id})");
                        return new HotkeyRegistrationAttempt(norm, id, true, null);
                }

				if (_registeredHotkeys.Contains(norm))
                {
                        Log($"RegisterHotKey reported failure but hotkey already active (suppressing): {norm}");
                        return new HotkeyRegistrationAttempt(norm, id, true, null);
                }

                int error = Marshal.GetLastWin32Error();
                string? message = null;
                if (error != 0)
                {
                        message = error switch
                        {
                                1409 => "The shortcut is already registered by another application.",
                                _ => new Win32Exception(error).Message
                        };
                }
                message ??= "Failed to register the shortcut.";

				if (isRouter)
					_routerFailedRegistrations.Add(hotkey.ToString());
				else
				{
					FailedRegistrations.Add(hotkey.ToString());
					_coreFailedRegistrations.Add(hotkey.ToString());
				}
                Log($"Hotkey registration FAILED: {norm} (error={error})");
                return new HotkeyRegistrationAttempt(norm, id, false, message);
        }

        public IReadOnlyList<HotkeyRegistrationResult> RegisterRouterHotkeys()
        {
                if (_settings.HotKeyRouterSettings is null)
                        return Array.Empty<HotkeyRegistrationResult>();

                return RegisterRouterHotkeys(_settings.HotKeyRouterSettings.Mappings);
        }

	        public IReadOnlyList<HotkeyRegistrationResult> RegisterRouterHotkeys(IEnumerable<HotKeyRouterSettings.HotKeyRouterMap> mappings)
        {
                foreach (var previous in _routerFailedRegistrations)
				FailedRegistrations.Remove(previous); // keep core failures intact
                _routerFailedRegistrations.Clear();

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
			                                var attempt = RegisterHotkeyInternal(hotkey, () => SendHotkeyAfterDelay(map.ToHotKey!, Constants.FailureSendHotkeyDelay), isRouter: true);
                                if (attempt.Success)
                                {
                                        if (attempt.Id > 0)
                                        {
                                                _routerIds.Add(attempt.Id);
                                                _routerNormalizedHotkeys[attempt.Id] = attempt.NormalizedHotkey;
                                        }

                                        Log($"Router registered: From='{map.FromHotKey}' -> To='{map.ToHotKey}', id={attempt.Id}");
                                        results.Add(new HotkeyRegistrationResult(map, attempt.NormalizedHotkey, true, attempt.Id, null));
                                }
                                else
                                {
                                        var failureKey = hotkey.ToString();
                                        _routerFailedRegistrations.Add(failureKey);
                                        Log($"Router registration FAILED: From='{map.FromHotKey}' -> To='{map.ToHotKey}' ({attempt.ErrorMessage})");
                                        results.Add(new HotkeyRegistrationResult(map, attempt.NormalizedHotkey, false, attempt.Id, attempt.ErrorMessage ?? "The shortcut could not be registered."));
                                }
                        }
                        catch (Exception ex)
                        {
                                FailedRegistrations.Add(map.FromHotKey ?? string.Empty);
                                _routerFailedRegistrations.Add(map.FromHotKey ?? string.Empty);
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
                foreach (var id in _routerIds)
                {
                        UnregisterHotKey(_hwnd, id);
                        _callbacks.Remove(id);
                        if (_routerNormalizedHotkeys.TryGetValue(id, out var norm))
                        {
                                _routerNormalizedHotkeys.Remove(id);
                                _registeredHotkeys.Remove(norm);
                        }
                }

                _routerIds.Clear();
                foreach (var previous in _routerFailedRegistrations)
                        FailedRegistrations.Remove(previous);
                _routerFailedRegistrations.Clear();
        }

        public void RegisterPromptHotkeys(IEnumerable<LlmSettings.LlmPrompt> prompts, Action<LlmSettings.LlmPrompt> callback)
        {
            ClearPromptHotkeys();
            if (prompts == null) return;

            foreach (var prompt in prompts)
            {
                if (string.IsNullOrWhiteSpace(prompt.Hotkey)) continue;

                try
                {
                    var hotkey = Hotkey.Parse(prompt.Hotkey);
                    var attempt = RegisterHotkeyInternal(hotkey, () => callback(prompt), isRouter: false); // Treat as core or unique type? 
                    // reusing RegisterHotkeyInternal. 'isRouter' tracks failures separately. 
                    // Let's treat prompts as core for failure tracking for now, or add a flag. 
                    // The current method separates core vs router. 
                    // If I pass isRouter=false, it adds to FailedRegistrations and _coreFailedRegistrations.
                    // This seems acceptable.

                    if (attempt.Success && attempt.Id > 0)
                    {
                        _promptIds.Add(attempt.Id);
                        _promptNormalizedHotkeys[attempt.Id] = attempt.NormalizedHotkey;
                    }
                }
                catch (Exception ex)
                {
                    Log($"Prompt hotkey failed: {prompt.Name} ({prompt.Hotkey}) - {ex.Message}");
                    FailedRegistrations.Add(prompt.Hotkey);
                }
            }
        }

        public void ClearPromptHotkeys()
        {
            foreach (var id in _promptIds)
            {
                UnregisterHotKey(_hwnd, id);
                _callbacks.Remove(id);
                if (_promptNormalizedHotkeys.TryGetValue(id, out var norm))
                {
                    _promptNormalizedHotkeys.Remove(id);
                    _registeredHotkeys.Remove(norm);
                }
            }
            _promptIds.Clear();
            _promptNormalizedHotkeys.Clear();
        }

        public void UnregisterAll()
        {
                foreach (var kvp in _callbacks)
                        UnregisterHotKey(_hwnd, kvp.Key);
                _callbacks.Clear();
                _routerIds.Clear();
                _routerNormalizedHotkeys.Clear();
                _promptIds.Clear();
                _promptNormalizedHotkeys.Clear();
                _registeredHotkeys.Clear();
        }

	private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
	{
		if (msg == WM_HOTKEY)
		{
			int id = wParam.ToInt32();
			Log($"WM_HOTKEY received: id={id}");
			if (_callbacks.TryGetValue(id, out var cb))
				cb();
			return IntPtr.Zero;
		}
		return CallWindowProc(_prevWndProc, hWnd, msg, wParam, lParam);
	}

	public static void SendHotkeyAfterDelay(string hotkey, int delayMs)
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

	public static void SendHotkey(string hotkey)
	{
		if (string.IsNullOrWhiteSpace(hotkey))
			return;

		// Quick override for diagnostics: set MUTATION_FORCE_SENDKEYS=1 to bypass SendInput
		if (Environment.GetEnvironmentVariable("MUTATION_FORCE_SENDKEYS") == "1")
		{
			try
			{
				string mappedHotkey = SendKeysMapper.Map(hotkey);
				Log($"ENV override: Fallback SendKeys: '{mappedHotkey}' (from '{hotkey}')");
				SendKeysOnUiThread(mappedHotkey);
				return;
			}
			catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"SendKeys env override failed: {ex.Message}"); }
		}

		// Support sequences like "Ctrl+C, Ctrl+V"
		var parts = hotkey.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		if (parts.Length == 0)
			return;

		bool allSentViaInput = true;
		foreach (var part in parts)
		{
			try
			{
				var hk = Hotkey.Parse(part);
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

	public static void SendText(string text)
	{
		if (string.IsNullOrEmpty(text))
			return;

		List<INPUT> inputs = new();
		foreach (char c in text)
		{
			inputs.Add(new INPUT
			{
				type = INPUT_KEYBOARD,
				U = new INPUTUNION { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE } }
			});
			inputs.Add(new INPUT
			{
				type = INPUT_KEYBOARD,
				U = new INPUTUNION { ki = new KEYBDINPUT { wScan = c, dwFlags = KEYEVENTF_UNICODE | KEYEVENTF_KEYUP } }
			});
		}
		SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
	}

	private static bool SendHotkeyViaSendInput(Hotkey hotkey)
	{
		// Wait until user releases modifier keys from the original chord to avoid contamination
		WaitForModifierRelease(timeoutMs: AppConstants.ModifierReleaseTimeoutMs);

		var inputs = new List<INPUT>();

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
			var preSent = SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
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
		var sent = SendInput(count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
		bool ok = sent == count && count > 0;
		if (!ok)
		{
			int err = Marshal.GetLastWin32Error();
			Log($"SendInput returned {sent}/{count}, GetLastError={err}");
		}
		return ok;
	}

	private const ushort VK_CONTROL = 0x11;
	private const ushort VK_SHIFT = 0x10;
	private const ushort VK_MENU = 0x12; // ALT
	private const ushort VK_LWIN = 0x5B;

	private static INPUT KeyDown(ushort vk)
	{
		return new INPUT
		{
			type = INPUT_KEYBOARD,
			U = new INPUTUNION
			{
				ki = new KEYBDINPUT
				{
					wVk = vk,
					wScan = 0,
					dwFlags = (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0),
					time = 0,
					dwExtraInfo = IntPtr.Zero
				}
			}
		};
	}

	private static INPUT KeyUp(ushort vk)
	{
		return new INPUT
		{
			type = INPUT_KEYBOARD,
			U = new INPUTUNION
			{
				ki = new KEYBDINPUT
				{
					wVk = vk,
					wScan = 0,
					dwFlags = KEYEVENTF_KEYUP | (IsExtended(vk) ? KEYEVENTF_EXTENDEDKEY : 0),
					time = 0,
					dwExtraInfo = IntPtr.Zero
				}
			}
		};
	}

	private static bool IsExtended(ushort vk)
	{
		// Extended key flag for navigation cluster and some others
		return vk is 0x21 /*PGUP*/ or 0x22 /*PGDN*/ or 0x23 /*END*/ or 0x24 /*HOME*/
				or 0x25 /*LEFT*/ or 0x26 /*UP*/ or 0x27 /*RIGHT*/ or 0x28 /*DOWN*/
				or 0x2D /*INSERT*/ or 0x2E /*DELETE*/ or 0x5B /*LWIN*/ or 0x5C /*RWIN*/
				or 0xA1 /*RSHIFT*/ or 0xA3 /*RCONTROL*/ or 0xA5 /*RMENU(ALT)*/;
	}

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

	private static string NormalizeHotkey(Hotkey hk)
	{
		// Construct a deterministic modifier order & uppercase key for set membership
		var sb = new System.Text.StringBuilder(32);
		if (hk.Control) sb.Append("CTRL+");
		if (hk.Shift) sb.Append("SHIFT+");
		if (hk.Alt) sb.Append("ALT+");
		if (hk.Win) sb.Append("WIN+");
		sb.Append(hk.Key.ToString().ToUpperInvariant());
		return sb.ToString();
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
