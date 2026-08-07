namespace Mutation.Ui.Services;

/// <summary>
/// The two Windows calls that global hotkey registration actually needs, behind a seam.
/// <para>
/// Registration bookkeeping — which shortcuts are taken, which id belongs to which
/// callback, and what a refresh has to release — is the part that leaks or mis-routes
/// chords, and it cannot be exercised against the real API because that needs a window
/// handle and a machine where the chords are free. Everything above this interface is
/// therefore testable; everything below it is a one-line P/Invoke.
/// </para>
/// </summary>
internal interface IHotkeyPlatform
{
	/// <summary>
	/// Asks Windows to bind <paramref name="virtualKey"/> plus <paramref name="modifiers"/>
	/// to <paramref name="id"/>.
	/// </summary>
	/// <param name="errorCode">
	/// The Win32 error code when the call failed, or 0 when Windows gave no reason.
	/// Meaningless when the call succeeded.
	/// </param>
	bool Register(int id, uint modifiers, uint virtualKey, out int errorCode);

	/// <summary>Releases a previously registered id. Silent about ids Windows does not know.</summary>
	void Unregister(int id);
}
