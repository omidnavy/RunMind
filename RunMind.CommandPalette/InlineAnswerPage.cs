using System.IO;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.CommandPalette.Extensions;
using Microsoft.CommandPalette.Extensions.Toolkit;
using RunMind.Shared;

namespace RunMind.CommandPalette;

/// <summary>
/// The Command Palette fallback page. It receives the search query, generates
/// directly against the configured local provider, streams the answer into the
/// palette, and accepts follow-up questions without launching another window.
/// </summary>
internal sealed partial class InlineAnswerPage : ContentPage, IFallbackHandler
{
    private readonly object _sync = new();
    private InlineConversationForm _conversationForm;
    private readonly List<InlineConversationTurn> _history = [];
    private CancellationTokenSource? _activeRequest;
    private long _generation;
    private string _sourceQuery = string.Empty;
    private string _pendingPrompt = string.Empty;
    private bool _initialGenerationPending;
    private string _currentPrompt = string.Empty;
    private string _currentAnswer = string.Empty;
    private string _status = "Type RunMind followed by a question.";
    private string _provider = string.Empty;
    private string _providerId = string.Empty;
    private string _model = string.Empty;
    private long _currentTimestampMilliseconds;
    private bool _isError;
    private string _notice = string.Empty;
    private long _lastUiUpdate;

    public InlineAnswerPage()
    {
        Name = "RunMind";
        Title = "RunMind";
        Icon = IconHelpers.FromRelativePath("Assets\\StoreLogo.png");
        _conversationForm = new InlineConversationForm(this);
    }

    public void UpdateQuery(string query)
    {
        query ??= string.Empty;
        var prompt = ExtractPrompt(query);
        RunMindCommandPaletteDiagnostics.Write(
            $"Inline fallback received query (length={query.Length}); extracted prompt length={prompt.Length}.");
        lock (_sync)
        {
            if (string.Equals(_sourceQuery, query, StringComparison.Ordinal)
                && string.Equals(_pendingPrompt, prompt, StringComparison.Ordinal))
                return;

            _sourceQuery = query;
            _pendingPrompt = prompt;
            _initialGenerationPending = true;
        }

        // IFallbackHandler.UpdateQuery runs while the user types in the root
        // palette. It only records the pending prompt; model inference begins in
        // GetContent after the user selects the RunMind fallback.
    }

    public override IContent[] GetContent()
    {
        bool startInitialGeneration;
        string initialPrompt;
        lock (_sync)
        {
            startInitialGeneration = _initialGenerationPending;
            initialPrompt = _pendingPrompt;
            _initialGenerationPending = false;
        }

        RunMindCommandPaletteDiagnostics.Write(
            $"Inline answer page GetContent called. Pending={startInitialGeneration}; prompt length={initialPrompt.Length}.");

        // Selecting the fallback navigates to this ContentPage and calls
        // GetContent. Start inference only at this point, not for every unmatched
        // query typed into Command Palette.
        if (startInitialGeneration)
            BeginGeneration(initialPrompt, clearHistory: true);

        string markdown;
        lock (_sync) markdown = BuildMarkdownLocked();
        return [new MarkdownContent(markdown), _conversationForm];
    }

    private void BeginGeneration(string prompt, bool clearHistory)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource? current = null;
        InlineConversationTurn[] history;
        long generation;

        lock (_sync)
        {
            previous = _activeRequest;
            if (string.IsNullOrWhiteSpace(prompt))
            {
                _activeRequest = null;
                _generation++;
                if (clearHistory) _history.Clear();
                _currentPrompt = string.Empty;
                _currentAnswer = string.Empty;
                _status = "Type RunMind followed by a question.";
                _provider = string.Empty;
                _providerId = string.Empty;
                _model = string.Empty;
                _currentTimestampMilliseconds = 0;
                _isError = false;
                _notice = string.Empty;
                IsLoading = false;
                history = [];
                generation = _generation;
            }
            else
            {
                current = new CancellationTokenSource();
                _activeRequest = current;
                generation = ++_generation;
                if (clearHistory) _history.Clear();
                _currentPrompt = prompt.Trim();
                _currentAnswer = string.Empty;
                _provider = string.Empty;
                _providerId = string.Empty;
                _model = string.Empty;
                _currentTimestampMilliseconds = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                _status = "Connecting to the selected model…";
                _isError = false;
                _notice = string.Empty;
                IsLoading = true;
                history = _history.ToArray();
            }
        }

