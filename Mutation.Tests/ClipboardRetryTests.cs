using System;
using System.Threading.Tasks;
using Mutation.Ui.Services;
using Xunit;

namespace Mutation.Tests;

public class ClipboardRetryTests
{
	[Fact]
	public async Task TryAsync_ReturnsTrue_WhenOperationSucceedsFirstTry()
	{
		int calls = 0;
		bool result = await ClipboardRetry.TryAsync(() => { calls++; return Task.CompletedTask; });

		Assert.True(result);
		Assert.Equal(1, calls);
	}

	[Fact]
	public async Task TryAsync_RetriesAndSucceeds_AfterTransientFailures()
	{
		int calls = 0;
		bool result = await ClipboardRetry.TryAsync(() =>
		{
			calls++;
			if (calls < 3)
				throw new InvalidOperationException("clipboard busy");
			return Task.CompletedTask;
		}, attempts: 5, delayMs: 1);

		Assert.True(result);
		Assert.Equal(3, calls);
	}

	[Fact]
	public async Task TryAsync_ReturnsFalse_WhenEveryAttemptFails()
	{
		int calls = 0;
		bool result = await ClipboardRetry.TryAsync(() =>
		{
			calls++;
			throw new InvalidOperationException("clipboard busy");
		}, attempts: 3, delayMs: 1);

		Assert.False(result);
		Assert.Equal(3, calls);
	}

	[Fact]
	public async Task TryAsync_Throws_OnNullOperation()
	{
		await Assert.ThrowsAsync<ArgumentNullException>(() => ClipboardRetry.TryAsync(null!));
	}

	[Fact]
	public async Task TryAsync_Throws_OnNonPositiveAttempts()
	{
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
			() => ClipboardRetry.TryAsync(() => Task.CompletedTask, attempts: 0));
	}

	[Fact]
	public async Task TryAsyncOfT_ReturnsValue_WhenOperationSucceedsFirstTry()
	{
		var (success, value) = await ClipboardRetry.TryAsync(() => Task.FromResult(42));

		Assert.True(success);
		Assert.Equal(42, value);
	}

	[Fact]
	public async Task TryAsyncOfT_RetriesAndReturnsValue_AfterTransientFailures()
	{
		int calls = 0;
		var (success, value) = await ClipboardRetry.TryAsync(() =>
		{
			calls++;
			if (calls < 3)
				throw new InvalidOperationException("clipboard busy");
			return Task.FromResult("text");
		}, attempts: 5, delayMs: 1);

		Assert.True(success);
		Assert.Equal("text", value);
		Assert.Equal(3, calls);
	}

	[Fact]
	public async Task TryAsyncOfT_ReturnsDefault_WhenEveryAttemptFails()
	{
		int calls = 0;
		var (success, value) = await ClipboardRetry.TryAsync<string>(() =>
		{
			calls++;
			throw new InvalidOperationException("clipboard busy");
		}, attempts: 3, delayMs: 1);

		Assert.False(success);
		Assert.Null(value);
		Assert.Equal(3, calls);
	}

	[Fact]
	public async Task TryAsyncOfT_Throws_OnNullOperation()
	{
		await Assert.ThrowsAsync<ArgumentNullException>(
			() => ClipboardRetry.TryAsync<int>(null!));
	}

	[Fact]
	public async Task TryAsyncOfT_Throws_OnNonPositiveAttempts()
	{
		await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
			() => ClipboardRetry.TryAsync(() => Task.FromResult(1), attempts: 0));
	}
}
