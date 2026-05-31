# Cold-start "process with LLM" crash — fix summary

## Root cause (already diagnosed; recorded here for posterity)
1. **No retry on the LLM path.** `LlmService` (OpenAI) and `AnthropicLlmService` issued a single
   request with one timeout. The transcription path already wraps its call in a Polly
   linear-backoff retry (`OpenAiSpeechToTextService.cs`). On the FIRST launch after a reboot,
   cold-start latency (cold DNS/TLS/cert-chain + cold JIT warmup) makes the first network call
   slow or fail. With no retry, that failure propagated unhandled during "process with LLM"
   right after transcription. The second launch was fine because everything was already warm.
2. **Crash logs silently vanished.** `App.xaml.cs` wrote `Mutation_Errors.log` / `CrashLog_*.txt`
   to `AppDomain.CurrentDomain.BaseDirectory` (the EXE folder). When Mutation is installed under
   Program Files that folder is not writable, so the writes threw (and were swallowed) — no log
   ever appeared. Additionally, when the LLM call failed inside
   `AudioSessionManager.ProcessTranscriptAsync`, the exception was caught but only shown as a
   transient status message; it was never written to any log.

## Deliverable 1 — Bulletproof, dual-location crash logging

- **`CognitiveSupport/ErrorLogger.cs`** (was an untracked, single-location logger; extended in place):
  - `Write()` now appends each entry to BOTH locations, each in its own try/catch (best-effort,
    never throws): (a) `%LOCALAPPDATA%\Mutation\logs\Mutation_Errors.log` (dir created with
    `Directory.CreateDirectory`), and (b) `AppContext.BaseDirectory\Mutation_Errors.log`. One
    location failing (e.g. Program Files not writable) never stops the other.
  - Added `public static string PrimaryLogPath` — returns the user-writable `%LOCALAPPDATA%`
    path for surfacing in dialogs; computing it never throws (falls back to BaseDirectory, then
    to the bare file name).
  - Kept the existing public API (`LogError`, `LogInfo`, `SanitizeException`, `RedactSecrets`)
    and the never-throw guarantee and secret redaction.
- **`Mutation.Ui/App.xaml.cs`** — routed ALL exception logging through `ErrorLogger` (single
  source of truth):
  - `OnUnhandledException` / `OnAppDomainUnhandledException` / `OnUnobservedTaskException`: same
    control flow (`e.Handled = true` / `e.SetObserved()`), body now calls
    `ErrorLogger.LogError("<same source string>", ...)`.
  - `OnLaunched` catch: replaced the `File.WriteAllText(CrashLog_*.txt, SanitizeException(ex))`
    block with `ErrorLogger.LogError("Startup Error", ex);` and pointed the user-facing
    dialog/MessageBox at `ErrorLogger.PrimaryLogPath`. Dialog + MessageBox fallback +
    `ShutdownAsync()` behavior unchanged.
  - Removed the now-divergent private `LogBackgroundException`, `SanitizeException`, and
    `RedactSecrets` (logic lives in `ErrorLogger`). `TryRecoverSettings` does not use them.
  - Added a startup breadcrumb near the top of `OnLaunched`:
    `ErrorLogger.LogInfo("Startup", "OnLaunched starting");` — confirms the log path works on
    every launch.
- **`Mutation.Ui/Core/AudioSessionManager.cs`** `ProcessTranscriptAsync`: added
  `ErrorLogger.LogInfo("LLM", $"LLM processing starting (model={modelName}).")` right before the
  `ProcessWithLlmAsync` call, and `ErrorLogger.LogError("LLM processing failed", ex)` in the catch
  (existing StatusMessage line kept). `using CognitiveSupport;` was already present.
- **`Mutation.Ui/MainWindow.xaml.cs`**: added `ErrorLogger.LogError("Process with LLM", ex)` in
  both `ExecutePrompt`'s catch and `BtnProcessLlm_Click`'s catch, before the existing dialog calls.
  `using CognitiveSupport;` was already present.

### Crash-log locations & format
- Primary (user-writable, shown in dialogs): `%LOCALAPPDATA%\Mutation\logs\Mutation_Errors.log`
- Secondary (best-effort): `<EXE folder>\Mutation_Errors.log`
- Entry format (per write):
  `[yyyy-MM-dd HH:mm:ss.fff zzz] [Source]\n<redacted body>\n\n`
  Secrets (`sk-…`, `Bearer …`) are redacted before writing.

## Deliverable 2 — Cold-start LLM hardening + configurable retry knob

- **`CognitiveSupport/CognitiveSupport.csproj`**: added explicit
  `<PackageReference Include="Polly" Version="7.2.4" />` and
  `<PackageReference Include="Polly.Contrib.WaitAndRetry" Version="1.1.1" />` (previously only
  transitive via Deepgram).
- **`CognitiveSupport/LlmService.cs`** (OpenAI): added `int retryCount = 3` ctor param
  (stored as `_retryCount`, clamped `< 0` → 0). Wrapped `client.CompleteChatAsync(...)` in a
  Polly `WaitAndRetryAsync` mirroring the transcription pattern: handles `HttpRequestException`,
  `Polly.Timeout.TimeoutRejectedException`, `TaskCanceledException`;
  `Backoff.LinearBackoff(500ms, retryCount: _retryCount, factor: 1)`; per-attempt
  `CancellationTokenSource` timeout = `_timeoutSeconds * attempt` (escalating). With
  `_retryCount == 0` the body still executes once.