        try { previous?.Cancel(); }
        catch (ObjectDisposedException) { }
        if (current is null)
        {
            NotifyContentChanged(force: true);
            return;
        }

        RunMindCommandPaletteDiagnostics.Write(
            $"Starting inline generation {generation}; prompt length={prompt.Trim().Length}; previous history turns={history.Length}.");
        NotifyContentChanged(force: true);
        _ = GenerateAsync(generation, current, history, prompt.Trim());
    }

    private async Task GenerateAsync(
        long generation,
        CancellationTokenSource requestSource,
        IReadOnlyList<InlineConversationTurn> history,
        string prompt)
    {
        var token = requestSource.Token;
        try
        {
            // Let Command Palette attach the newly opened content page before
            // asynchronous status/error notifications begin.
            await Task.Yield();
            token.ThrowIfCancellationRequested();
            var settings = InlineModelSettingsStore.Load();
            RunMindCommandPaletteDiagnostics.Write(
                $"Inline generation {generation} loaded settings. Provider={settings.Provider}; model='{settings.Model}'; settings file='{InlineModelSettingsStore.FilePath}'.");
            lock (_sync)
            {
                if (generation != _generation || token.IsCancellationRequested) return;
                _provider = settings.Provider == "lmstudio" ? "LM Studio" : "Ollama";
                _providerId = settings.Provider;
                _model = settings.Model;
                _status = string.IsNullOrWhiteSpace(settings.Model)
                    ? "No model selected. Open RunMind extension Settings and choose one of your provider's models."
                    : $"Generating with {_provider} · {_model}…";
                IsLoading = true;
            }
            NotifyContentChanged(force: true);

            RunMindCommandPaletteDiagnostics.Write(
                $"Inline generation {generation} is sending the request to provider '{settings.Provider}' for model '{settings.Model}'.");
            var lastUpdate = Stopwatch.GetTimestamp();
            var response = await InlineModelClient.GenerateAsync(
                settings,
                history,
                prompt,
                chunk =>
                {
                    bool shouldNotify = false;
                    lock (_sync)
                    {
                        if (generation != _generation || token.IsCancellationRequested) return;
                        _currentAnswer += chunk;
                        var now = Stopwatch.GetTimestamp();
                        if (Stopwatch.GetElapsedTime(lastUpdate, now) >= TimeSpan.FromMilliseconds(100))
                        {
                            lastUpdate = now;
                            shouldNotify = true;
                        }
                    }
                    if (shouldNotify) NotifyContentChanged(force: false);
                },
                notice =>
                {
                    lock (_sync)
                    {
                        if (generation != _generation || token.IsCancellationRequested) return;
                        _notice = notice;
                        _status = "Retrying with Auto reasoning…";
                    }
                    NotifyContentChanged(force: true);
                },
                token).ConfigureAwait(false);

            if (string.IsNullOrWhiteSpace(response))
                throw new InvalidDataException("The selected model completed without returning answer text. Check the model's reasoning support or choose another model.");

            lock (_sync)
            {
                if (generation != _generation || token.IsCancellationRequested) return;
                _currentAnswer = response;
                _status = "Completed";
                _isError = false;
                IsLoading = false;
            }
            RunMindCommandPaletteDiagnostics.Write(
                $"Inline generation {generation} completed; response length={response.Length}.");
            NotifyContentChanged(force: true);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            // A newer search or follow-up question superseded this response.
        }
        catch (Exception exception)
        {
            RunMindCommandPaletteDiagnostics.Write(
                $"Inline generation {generation} failed.", exception);
            lock (_sync)
            {
                if (generation != _generation || token.IsCancellationRequested) return;
                _currentAnswer = exception.Message;
                _status = "Request failed";
                _isError = true;
                IsLoading = false;
            }
            NotifyContentChanged(force: true);
        }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_activeRequest, requestSource)) _activeRequest = null;
            }
            requestSource.Dispose();
        }
    }

    private bool SendFollowUp(string prompt)
    {
        prompt = prompt?.Trim() ?? string.Empty;
        if (prompt.Length == 0) return false;
        lock (_sync)
        {
            if (IsLoading) return false;
            if (!string.IsNullOrWhiteSpace(_currentPrompt)
                && !string.IsNullOrWhiteSpace(_currentAnswer)
                && !_isError)
            {
                _history.Add(new InlineConversationTurn(
                    _currentPrompt,
                    _currentAnswer,
                    _providerId,
                    _model,
                    _currentTimestampMilliseconds));
            }
        }
        BeginGeneration(prompt, clearHistory: false);
        return true;
    }

    private void StartFreshConversation()
    {
        CancellationTokenSource? previous;
        lock (_sync)
        {
            previous = _activeRequest;
            _activeRequest = null;
            _generation++;
            _history.Clear();
            _sourceQuery = string.Empty;
            _pendingPrompt = string.Empty;
            _initialGenerationPending = false;
            _currentPrompt = string.Empty;
            _currentAnswer = string.Empty;
            _status = "Fresh conversation. Type a message below.";
            _provider = string.Empty;
            _providerId = string.Empty;
            _model = string.Empty;
            _currentTimestampMilliseconds = 0;
            _isError = false;
            _notice = string.Empty;
            IsLoading = false;
            _conversationForm = new InlineConversationForm(this);
        }

        try { previous?.Cancel(); }
        catch (ObjectDisposedException) { }

        // The new FormContent instance guarantees the old text-box value is not
        // retained by Command Palette's Adaptive Card renderer.
        RunMindCommandPaletteDiagnostics.Write(
            "Started a fresh inline conversation; prior history was cleared and any active request was cancelled.");
        NotifyContentChanged(force: true);
    }

    private LaunchResult OpenChatWithContext()
    {
        string? conversationJson = null;
        int messageCount;
        bool currentTurnPending;
        bool hasConversation;
        lock (_sync)
        {
            currentTurnPending = IsLoading;
            hasConversation = _history.Count > 0 || !string.IsNullOrWhiteSpace(_currentPrompt);
            messageCount = _history.Count * 2
                + (string.IsNullOrWhiteSpace(_currentPrompt) ? 0 : 1)
                + (!currentTurnPending && !string.IsNullOrWhiteSpace(_currentAnswer) ? 1 : 0);
            if (hasConversation)
                conversationJson = BuildConversationJsonLocked();
        }

        if (!hasConversation || string.IsNullOrWhiteSpace(conversationJson))
        {
            RunMindCommandPaletteDiagnostics.Write(
                "Open chat with context selected on an empty inline page; launching a new empty chat instead of reporting an empty-transcript error.");
            return RunMindLauncher.Open();
        }

        RunMindCommandPaletteDiagnostics.Write(
            $"Open chat with full inline context requested. Messages={messageCount}; JSON length={conversationJson.Length}; currentTurnPending={currentTurnPending}.");
        return RunMindLauncher.OpenWithConversation(conversationJson);
    }

    private string BuildConversationJsonLocked()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = new JsonArray();
        var latestProvider = _providerId;
        var latestModel = _model;

        void AddMessage(string role, string content, long timestamp, string provider, string model, bool isError = false)
        {
            var message = new JsonObject
            {
                ["role"] = role,
                ["content"] = content,
                ["timestamp"] = DateTimeOffset.FromUnixTimeMilliseconds(
                    timestamp > 0 ? timestamp : now.ToUnixTimeMilliseconds()).ToString("O"),
                ["thinkRequested"] = false,
            };
            if (provider is "ollama" or "lmstudio") message["provider"] = provider;
            if (!string.IsNullOrWhiteSpace(model)) message["model"] = model;
            if (isError) message["error"] = true;
            messages.Add(message);
        }

        foreach (var turn in _history)
        {
            AddMessage("user", turn.Prompt, turn.TimestampMilliseconds, turn.Provider, turn.Model);
            AddMessage("assistant", turn.Response, turn.TimestampMilliseconds + 1, turn.Provider, turn.Model);
            if (!string.IsNullOrWhiteSpace(turn.Provider)) latestProvider = turn.Provider;
            if (!string.IsNullOrWhiteSpace(turn.Model)) latestModel = turn.Model;
        }

        if (!string.IsNullOrWhiteSpace(_currentPrompt))
        {
            AddMessage("user", _currentPrompt, _currentTimestampMilliseconds, _providerId, _model);
            // If generation is still running, transfer the current user message only.
            // A partial assistant response is not a completed turn and must not be
            // presented as a finished answer in the destination chat.
            if (!IsLoading && !string.IsNullOrWhiteSpace(_currentAnswer))
                AddMessage("assistant", _currentAnswer, _currentTimestampMilliseconds + 1, _providerId, _model, _isError);
        }

        var titleSource = _history.Count > 0 ? _history[0].Prompt : _currentPrompt;
        var title = string.IsNullOrWhiteSpace(titleSource) ? "RunMind Conversation" : titleSource.Trim();
        if (title.Length > 72) title = title[..69].TrimEnd() + "…";

        var firstTimestamp = _history.Count > 0
            ? _history[0].TimestampMilliseconds
            : _currentTimestampMilliseconds;
        var createdAt = firstTimestamp > 0
            ? DateTimeOffset.FromUnixTimeMilliseconds(firstTimestamp)
            : now;
        var conversation = new JsonObject
        {
            ["format"] = "runmind",
            ["version"] = 2,
            ["title"] = title,
            ["createdAt"] = createdAt.ToString("O"),
            ["updatedAt"] = now.ToString("O"),
            ["session"] = new JsonObject
            {
                ["provider"] = latestProvider is "ollama" or "lmstudio" ? latestProvider : null,
                ["model"] = string.IsNullOrWhiteSpace(latestModel) ? null : latestModel,
            },
            ["messages"] = messages,
        };

        var json = conversation.ToJsonString();
        const int maximumConversationJsonCharacters = 4 * 1024 * 1024;
        if (json.Length > maximumConversationJsonCharacters)
            throw new InvalidOperationException("The inline conversation is too large to transfer (maximum 4 MB). Open a new conversation or shorten the inline history.");
        if (messages.Count == 0)
            throw new InvalidOperationException("There are no inline messages to transfer.");
        return json;
    }

    private string BuildMarkdownLocked()
    {
        var builder = new StringBuilder();
        builder.AppendLine("# RunMind");
        if (!string.IsNullOrWhiteSpace(_provider))
            builder.Append("**Model:** ").Append(_provider).Append(" · ").AppendLine(_model);
        builder.Append("**Status:** ").AppendLine(_status);
        builder.AppendLine().AppendLine("Continue this conversation, or choose **Start fresh** below to clear its history.");
        if (!string.IsNullOrWhiteSpace(_notice))
            builder.AppendLine().Append("> **Notice:** ").AppendLine(_notice);

        foreach (var turn in _history)
        {
            builder.AppendLine().AppendLine("---").AppendLine();
            builder.AppendLine("**You**").AppendLine().AppendLine(turn.Prompt);
            builder.AppendLine().AppendLine("**RunMind**").AppendLine().AppendLine(turn.Response);
        }

        if (!string.IsNullOrWhiteSpace(_currentPrompt))
        {
            builder.AppendLine().AppendLine("---").AppendLine();
            builder.AppendLine("**You**").AppendLine().AppendLine(_currentPrompt);
            builder.AppendLine().AppendLine("**RunMind**").AppendLine().AppendLine(
                string.IsNullOrWhiteSpace(_currentAnswer) ? (_isError ? "The request failed." : "Generating response…") : _currentAnswer);
        }
        else
        {
            builder.AppendLine().AppendLine("Type a message below to start a conversation. Use **Start fresh** at any time to clear this page's history. The Dock and search fallback keep separate conversations.");
        }

        if (_isError) builder.AppendLine().AppendLine("**Error**");
        return builder.ToString();
    }

    private void NotifyContentChanged(bool force)
    {
        var now = Stopwatch.GetTimestamp();
        if (!force && Stopwatch.GetElapsedTime(_lastUiUpdate, now) < TimeSpan.FromMilliseconds(100)) return;
        _lastUiUpdate = now;
        try { RaiseItemsChanged(2); }
        catch { /* The user may have dismissed Command Palette during generation. */ }
    }

    private static string ExtractPrompt(string? query)
    {
        var value = query?.Trim() ?? string.Empty;
        if (value.Length == 0) return string.Empty;

        const string longPrefix = "ask runmind";
        const string prefix = "runmind";
        if (StartsWithPromptPrefix(value, longPrefix))
            return value.Length == longPrefix.Length ? string.Empty : value[longPrefix.Length..].TrimStart(' ', ':', '-', '—');
        if (StartsWithPromptPrefix(value, prefix))
            return value.Length == prefix.Length ? string.Empty : value[prefix.Length..].TrimStart(' ', ':', '-', '—');

        // A user may type a plain natural-language query and choose the RunMind
        // fallback row. In that case the full search text is the model prompt.
        return value;
    }

    private static bool StartsWithPromptPrefix(string value, string prefix)
        => value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
            && (value.Length == prefix.Length
                || char.IsWhiteSpace(value[prefix.Length])
                || value[prefix.Length] is ':' or '-' or '—');

    private sealed partial class InlineConversationForm : FormContent
    {
        private readonly InlineAnswerPage _owner;

        public InlineConversationForm(InlineAnswerPage owner)
        {
            _owner = owner;
            TemplateJson = """
            {
              "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
              "type": "AdaptiveCard",
              "version": "1.6",
              "body": [
                {
                  "type": "Input.Text",
                  "id": "prompt",
                  "label": "Your message",
                  "placeholder": "Type a message…",
                  "isMultiline": true,
                  "maxLength": 12000
                }
              ],
              "actions": [
                { "type": "Action.Submit", "title": "Send", "data": { "action": "send" } },
                { "type": "Action.Submit", "title": "Start fresh", "data": { "action": "startFresh" } },
                { "type": "Action.Submit", "title": "Open chat with context", "data": { "action": "openChat" } }
              ]
            }
            """;
        }

        // Keep the legacy overload for older hosts. Current Command Palette
        // passes the input values and Action.Submit.data independently.
        public override CommandResult SubmitForm(string payload)
            => SubmitForm(payload, "{}");

        public override CommandResult SubmitForm(string inputs, string data)
        {
            try
            {
                RunMindCommandPaletteDiagnostics.Write(
                    $"Inline conversation form submitted; inputs length={inputs?.Length ?? 0}; action-data length={data?.Length ?? 0}.");
                var values = FlattenPayload(inputs ?? "{}", data ?? "{}");
                var action = ReadString(values, "action");
                RunMindCommandPaletteDiagnostics.Write(
                    $"Inline conversation form action='{action}'.");
                if (action.Equals("openChat", StringComparison.OrdinalIgnoreCase))
                {
                    var result = _owner.OpenChatWithContext();
                    return CommandResult.ShowToast(result.Message);
                }

                if (action.Equals("startFresh", StringComparison.OrdinalIgnoreCase))
                {
                    _owner.StartFreshConversation();
                    return CommandResult.KeepOpen();
                }

                var prompt = ReadString(values, "prompt");
                if (string.IsNullOrWhiteSpace(prompt))
                    return CommandResult.ShowToast("Enter a follow-up question first.");
                if (!_owner.SendFollowUp(prompt))
                    return CommandResult.ShowToast("Wait for the current response to finish before sending a follow-up.");
                return CommandResult.KeepOpen();
            }
            catch (Exception exception)
            {
                RunMindCommandPaletteDiagnostics.Write("Inline conversation form submission failed.", exception);
                return CommandResult.ShowToast("Could not submit the follow-up: " + exception.Message);
            }
        }

        private static JsonObject FlattenPayload(string inputs, string data)
        {
            var values = new JsonObject();
            MergePayload(inputs, values);
            MergePayload(data, values);
            return values;
        }

        private static void MergePayload(string payload, JsonObject destination)
        {
            if (string.IsNullOrWhiteSpace(payload)) return;
            var root = JsonNode.Parse(payload) as JsonObject
                ?? throw new InvalidDataException("Command Palette returned an invalid conversation form payload.");
            foreach (var key in new[] { "data", "inputs" })
            {
                if (root[key] is JsonObject nested)
                    foreach (var pair in nested) destination[pair.Key] = pair.Value?.DeepClone();
            }
            foreach (var pair in root) destination[pair.Key] = pair.Value?.DeepClone();
        }

        private static string ReadString(JsonObject values, string key)
        {
            if (values[key] is JsonValue value && value.TryGetValue<string>(out var text)) return text?.Trim() ?? string.Empty;
            return string.Empty;
        }
    }
}
