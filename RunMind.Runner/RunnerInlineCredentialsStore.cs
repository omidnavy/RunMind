#nullable enable

using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace RunMind.Runner;

/// <summary>Stores Runner-only inline provider credentials with Windows DPAPI CurrentUser scope (the machine-wide flag is never set).</summary>
internal static class RunnerInlineCredentialsStore
{
    private const uint CryptProtectUiForbidden = 0x1;
    private const string EntropyText = "RunMind.Runner.InlineCredentials.v1";
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static string FilePath => Path.Combine(RunnerInlineSettingsStore.DirectoryPath, "inline-credentials.dpapi");

    public static RunnerInlineCredentials Load()
    {
        lock (Sync)
        {
            try
            {
                if (!File.Exists(FilePath)) return RunnerInlineCredentials.Empty;
                var encrypted = File.ReadAllBytes(FilePath);
                if (encrypted.Length is 0 or > 64 * 1024) return RunnerInlineCredentials.Empty;
                var clear = Unprotect(encrypted);
                try
                {
                    var value = JsonSerializer.Deserialize<RunnerInlineCredentials>(clear, JsonOptions) ?? RunnerInlineCredentials.Empty;
                    return new RunnerInlineCredentials(
                        value.OllamaApiKey ?? string.Empty,
                        value.OllamaAuthHeader ?? string.Empty,
                        value.LMStudioApiKey ?? string.Empty,
                        value.LMStudioAuthHeader ?? string.Empty);
                }
                finally { CryptographicOperations.ZeroMemory(clear); }
            }
            catch { return RunnerInlineCredentials.Empty; }
        }
    }

    public static void Save(RunnerInlineCredentials credentials)
    {
        ArgumentNullException.ThrowIfNull(credentials);
        var clear = JsonSerializer.SerializeToUtf8Bytes(credentials, JsonOptions);
        byte[]? encrypted = null;
        lock (Sync)
        {
            try
            {
                Directory.CreateDirectory(RunnerInlineSettingsStore.DirectoryPath);
                encrypted = Protect(clear);
                var temporaryPath = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.WriteAllBytes(temporaryPath, encrypted);
                    File.Move(temporaryPath, FilePath, true);
                    try { File.SetAttributes(FilePath, FileAttributes.Hidden); } catch { }
                }
                finally { try { File.Delete(temporaryPath); } catch { } }
            }
            finally
            {
                CryptographicOperations.ZeroMemory(clear);
                if (encrypted is not null) CryptographicOperations.ZeroMemory(encrypted);
            }
        }
    }

    private static byte[] Protect(byte[] clear)
    {
        var input = ToBlob(clear);
        var entropy = ToBlob(Encoding.UTF8.GetBytes(EntropyText));
        try
        {
            if (!CryptProtectData(ref input, "RunMind Runner inline credentials", ref entropy, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
                throw new CryptographicException(Marshal.GetLastWin32Error());
            return FromBlob(output);
        }
        finally
        {
            FreeBlob(input);
            FreeBlob(entropy);
        }
    }

    private static byte[] Unprotect(byte[] encrypted)
    {
        var input = ToBlob(encrypted);
        var entropy = ToBlob(Encoding.UTF8.GetBytes(EntropyText));
        try
        {
            if (!CryptUnprotectData(ref input, IntPtr.Zero, ref entropy, IntPtr.Zero, IntPtr.Zero,
                    CryptProtectUiForbidden, out var output))
                throw new CryptographicException(Marshal.GetLastWin32Error());
            return FromBlob(output);
        }
        finally
        {
            FreeBlob(input);
            FreeBlob(entropy);
        }
    }

    private static DataBlob ToBlob(byte[] bytes)
    {
        var pointer = Marshal.AllocHGlobal(bytes.Length);
        Marshal.Copy(bytes, 0, pointer, bytes.Length);
        return new DataBlob { Length = bytes.Length, Data = pointer };
    }

    private static byte[] FromBlob(DataBlob blob)
    {
        try
        {
            if (blob.Length <= 0 || blob.Length > 64 * 1024)
                throw new InvalidDataException("The DPAPI credential payload has an invalid size.");
            var bytes = new byte[blob.Length];
            Marshal.Copy(blob.Data, bytes, 0, blob.Length);
            return bytes;
        }
        finally { if (blob.Data != IntPtr.Zero) LocalFree(blob.Data); }
    }

    private static void FreeBlob(DataBlob blob)
    {
        if (blob.Data == IntPtr.Zero) return;
        var zero = new byte[Math.Max(0, blob.Length)];
        if (zero.Length > 0) Marshal.Copy(zero, 0, blob.Data, zero.Length);
        Marshal.FreeHGlobal(blob.Data);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DataBlob
    {
        public int Length;
        public IntPtr Data;
    }

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref DataBlob input, string? description, ref DataBlob optionalEntropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("crypt32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref DataBlob input, IntPtr description, ref DataBlob optionalEntropy,
        IntPtr reserved, IntPtr prompt, uint flags, out DataBlob output);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr LocalFree(IntPtr memory);
}
