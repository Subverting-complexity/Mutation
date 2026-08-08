using System.Net;
using System.Net.Http;
using System.Text;

namespace Mutation.Tests;

/// <summary>
/// Captures every outgoing request and replies from a scripted queue, so tests can
/// assert on the exact body and headers a service sends without touching the network.
/// Replies are consumed in order; the last one is reused once the queue runs dry, which
/// keeps a single-response setup working for a service that retries.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
	private readonly Queue<(HttpStatusCode Status, string Body, string? RetryAfter)> _replies = new();
	private (HttpStatusCode Status, string Body, string? RetryAfter) _lastReply = (HttpStatusCode.OK, "{}", null);

	/// <summary>Every request sent, in order, with its body already read.</summary>
	internal List<CapturedRequest> Requests { get; } = new();

	/// <param name="retryAfter">
	/// The <c>Retry-After</c> header to answer with, when the reply is one that carries one.
	/// Null sends no such header, which is the ordinary case.
	/// </param>
	internal FakeHttpMessageHandler Respond(HttpStatusCode status, string body, string? retryAfter = null)
	{
		_replies.Enqueue((status, body, retryAfter));
		return this;
	}

	protected override async Task<HttpResponseMessage> SendAsync(
		HttpRequestMessage request,
		CancellationToken cancellationToken)
	{
		string body = request.Content is null
			? string.Empty
			: await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

		// Header values must be copied out now: HttpRequestMessage is disposed by the
		// caller as soon as the response is read.
		var headers = request.Headers
			.ToDictionary(h => h.Key, h => string.Join(",", h.Value), StringComparer.OrdinalIgnoreCase);

		Requests.Add(new CapturedRequest(body, headers));

		if (_replies.Count > 0)
			_lastReply = _replies.Dequeue();

		var response = new HttpResponseMessage(_lastReply.Status)
		{
			Content = new StringContent(_lastReply.Body, Encoding.UTF8, "application/json")
		};
		if (_lastReply.RetryAfter is not null)
		{
			// Without validation, so a deliberately malformed value can be sent to see what
			// the reader makes of it.
			response.Headers.TryAddWithoutValidation("Retry-After", _lastReply.RetryAfter);
		}
		return response;
	}

	internal sealed record CapturedRequest(string Body, IReadOnlyDictionary<string, string> Headers)
	{
		internal bool HasHeader(string name) => Headers.ContainsKey(name);

		internal string? Header(string name) => Headers.TryGetValue(name, out var value) ? value : null;
	}
}
