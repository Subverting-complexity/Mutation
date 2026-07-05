namespace Mutation.Ui.Core;

/// <summary>
/// Tracks whether the microphone waveform has new samples to draw since the last
/// frame was rendered. The audio capture thread calls <see cref="MarkDataArrived"/>
/// as buffers arrive; the UI render tick calls <see cref="ConsumeShouldRender"/> to
/// decide whether to redraw. When nothing new has arrived, the plot, level meter, and
/// pulse overlay are already showing the last rendered state, so the tick can skip its
/// buffer copy, RMS scan, plot refresh, and meter writes entirely. This stops the
/// ~30 FPS idle churn that otherwise ran for the app's lifetime whenever no capture
/// was active (device not resolved, capture failed, or capture stopped).
/// </summary>
internal sealed class WaveformRenderGate
{
	private readonly object _gate = new();
	private bool _dataArrived;

	/// <summary>
	/// Marks that new samples have been captured since the last render. Safe to call
	/// from the capture thread; may be called many times between renders.
	/// </summary>
	public void MarkDataArrived()
	{
		lock (_gate)
			_dataArrived = true;
	}

	/// <summary>
	/// Returns whether a redraw is needed and clears the flag in one atomic step.
	/// True on the first call after one or more <see cref="MarkDataArrived"/> calls,
	/// then false until the next <see cref="MarkDataArrived"/>. Multiple arrivals
	/// between renders coalesce into a single true.
	/// </summary>
	public bool ConsumeShouldRender()
	{
		lock (_gate)
		{
			bool shouldRender = _dataArrived;
			_dataArrived = false;
			return shouldRender;
		}
	}
}
