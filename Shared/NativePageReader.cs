#nullable enable

using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;

namespace RunMind.Shared;

/// <summary>
/// Bounded, public-web-only HTTP reader exposed to the local RunMind page through
/// a narrowly scoped WebView2 message. No host objects or general-purpose proxy
/// are exposed to JavaScript.
/// </summary>
public sealed class NativePageReader : IDisposable
{
    private const int MaximumMessageBytes = 16 * 1024;
    private const int MaximumResponseBytes = 1536 * 1024;
    private const int MaximumRedirects = 5;
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(25);

    private static readonly HttpClient HttpClient = CreateHttpClient();
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _requests = new(StringComparer.Ordinal);
    private int _disposed;

    public bool TryHandleMessage(
        string? source,
        string? messageJson,
        CoreWebView2 core,
        Dispatcher dispatcher)
    {
        if (Volatile.Read(ref _disposed) != 0 || !IsTrustedSource(source) || string.IsNullOrWhiteSpace(messageJson))
            return false;

        if (Encoding.UTF8.GetByteCount(messageJson) > MaximumMessageBytes)
            return false;

        try
        {
            using var document = JsonDocument.Parse(messageJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
                return false;

            var type = typeElement.GetString();
            if (type == "runmind.nativeReaderCancel")
            {
                if (root.TryGetProperty("requestId", out var cancelId)
                    && cancelId.ValueKind == JsonValueKind.String
                    && _requests.TryGetValue(cancelId.GetString() ?? string.Empty, out var pending))
                {
                    try { pending.Cancel(); } catch (ObjectDisposedException) { }
                }
                return true;
            }

            if (type != "runmind.nativeReaderRequest")
                return false;

            var requestId = root.TryGetProperty("requestId", out var idElement) && idElement.ValueKind == JsonValueKind.String
                ? idElement.GetString() ?? string.Empty
                : string.Empty;
            var url = root.TryGetProperty("url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String
                ? urlElement.GetString() ?? string.Empty
                : string.Empty;

            if (requestId.Length is < 1 or > 100 || url.Length is < 1 or > 4096)
            {
                _ = PostResultAsync(core, dispatcher, new
                {
                    type = "runmind.nativeReaderResult",
                    requestId,
                    ok = false,
                    error = "The native Reader request was malformed or exceeded its input limit.",
                });
                return true;
            }

            var cancellation = new CancellationTokenSource();
            if (Volatile.Read(ref _disposed) != 0)
            {
                cancellation.Dispose();
                return true;
            }
            if (!_requests.TryAdd(requestId, cancellation))
            {
                cancellation.Dispose();
                _ = PostResultAsync(core, dispatcher, new
                {
                    type = "runmind.nativeReaderResult",
                    requestId,
                    ok = false,
                    error = "A Reader request with the same identifier is already running.",
                });
                return true;
            }
            if (Volatile.Read(ref _disposed) != 0)
            {
                try { cancellation.Cancel(); } catch (ObjectDisposedException) { }
                return true;
            }

            _ = ProcessRequestAsync(requestId, url, core, dispatcher, cancellation);
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private async Task ProcessRequestAsync(
        string requestId,
        string url,
        CoreWebView2 core,
        Dispatcher dispatcher,
        CancellationTokenSource cancellation)
    {
        try
        {
            var page = await FetchAsync(url, cancellation.Token).ConfigureAwait(false);
            await PostResultAsync(core, dispatcher, new
            {
                type = "runmind.nativeReaderResult",
                requestId,
                ok = true,
                url = page.Url,
                contentType = page.ContentType,
                content = page.Content,
                status = page.StatusCode,
            }).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await PostResultAsync(core, dispatcher, new
            {
                type = "runmind.nativeReaderResult",
                requestId,
                ok = false,
                error = "The native Reader request was cancelled or exceeded its 25-second limit.",
            }).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            await PostResultAsync(core, dispatcher, new
            {
                type = "runmind.nativeReaderResult",
                requestId,
                ok = false,
                code = ContainsDestinationBlock(ex) ? "NATIVE_DESTINATION_BLOCKED" : "NATIVE_READER_FAILED",
                error = ex.Message,
            }).ConfigureAwait(false);
        }
        finally
        {
            _requests.TryRemove(requestId, out _);
            cancellation.Dispose();
        }
    }

    private static async Task<NativePageResult> FetchAsync(string rawUrl, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(RequestTimeout);

        if (!Uri.TryCreate(rawUrl.Trim(), UriKind.Absolute, out var current))
            throw new InvalidOperationException("The Native Reader requires an absolute URL.");

        ValidateTarget(current);

        for (var redirectCount = 0; ; redirectCount++)
        {
            timeout.Token.ThrowIfCancellationRequested();
            await EnsurePublicDestinationAsync(current, timeout.Token).ConfigureAwait(false);

            using var request = new HttpRequestMessage(HttpMethod.Get, current);
            request.Headers.UserAgent.ParseAdd("RunMind-Native-Reader/1.10.32");
            request.Headers.Accept.ParseAdd("text/html,application/xhtml+xml,application/json,text/plain,application/xml,text/xml;q=0.9,*/*;q=0.5");
            request.Headers.AcceptLanguage.ParseAdd("en-US,en;q=0.8");

            using var response = await HttpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token).ConfigureAwait(false);

            if (IsRedirect(response.StatusCode))
            {
                if (redirectCount >= MaximumRedirects)
                    throw new HttpRequestException($"The page exceeded the {MaximumRedirects}-redirect limit.");

                var location = response.Headers.Location;
                if (location is null)
                    throw new HttpRequestException($"The page returned HTTP {(int)response.StatusCode} without a redirect location.");

                var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                ValidateTarget(next);
                if (current.Scheme == Uri.UriSchemeHttps && next.Scheme == Uri.UriSchemeHttp)
                    throw new HttpRequestException("The page attempted to downgrade an HTTPS request to HTTP.");
                current = next;
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException($"The page returned HTTP {(int)response.StatusCode} ({response.ReasonPhrase}).");

            var body = await ReadBoundedBytesAsync(response.Content, MaximumResponseBytes, timeout.Token).ConfigureAwait(false);
            var contentType = response.Content.Headers.ContentType?.ToString() ?? "text/html";
            var charset = response.Content.Headers.ContentType?.CharSet;
            var content = DecodeBody(body, charset);

            if (string.IsNullOrWhiteSpace(content))
                throw new InvalidOperationException("The page returned an empty response body.");

            return new NativePageResult(current.AbsoluteUri, contentType, content, (int)response.StatusCode);
        }
    }

    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate | DecompressionMethods.Brotli,
            UseCookies = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(10),
            PooledConnectionLifetime = TimeSpan.FromMinutes(2),
            ConnectCallback = ConnectPublicAddressAsync,
        };
        return new HttpClient(handler, disposeHandler: true)
        {
            Timeout = Timeout.InfiniteTimeSpan,
        };
    }

    private static async ValueTask<Stream> ConnectPublicAddressAsync(
        SocketsHttpConnectionContext context,
        CancellationToken cancellationToken)
    {
        IPAddress[] addresses;
        if (IPAddress.TryParse(context.DnsEndPoint.Host, out var literal))
            addresses = new[] { literal };
        else
            addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, cancellationToken).ConfigureAwait(false);

        // DNS may return a mixture of globally routable and non-public records
        // (for example, because of split-horizon DNS, DNS filtering, or NAT64).
        // Never connect to a non-public answer, but don't reject a public hostname
        // merely because one additional A/AAAA answer is non-public.
        // A DNS outage or empty answer is a retrieval failure, not an SSRF
        // destination block. The JavaScript Reader pipeline must be allowed to
        // try the configured custom gateway and then Jina in that case.
        if (addresses.Length == 0)
            throw new HttpRequestException($"DNS resolution for '{context.DnsEndPoint.Host}' returned no IP addresses.");

        // Never connect to private, loopback, link-local, or reserved DNS answers.
        // For a syntactically public hostname, however, a DNS response containing
        // only non-public addresses is a Native retrieval failure rather than an
        // input-URL security block. This lets the application try its configured
        // Custom Reader and then Jina, which resolve the original hostname from
        // their own network. Private IP literals and local/special-use hostnames
        // are still rejected by ValidateTarget before this callback is reached.
        addresses = addresses.Where(IsPublicAddress).Distinct().ToArray();
        if (addresses.Length == 0)
            throw new HttpRequestException($"The Native Reader could not safely connect to '{context.DnsEndPoint.Host}' because DNS returned only non-public IP addresses. No connection was made; the configured remote Reader fallback may retry the original public hostname.");

        Exception? lastError = null;
        foreach (var address in addresses)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp)
            {
                NoDelay = true,
            };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), cancellationToken).ConfigureAwait(false);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                socket.Dispose();
                if (ex is OperationCanceledException) throw;
                lastError = ex;
            }
        }

        throw new HttpRequestException("The Native Reader could not connect to the public destination.", lastError);
    }

    private static async Task EnsurePublicDestinationAsync(Uri uri, CancellationToken cancellationToken)
    {
        ValidateTarget(uri);
        IPAddress[] addresses;
        if (IPAddress.TryParse(uri.DnsSafeHost, out var literal))
            addresses = new[] { literal };
        else
            addresses = await Dns.GetHostAddressesAsync(uri.DnsSafeHost, cancellationToken).ConfigureAwait(false);

        // An empty answer (or a DNS exception above) is an ordinary resolution
        // failure. Return it as NATIVE_READER_FAILED so the remote Reader chain
        // can run. A non-empty answer containing only non-public addresses is a
        // genuine destination block and must not be sent to a remote Reader.
        if (addresses.Length == 0)
            throw new HttpRequestException($"DNS resolution for '{uri.DnsSafeHost}' returned no IP addresses.");

        // A mixed DNS response is acceptable only when at least one public
        // address exists. ConnectPublicAddressAsync independently filters the
        // actual socket candidates, so private answers are never connected to.
        // If a public hostname resolves only to non-public answers, fail this
        // local attempt normally so the JavaScript orchestrator can continue to
        // the configured remote Readers. Literal private IPs and local/special
        // hostnames have already been rejected by ValidateTarget above.
        if (!addresses.Any(IsPublicAddress))
            throw new HttpRequestException($"The Native Reader could not safely retrieve '{uri.DnsSafeHost}' because DNS returned only non-public IP addresses. No connection was made; the configured remote Reader fallback may retry the original public hostname.");
    }

    private static void ValidateTarget(Uri uri)
    {
        if (!uri.IsAbsoluteUri || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            throw new InvalidOperationException("Only absolute HTTP and HTTPS URLs are supported by the Native Reader.");
        if (!string.IsNullOrEmpty(uri.UserInfo))
            throw new InvalidOperationException("URLs containing embedded username or password credentials are not supported.");

        var host = uri.DnsSafeHost.TrimEnd('.');
        if (host.Length == 0)
            throw new NativeReaderBlockedException("The Native Reader only retrieves public website hostnames; local or special-use names are blocked.");
        if (IPAddress.TryParse(host, out var literalAddress))
        {
            if (!IsPublicAddress(literalAddress))
                throw new NativeReaderBlockedException("The Native Reader only retrieves public website addresses; local, private, link-local, and reserved destinations are blocked.");
            return;
        }
        if (!host.Contains('.')
            || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".local", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".internal", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".lan", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".home", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".home.arpa", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".corp", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".test", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".example", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".invalid", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".onion", StringComparison.OrdinalIgnoreCase))
            throw new NativeReaderBlockedException("The Native Reader only retrieves public website hostnames; local or special-use names are blocked.");
    }

    private static bool IsPublicAddress(IPAddress address)
    {
        if (IPAddress.IsLoopback(address)) return false;
        if (address.IsIPv4MappedToIPv6) return IsPublicAddress(address.MapToIPv4());

        var bytes = address.GetAddressBytes();
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var a = bytes[0];
            var b = bytes[1];
            var c = bytes[2];
            if (a == 0 || a == 10 || a == 127 || a >= 224) return false;
            if (a == 100 && b >= 64 && b <= 127) return false;              // Shared address space.
            if (a == 169 && b == 254) return false;                         // Link-local.
            if (a == 172 && b >= 16 && b <= 31) return false;               // Private.
            if (a == 192 && b == 168) return false;                         // RFC 1918 private.
            if (a == 192 && b == 0 && c == 0) return false;                 // IETF protocol assignments (192.0.0.0/24).
            if (a == 192 && b == 88 && c == 99) return false;               // Deprecated 6to4 relay.
            if (a == 192 && b == 0 && c == 2) return false;                 // TEST-NET-1.
            if (a == 198 && (b == 18 || b == 19 || (b == 51 && c == 100))) return false;
            if (a == 203 && b == 0 && c == 113) return false;               // TEST-NET-3.
            return true;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.Equals(IPAddress.IPv6Any)
                || address.Equals(IPAddress.IPv6None)
                || address.IsIPv6LinkLocal
                || address.IsIPv6SiteLocal
                || address.IsIPv6Multicast)
                return false;
            if ((bytes[0] & 0xFE) == 0xFC) return false;                    // Unique-local fc00::/7.
            if ((bytes[0] & 0xE0) != 0x20) return false;                    // Only global-unicast 2000::/3.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] == 0x0D && bytes[3] == 0xB8)
                return false;                                               // Documentation 2001:db8::/32.
            if (bytes[0] == 0x20 && bytes[1] == 0x01 && bytes[2] <= 0x01)
                return false;                                               // IETF protocol assignments in 2001::/23.
            if (bytes[0] == 0x20 && bytes[1] == 0x02)
                return false;                                               // Deprecated 6to4 2002::/16.
            if (bytes[0] == 0x3F && bytes[1] == 0xFF && (bytes[2] & 0xF0) == 0x00)
                return false;                                               // Documentation 3fff::/20.
            return true;
        }

        return false;
    }

    private static bool ContainsDestinationBlock(Exception exception)
    {
        for (Exception? current = exception; current is not null; current = current.InnerException)
        {
            if (current is NativeReaderBlockedException) return true;
        }
        return false;
    }

    private static bool IsRedirect(HttpStatusCode statusCode) =>
        statusCode is HttpStatusCode.MovedPermanently
            or HttpStatusCode.Redirect
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    private static async Task<byte[]> ReadBoundedBytesAsync(HttpContent content, int maximumBytes, CancellationToken cancellationToken)
    {
        if (content.Headers.ContentLength is long length && length > maximumBytes)
            throw new InvalidOperationException($"The page exceeded the Native Reader's {maximumBytes / 1024} KB response limit.");

        await using var input = await content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16 * 1024];
        while (true)
        {
            var read = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), cancellationToken).ConfigureAwait(false);
            if (read == 0) break;
            if (output.Length + read > maximumBytes)
                throw new InvalidOperationException($"The page exceeded the Native Reader's {maximumBytes / 1024} KB response limit.");
            output.Write(buffer, 0, read);
        }
        return output.ToArray();
    }

    private static string DecodeBody(byte[] bytes, string? charset)
    {
        Encoding encoding = Encoding.UTF8;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            try { encoding = Encoding.GetEncoding(charset.Trim().Trim('"', '\'')); }
            catch (ArgumentException) { /* Fall back to UTF-8 for unknown charset labels. */ }
        }

        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new StreamReader(stream, encoding, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }

    private static bool IsTrustedSource(string? source) =>
        Uri.TryCreate(source, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps
        && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
        && uri.AbsolutePath.Equals("/ChatWindow.html", StringComparison.OrdinalIgnoreCase);

    private static async Task PostResultAsync(CoreWebView2 core, Dispatcher dispatcher, object result)
    {
        if (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished) return;
        var json = JsonSerializer.Serialize(result);
        try
        {
            await dispatcher.InvokeAsync(() =>
            {
                if (!dispatcher.HasShutdownStarted && !dispatcher.HasShutdownFinished)
                    core.PostWebMessageAsJson(json);
            });
        }
        catch (Exception)
        {
            // The window/WebView can close while a native read is completing.
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        foreach (var request in _requests.Values)
        {
            try { request.Cancel(); } catch (ObjectDisposedException) { }
        }
    }

    private sealed class NativeReaderBlockedException : InvalidOperationException
    {
        public NativeReaderBlockedException(string message) : base(message) { }
    }

    private sealed record NativePageResult(string Url, string ContentType, string Content, int StatusCode);
}
