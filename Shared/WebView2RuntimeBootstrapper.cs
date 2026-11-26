using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Web.WebView2.Core;

namespace RunMind.Shared;

/// <summary>
/// Ensures the Microsoft Evergreen WebView2 Runtime is available before creating a WebView.
/// Windows 11 normally includes it. On systems where it is missing, RunMind silently
/// installs it per-user with Microsoft's official bootstrapper.
/// </summary>
internal static class WebView2RuntimeBootstrapper
{
    private const string BootstrapperUrl = "https://go.microsoft.com/fwlink/p/?LinkId=2124703";
    private const long MaximumBootstrapperBytes = 20L * 1024 * 1024;
    private static readonly SemaphoreSlim ProcessGate = new(1, 1);
    private static readonly HttpClient Http = new(new HttpClientHandler
    {
        AllowAutoRedirect = true,
        AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
    })
    {
        Timeout = TimeSpan.FromMinutes(3),
    };

    /// <summary>
    /// Serializes the first WebView2 environment/controller initialization for a user-data
    /// folder across RunMind processes. WebView2 sessions are shared by controls using the
    /// same UDF; racing two fresh controller initializations against that UDF can disconnect
    /// the COM controller before WPF finishes attaching it.
    /// </summary>
    public static async Task<FileStream> AcquireUserDataFolderInitializationLockAsync(string userDataFolder)
    {
        if (string.IsNullOrWhiteSpace(userDataFolder))
            throw new ArgumentException("A WebView2 user-data folder is required.", nameof(userDataFolder));

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localAppData))
            throw new InvalidOperationException("Windows did not provide a Local AppData path for WebView2 initialization coordination.");

        var normalizedFolder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(userDataFolder));
        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalizedFolder.ToUpperInvariant()))).ToLowerInvariant();
        var lockFolder = Path.Combine(localAppData, "RunMind", "WebView2Locks");
        Directory.CreateDirectory(lockFolder);
        var lockPath = Path.Combine(lockFolder, key + ".lock");

        for (var attempt = 0; attempt < 600; attempt++)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None);
            }
            catch (IOException)
            {
                if (attempt == 599) break;
                await Task.Delay(200).ConfigureAwait(false);
            }
        }

        throw new TimeoutException("Another RunMind window did not finish initializing WebView2 with the same user-data folder within two minutes. Close the other window or retry.");
    }

    public static async Task EnsureInstalledAsync()
    {
        if (IsRuntimeAvailable()) return;

        await ProcessGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (IsRuntimeAvailable()) return;

            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrWhiteSpace(localAppData))
                throw new InvalidOperationException("Windows did not provide a Local AppData path for the WebView2 installer.");

            var installFolder = Path.Combine(localAppData, "RunMind", "Prerequisites", "WebView2");
            Directory.CreateDirectory(installFolder);
            var lockPath = Path.Combine(installFolder, "install.lock");
            await using var installLock = await AcquireInstallLockAsync(lockPath).ConfigureAwait(false);

            // A different RunMind process may have completed installation while this one waited.
            if (IsRuntimeAvailable()) return;

            var installerPath = Path.Combine(installFolder, "MicrosoftEdgeWebview2Setup.exe");
            if (!IsWindowsExecutable(installerPath))
                await DownloadBootstrapperAsync(installerPath).ConfigureAwait(false);

            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = installerPath,
                    Arguments = "/silent /install",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WorkingDirectory = installFolder,
                }) ?? throw new InvalidOperationException("Microsoft's WebView2 installer could not be started.");

                await process.WaitForExitAsync().ConfigureAwait(false);
                var installed = false;
                for (var attempt = 0; attempt < 20; attempt++)
                {
                    if (IsRuntimeAvailable())
                    {
                        installed = true;
                        break;
                    }
                    await Task.Delay(500).ConfigureAwait(false);
                }

                if (!installed)
                {
                    throw new InvalidOperationException(
                        "The Microsoft Edge WebView2 Runtime could not be installed. "
                        + $"The installer exited with code {process.ExitCode}. "
                        + "Check your internet connection and Windows installation permissions, then restart RunMind.");
                }
            }
            catch
            {
                // A broken/cancelled installer must not be reused forever on the next launch.
                if (!IsRuntimeAvailable()) TryDelete(installerPath);
                throw;
            }
        }
        finally
        {
            ProcessGate.Release();
        }
    }

    private static bool IsRuntimeAvailable()
    {
        try
        {
            return !string.IsNullOrWhiteSpace(CoreWebView2Environment.GetAvailableBrowserVersionString());
        }
        catch (WebView2RuntimeNotFoundException)
        {
            return false;
        }
        catch (FileNotFoundException)
        {
            return false;
        }
    }

    private static async Task<FileStream> AcquireInstallLockAsync(string lockPath)
    {
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                await Task.Delay(300).ConfigureAwait(false);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static bool IsWindowsExecutable(string path)
    {
        try
        {
            if (!File.Exists(path) || new FileInfo(path).Length < 64 * 1024) return false;
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            return file.ReadByte() == 'M' && file.ReadByte() == 'Z';
        }
        catch
        {
            return false;
        }
    }

    private static async Task DownloadBootstrapperAsync(string installerPath)
    {
        var temporaryPath = installerPath + ".download";
        try
        {
            using var response = await Http.GetAsync(BootstrapperUrl, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            var finalUri = response.RequestMessage?.RequestUri;
            if (finalUri is null
                || !string.Equals(finalUri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || !(string.Equals(finalUri.Host, "go.microsoft.com", StringComparison.OrdinalIgnoreCase)
                    || finalUri.Host.EndsWith(".microsoft.com", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Microsoft's WebView2 bootstrapper redirected to an unexpected download host.");
            if (response.Content.Headers.ContentLength is long contentLength && contentLength > MaximumBootstrapperBytes)
                throw new InvalidDataException("Microsoft's WebView2 bootstrapper exceeded the expected download size.");

            await using var source = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            await using var destination = new FileStream(temporaryPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long totalBytes = 0;
            while (true)
            {
                var read = await source.ReadAsync(buffer).ConfigureAwait(false);
                if (read == 0) break;
                totalBytes += read;
                if (totalBytes > MaximumBootstrapperBytes)
                    throw new InvalidDataException("Microsoft's WebView2 bootstrapper exceeded the expected download size.");
                await destination.WriteAsync(buffer.AsMemory(0, read)).ConfigureAwait(false);
            }

            await destination.FlushAsync().ConfigureAwait(false);
            if (totalBytes < 64 * 1024)
                throw new InvalidDataException("Microsoft's WebView2 bootstrapper download was incomplete.");

            await destination.DisposeAsync().ConfigureAwait(false);
            await using (var signatureCheck = new FileStream(temporaryPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var signature = new byte[2];
                if (await signatureCheck.ReadAsync(signature).ConfigureAwait(false) != 2
                    || signature[0] != (byte)'M'
                    || signature[1] != (byte)'Z')
                    throw new InvalidDataException("Microsoft's WebView2 bootstrapper download is not a valid Windows executable.");
            }

            File.Move(temporaryPath, installerPath, overwrite: true);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or TaskCanceledException)
        {
            throw new InvalidOperationException(
                "RunMind needs the Microsoft Edge WebView2 Runtime, which is not installed on this PC. "
                + "RunMind tried to download Microsoft's official installer automatically but could not complete the download. "
                + "Check your internet connection and try again.", ex);
        }
        finally
        {
            try { if (File.Exists(temporaryPath)) File.Delete(temporaryPath); } catch { }
        }
    }
}
