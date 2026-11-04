#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;
using System.Windows;

namespace RunMind.Shared;

/// <summary>
/// Shared native Save As handling for the PowerToys and standalone WebView2 hosts.
/// </summary>
internal static class ConversationSaveDialog
{
    private const int MaximumConversationBytes = 50 * 1024 * 1024;

    public static bool TryHandleMessage(string source, string messageJson, Window owner, CoreWebView2 webView)
    {
        if (!IsChatPage(source) || string.IsNullOrWhiteSpace(messageJson))
            return false;

        if (Encoding.UTF8.GetByteCount(messageJson) > MaximumConversationBytes + (5 * 1024 * 1024))
            return false;

        JsonDocument messageDocument;
        try
        {
            messageDocument = JsonDocument.Parse(messageJson);
        }
        catch (JsonException)
        {
            return false;
        }

        using (messageDocument)
        {
            var root = messageDocument.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("type", out var type)
                || type.ValueKind != JsonValueKind.String
                || type.GetString() != "runmind.saveConversation")
                return false;

            var requestId = root.TryGetProperty("requestId", out var requestElement)
                && requestElement.ValueKind == JsonValueKind.String
                ? requestElement.GetString() ?? string.Empty
                : string.Empty;

            try
            {
                if (requestId.Length is 0 or > 100)
                    throw new InvalidDataException("The save request has an invalid request ID.");

                if (!root.TryGetProperty("conversation", out var conversation)
                    || conversation.ValueKind != JsonValueKind.Object
                    || !IsSupportedConversation(conversation))
                    throw new InvalidDataException("The conversation data is invalid or unsupported.");

                var conversationJson = conversation.GetRawText();
                if (Encoding.UTF8.GetByteCount(conversationJson) > MaximumConversationBytes)
                    throw new InvalidDataException("Conversation files must be 50 MB or smaller.");

                var suggestedName = root.TryGetProperty("fileName", out var fileNameElement)
                    && fileNameElement.ValueKind == JsonValueKind.String
                    ? Path.GetFileName(fileNameElement.GetString() ?? string.Empty)
                    : string.Empty;
                if (string.IsNullOrWhiteSpace(suggestedName))
                {
                    var title = conversation.TryGetProperty("title", out var titleElement)
                        && titleElement.ValueKind == JsonValueKind.String
                        ? titleElement.GetString() ?? string.Empty
                        : string.Empty;
                    suggestedName = MakeSafeFileName(title) + ".runmind";
                }
                else if (!suggestedName.EndsWith(".runmind", StringComparison.OrdinalIgnoreCase))
                {
                    suggestedName = Path.GetFileNameWithoutExtension(suggestedName) + ".runmind";
                }

                var dialog = new SaveFileDialog
                {
                    Title = "Save RunMind conversation",
                    Filter = "RunMind conversation (*.runmind)|*.runmind",
                    DefaultExt = ".runmind",
                    AddExtension = true,
                    CheckPathExists = true,
                    OverwritePrompt = true,
                    ValidateNames = true,
                    FileName = suggestedName,
                };

                if (dialog.ShowDialog(owner) != true)
                {
                    PostResult(webView, requestId, cancelled: true, success: false, fileName: null, error: null);
                    return true;
                }

                var targetPath = dialog.FileName;
                if (!targetPath.EndsWith(".runmind", StringComparison.OrdinalIgnoreCase))
                {
                    targetPath = Path.ChangeExtension(targetPath, ".runmind");
                    if (File.Exists(targetPath))
                        throw new IOException("A RunMind conversation with that filename already exists. Select the `.runmind` file directly to overwrite it.");
                }

                var savedTitle = Path.GetFileNameWithoutExtension(targetPath);
                if (string.IsNullOrWhiteSpace(savedTitle))
                    throw new InvalidDataException("The selected file name is not valid.");

                var savedDocument = JsonNode.Parse(conversationJson) as JsonObject
                    ?? throw new InvalidDataException("The conversation JSON could not be read.");
                savedDocument["title"] = savedTitle;
                savedDocument["updatedAt"] = DateTimeOffset.UtcNow.ToString("O");

                var serialized = savedDocument.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(targetPath, serialized, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

                PostResult(webView, requestId, cancelled: false, success: true, Path.GetFileName(targetPath), error: null);
            }
            catch (Exception ex)
            {
                PostResult(webView, requestId, cancelled: false, success: false, fileName: null, error: ex.Message);
            }

            return true;
        }
    }

    private static bool IsChatPage(string source)
    {
        return Uri.TryCreate(source, UriKind.Absolute, out var uri)
            && uri.Scheme == Uri.UriSchemeHttps
            && uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
            && uri.AbsolutePath.Equals("/ChatWindow.html", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsSupportedConversation(JsonElement conversation)
    {
        if (!conversation.TryGetProperty("format", out var format) || format.ValueKind != JsonValueKind.String)
            return false;
        if (!string.Equals(format.GetString(), "runmind", StringComparison.Ordinal))
            return false;
        if (!conversation.TryGetProperty("version", out var version)
            || !version.TryGetInt32(out var versionNumber)
            || versionNumber != 1)
            return false;
        return conversation.TryGetProperty("messages", out var messages)
            && messages.ValueKind == JsonValueKind.Array
            && messages.GetArrayLength() <= 10000;
    }

    private static string MakeSafeFileName(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars().ToHashSet();
        var safe = new string((value ?? string.Empty).Select(character =>
            invalidCharacters.Contains(character) || "<>:\"/\\|?*".Contains(character) || char.IsControl(character)
                ? '-'
                : character).ToArray()).Trim().TrimEnd('.');
        if (safe.Length > 80) safe = safe[..80].TrimEnd();
        return string.IsNullOrWhiteSpace(safe) ? "Conversation" : safe;
    }

    private static void PostResult(
        CoreWebView2 webView,
        string requestId,
        bool cancelled,
        bool success,
        string? fileName,
        string? error)
    {
        try
        {
            webView.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                type = "runmind.saveConversationResult",
                requestId,
                cancelled,
                success,
                fileName,
                error,
            }));
        }
        catch
        {
            // The page may be navigating or closing while the native dialog is dismissed.
        }
    }
}
