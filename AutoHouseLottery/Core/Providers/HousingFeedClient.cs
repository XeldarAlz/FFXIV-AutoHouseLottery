using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using AutoHouseLottery.Core.Providers.Model;

namespace AutoHouseLottery.Core.Providers;

// Read-only GETs against the housing service: at most two in flight, a hard timeout and a response size limit, and the
// proxy's age headers carried into the receipt. Ported from Aetherphone's HTTP service, without its retries: a failed
// request is reported once and the cadence decides when to try again.
internal sealed class HousingFeedClient : IDisposable
{
    public const int MaximumConcurrentRequests = 2;
    public const long MaximumResponseBytes = 8L * 1024 * 1024;

    private const int TimeoutSeconds = 20;
    private const string UpstreamFetchedHeader = "X-Source-Fetched-At";
    private const string ProxyAgeHeader = "X-Cache-Age-Seconds";
    private const string ProductName = "AutoHouseLottery";

    private static readonly TimeSpan requestTimeout = TimeSpan.FromSeconds(TimeoutSeconds);

    private readonly HttpClient client;
    private readonly SemaphoreSlim gate = new(MaximumConcurrentRequests, MaximumConcurrentRequests);

    public HousingFeedClient(string version)
    {
        var handler = new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            PooledConnectionLifetime = TimeSpan.FromMinutes(10),
        };
        client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(ProductName, version));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<FeedFetch> GetAsync(string url, CancellationToken token)
    {
        await gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            return await SendAsync(url, token).ConfigureAwait(false);
        }
        finally
        {
            gate.Release();
        }
    }

    public void Dispose()
    {
        client.Dispose();
        gate.Dispose();
    }

    private async Task<FeedFetch> SendAsync(string url, CancellationToken token)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(requestTimeout);
        try
        {
            using var response = await client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            var received = DateTime.UtcNow;
            var receipt = FeedReceipt.From(received, UpstreamFetched(response), ProxyAge(response));
            var status = (int)response.StatusCode;
            if (response.StatusCode == HttpStatusCode.TooManyRequests)
            {
                return new FeedFetch(FetchStatus.RateLimited, null, status, RetryAfter(response, received), null, receipt);
            }

            if (!response.IsSuccessStatusCode)
            {
                return new FeedFetch(FetchStatus.HttpError, null, status, RetryAfter(response, received), null, receipt);
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            var body = await BoundedBody.ReadAsync(stream, response.Content.Headers.ContentLength, MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
            return body is null
                ? new FeedFetch(FetchStatus.Oversized, null, status, null, null, receipt)
                : new FeedFetch(FetchStatus.Success, body, status, null, response.Headers.CacheControl?.MaxAge, receipt);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            return Failed(FetchStatus.Cancelled);
        }
        catch (OperationCanceledException)
        {
            return Failed(FetchStatus.TimedOut);
        }
        catch (HttpRequestException)
        {
            return Failed(FetchStatus.Unreachable);
        }
        catch (IOException)
        {
            return Failed(FetchStatus.Unreachable);
        }
    }

    private static FeedFetch Failed(FetchStatus status)
    {
        var now = DateTime.UtcNow;
        return new FeedFetch(status, null, 0, null, null, new FeedReceipt(now, now));
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response, DateTime receivedUtc)
    {
        var retryAfter = response.Headers.RetryAfter;
        if (retryAfter?.Delta is { } delta)
        {
            return delta;
        }

        return retryAfter?.Date is { } date ? date.UtcDateTime - receivedUtc : null;
    }

    private static DateTimeOffset? UpstreamFetched(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(UpstreamFetchedHeader, out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var fetched))
            {
                return fetched;
            }
        }

        return null;
    }

    private static int? ProxyAge(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues(ProxyAgeHeader, out var values))
        {
            return null;
        }

        foreach (var value in values)
        {
            if (int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds))
            {
                return seconds;
            }
        }

        return null;
    }
}
