using System.Net;
using BacklinkStudio.Application;
using Microsoft.Extensions.Options;
using Microsoft.Playwright;

namespace BacklinkStudio.Infrastructure.Http;

public sealed class ControlledBrowserRuntime : IAsyncDisposable
{
    private readonly ControlledBrowserOptions _options;
    private readonly HashSet<string> _privateHosts;
    private readonly SemaphoreSlim _slots;
    private readonly SemaphoreSlim _startup = new(1, 1);
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public ControlledBrowserRuntime(
        IOptions<ControlledBrowserOptions> options,
        IOptions<SubmissionSourceHttpOptions> httpOptions)
    {
        _options = options.Value;
        _slots = new(_options.MaximumConcurrency, _options.MaximumConcurrency);
        _privateHosts = httpOptions.Value.AllowedPrivateHosts.Select(NormalizeHost).ToHashSet(StringComparer.Ordinal);
    }

    public async Task<T> UsePageAsync<T>(Uri source, string expectedHost, Func<IPage, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!_options.Enabled) throw new InvalidOperationException("Controlled browser automation is disabled.");
        ValidateExactHost(source, expectedHost);
        await ValidateAddressAsync(source.IdnHost, cancellationToken);
        await _slots.WaitAsync(cancellationToken);
        try
        {
            var browser = await BrowserAsync();
            await using var context = await browser.NewContextAsync(new()
            {
                AcceptDownloads = false,
                IgnoreHTTPSErrors = false,
                JavaScriptEnabled = true,
                ServiceWorkers = ServiceWorkerPolicy.Block
            });
            context.SetDefaultNavigationTimeout(_options.NavigationTimeoutMilliseconds);
            context.SetDefaultTimeout(_options.ActionTimeoutMilliseconds);
            var page = await context.NewPageAsync();
            await page.RouteAsync("**/*", async route =>
            {
                if (!Uri.TryCreate(route.Request.Url, UriKind.Absolute, out var requested) ||
                    (requested.Scheme != Uri.UriSchemeHttp && requested.Scheme != Uri.UriSchemeHttps) ||
                    !string.Equals(NormalizeHost(requested.IdnHost), NormalizeHost(expectedHost), StringComparison.Ordinal))
                {
                    await route.AbortAsync();
                    return;
                }
                await route.ContinueAsync();
            });
            await page.GotoAsync(source.AbsoluteUri, new()
            {
                WaitUntil = WaitUntilState.DOMContentLoaded,
                Timeout = _options.NavigationTimeoutMilliseconds
            });
            var final = new Uri(page.Url, UriKind.Absolute);
            ValidateExactHost(final, expectedHost);
            await ValidateAddressAsync(final.IdnHost, cancellationToken);
            return await action(page);
        }
        finally
        {
            _slots.Release();
        }
    }

    private async Task<IBrowser> BrowserAsync()
    {
        if (_browser is not null && _browser.IsConnected) return _browser;
        await _startup.WaitAsync();
        try
        {
            if (_browser is not null && _browser.IsConnected) return _browser;
            _playwright ??= await Playwright.CreateAsync();
            _browser = await _playwright.Chromium.LaunchAsync(new()
            {
                Headless = true,
                ChromiumSandbox = _options.ChromiumSandbox,
                ExecutablePath = string.IsNullOrWhiteSpace(_options.ChromiumExecutablePath) ? null : _options.ChromiumExecutablePath,
                Args = ["--disable-dev-shm-usage"]
            });
            return _browser;
        }
        finally
        {
            _startup.Release();
        }
    }

    private async Task ValidateAddressAsync(string host, CancellationToken cancellationToken)
    {
        var normalized = NormalizeHost(host);
        var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
        if (addresses.Length == 0 || (!_privateHosts.Contains(normalized) && addresses.Any(address => !SafeNetworkHandler.IsPublic(address))))
            throw new UnauthorizedAccessException("Controlled browser destination is not permitted by the network safety policy.");
    }

    private void ValidateExactHost(Uri uri, string expectedHost)
    {
        if ((uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) || !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.Equals(NormalizeHost(uri.IdnHost), NormalizeHost(expectedHost), StringComparison.Ordinal) ||
            (uri.Scheme == Uri.UriSchemeHttp && !_privateHosts.Contains(NormalizeHost(uri.IdnHost))))
            throw new UnauthorizedAccessException("Controlled browser navigation does not match the authorized exact host.");
    }

    private static string NormalizeHost(string value) =>
        new UriBuilder(Uri.UriSchemeHttps, value.Trim().TrimEnd('.')).Uri.IdnHost.ToLowerInvariant();

    public async ValueTask DisposeAsync()
    {
        if (_browser is not null) await _browser.DisposeAsync();
        _playwright?.Dispose();
        _startup.Dispose();
        _slots.Dispose();
    }
}