- **`CognitiveSupport/AnthropicLlmService.cs`**: same `retryCount` ctor param + Polly policy
  wrapping `_httpClient.SendAsync(...)` and the response read, with escalating per-attempt
  timeout. The `HttpRequestMessage` is rebuilt inside each attempt (it is single-use). Existing
  error parsing kept. NOTE comment added: a non-success status (incl. 4xx like 401) is surfaced
  as `HttpRequestException`, so it WILL be retried — this matches the existing transcription
  behavior; not retrying 4xx is a sensible follow-up.
- **`CognitiveSupport/Settings.cs`** — `LlmSettings`: added `public int RetryCount { get; set; } = 3;`
  next to `TimeoutSeconds`.
- **`Mutation.Ui/Views/Settings/SettingsDefaults.cs`** — `static class Llm`: added
  `public const int RetryCount = 3;`.
- **`Mutation.Ui/Services/SettingsManager.cs`** `EnsureSettings`: after the LLM section, added a
  guard that corrects hand-edited values — `RetryCount < 0` → 3, `RetryCount > 10` → 10, each
  setting `somethingWasMissing = true`. A clean object (field-init 3) is unchanged, so parity holds.
- **`Mutation.Ui/Views/Settings/SettingsWorkingCopy.cs`** `CommitInto`: added
  `dst.RetryCount = src.RetryCount;` in the LlmSettings block.
- **`Mutation.Ui/App.xaml.cs`** DI of `ILlmService`: reads
  `int retryCount = llmSettings?.RetryCount ?? SettingsDefaults.Llm.RetryCount;` then
  `if (retryCount < 0) retryCount = SettingsDefaults.Llm.RetryCount;`, and passes `retryCount`
  as the new last arg to both `new LlmService(...)` and `new AnthropicLlmService(...)`.

### Accessible UI knob (hard requirement — user is blind, uses ZoomText)
- **`Mutation.Ui/Resources/HelpText.xaml`**: added `Help.Llm.RetryCount` next to
  `Help.Llm.RequestTimeout` (no duplicate keys). Text: "How many times to retry a failed
  language-model request before giving up. Retries help the first request after a reboot succeed
  while the network warms up. Default 3."
- **`Mutation.Ui/Views/Settings/Pages/LlmSettingsPage.xaml`**: added a "Retries" block modeled
  exactly on the "Request timeout (seconds)" block — label + `InfoHint` (Help.Llm.RetryCount),
  a `NumberBox x:Name="NbRetryCount"` Minimum="0" Maximum="10" SmallChange="1"
  SpinButtonPlacementMode="Inline" with `AutomationProperties.Name="Retry count"` and
  `AutomationProperties.HelpText="{StaticResource Help.Llm.RetryCount}"`, plus a reset Button
  with `AutomationProperties.Name="Reset retry count"`. Placed right after the timeout block.
- **`Mutation.Ui/Views/Settings/Pages/LlmSettingsPage.xaml.cs`**: `LoadValues` sets
  `NbRetryCount.Value = llm.RetryCount >= 0 ? llm.RetryCount : SettingsDefaults.Llm.RetryCount;`;
  added `NbRetryCount_ValueChanged` (writes `RetryCount`, guarded by `_suppressEvents` + NaN
  check) and `BtnResetRetryCount_Click` (sets default), mirroring the timeout equivalents.

### Test
- **`Mutation.Tests/SettingsDefaultsParityTests.cs`**: added
  `[Fact] public void Llm_Defaults_Match()` asserting
  `Assert.Equal(SettingsDefaults.Llm.RetryCount, s.LlmSettings!.RetryCount)` after `ApplyDefaults()`.

## How to verify
- Build: `dotnet build Mutation.slnx -c Debug` → Build succeeded, 0 warnings, 0 errors.
- Test: `dotnet test Mutation.Tests/Mutation.Tests.csproj -c Debug` → Passed! 308/308, 0 failed.
- Runtime: launch the app; `%LOCALAPPDATA%\Mutation\logs\Mutation_Errors.log` should receive a
  `[Startup] OnLaunched starting` breadcrumb on every launch. The "Retries" NumberBox appears on
  the LLM settings page with full screen-reader name/help text.

## Resolved follow-ups
- **~~Anthropic retries 4xx (e.g. 401 Unauthorized).~~ Resolved.** Both LLM services now classify
  HTTP statuses via `LlmHttpStatus.IsTransient` (`CognitiveSupport/LlmHttpStatus.cs`): only
  connection failures, 408, 429 and 5xx are retried. Permanent 4xx (401/403/400/404/422) throw
  `NonTransientLlmException` (Anthropic) or are detected via `ClientResultException.Status` (OpenAI
  SDK), so a bad API key fails fast instead of retrying. Covered by
  `Mutation.Tests/LlmHttpStatusTests.cs`.

## Remaining follow-ups
- **Consider escalating the transcription-style timeout** consistently across all network paths,
  and surfacing per-attempt diagnostics to the log.
- The OpenAI SDK's `CompleteChatAsync` may apply its own internal retry/timeout; the added Polly
  layer is additive. Worth confirming the combined behavior is acceptable under sustained outage.
