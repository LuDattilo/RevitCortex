using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RevitCortex.Plugin.Tracking;

namespace RevitCortex.Plugin.UI;

public class CortexChatClient
{
    private readonly List<JObject> _conversationHistory = new();
    private string? _apiKey;
    private const string ANTHROPIC_URL = "https://api.anthropic.com/v1/messages";
    private string _model = "claude-sonnet-4-6";
    private static int MCP_PORT = 8080;

    static CortexChatClient()
    {
        try
        {
            string settingsPath = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".revitcortex", "settings.json");
            if (File.Exists(settingsPath))
            {
                var json = File.ReadAllText(settingsPath);
                var settings = JObject.Parse(json);
                var port = settings["Port"]?.ToObject<int>();
                if (port.HasValue && port.Value > 0 && port.Value <= 65535)
                    MCP_PORT = port.Value;
            }
        }
        catch { /* fallback to 8080 */ }
    }
    private CancellationTokenSource? _cts;

    public string Model
    {
        get => _model;
        set => _model = value;
    }

    private const string SYSTEM_PROMPT = @"You are an AI assistant integrated directly into Autodesk Revit via RevitCortex. You have access to tools that execute commands on the active Revit model in real time.

BEHAVIOR:
- Manage the model directly. When the user asks for something, EXECUTE the action with the available tools. Do not ask for unnecessary confirmations.
- For simple tasks (info, reading, single operation): execute immediately.
- For complex tasks (multi-step, creating multiple elements, workflows): mentally plan the steps, then execute them one after another.
- Use reading tools (get_project_info, get_available_family_types, ai_element_filter, get_selected_elements) to discover what is in the model before acting.
- If the user says 'selected elements', use get_selected_elements. If empty, ask them to select.
- After each operation, briefly describe the result.

RULES:
- Revit parameter and category names are localized (e.g. 'Muri' in Italian, 'Walls' in English). Use BuiltInCategory (OST_Walls, OST_Doors, etc.) for categories when possible.
- Coordinates in millimeters (mm).
- Reply in the user's language, be concise.";

    public CortexChatClient()
    {
        LoadApiKey();
    }

    public void Cancel() => _cts?.Cancel();

    public void ClearHistory() => _conversationHistory.Clear();

    private void LoadApiKey()
    {
        string claudeDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");

        _apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        if (string.IsNullOrEmpty(_apiKey))
        {
            try
            {
                string path = Path.Combine(claudeDir, "api_key.txt");
                if (File.Exists(path)) _apiKey = File.ReadAllText(path).Trim();
            }
            catch { }
        }
    }

    public async Task<string> SendMessage(string userMessage)
    {
        if (string.IsNullOrEmpty(_apiKey))
        {
            return "API key not configured.\n\n" +
                   "Set your Anthropic API key:\n" +
                   "  File: %USERPROFILE%\\.claude\\api_key.txt\n" +
                   "  or env variable: ANTHROPIC_API_KEY=sk-ant-...";
        }

        _conversationHistory.Add(new JObject { ["role"] = "user", ["content"] = userMessage });

        // Trim history in pairs to avoid orphaning tool_use/tool_result blocks
        while (_conversationHistory.Count > 30)
        {
            _conversationHistory.RemoveAt(0);
            if (_conversationHistory.Count > 0)
                _conversationHistory.RemoveAt(0);
        }

        _cts?.Dispose();
        _cts = new CancellationTokenSource();

        try
        {
            return await ProcessConversation();
        }
        catch (OperationCanceledException)
        {
            return "Operation cancelled.";
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private async Task<string> ProcessConversation()
    {
        int maxToolRounds = 15;

        for (int round = 0; round < maxToolRounds; round++)
        {
            var response = await CallClaudeApi();
            if (response == null) return "No response from API.";

            var roundToolNames = new List<string>();

            var content = response["content"] as JArray;
            if (content == null) return "Empty response.";

            string stopReason = response["stop_reason"]?.ToString() ?? "end_turn";

            var thinkingParts = new List<string>();
            var textParts = new List<string>();
            var toolUses = new List<JObject>();

            foreach (var block in content)
            {
                string? blockType = block["type"]?.ToString();
                if (blockType == "thinking")
                    thinkingParts.Add(block["thinking"]?.ToString() ?? "");
                else if (blockType == "text")
                    textParts.Add(block["text"]?.ToString() ?? "");
                else if (blockType == "tool_use")
                    toolUses.Add((JObject)block);
            }

            if (thinkingParts.Count > 0)
                CortexPanel.Instance?.OnThinkingReceived(string.Join("\n", thinkingParts));

            foreach (var tu in toolUses)
                roundToolNames.Add(tu["name"]?.ToString() ?? "unknown");

            _conversationHistory.Add(new JObject { ["role"] = "assistant", ["content"] = content });

            // Record API token usage
            try
            {
                var usage = response["usage"];
                if (usage != null)
                {
                    int inputTokens = usage["input_tokens"]?.Value<int>() ?? 0;
                    int outputTokens = usage["output_tokens"]?.Value<int>() ?? 0;
                    UsageTracker.Record(_model, inputTokens, outputTokens, 0, roundToolNames, 0);
                }
            }
            catch { }

            if (textParts.Count > 0 && toolUses.Count > 0)
                CortexPanel.Instance?.OnIntermediateText(string.Join("\n", textParts));

            if (stopReason == "tool_use" && toolUses.Count > 0)
            {
                var toolResults = new JArray();
                foreach (var toolUse in toolUses)
                {
                    string? toolName = toolUse["name"]?.ToString();
                    string? toolId = toolUse["id"]?.ToString();
                    JObject toolInput = toolUse["input"] as JObject ?? new JObject();

                    CortexPanel.Instance?.OnToolExecuting(toolName ?? "unknown");

                    string result = await ExecuteMcpCommand(toolName ?? "", toolInput);
                    bool isError = result.StartsWith("Error:") || result.StartsWith("MCP command failed:");

                    CortexPanel.Instance?.OnToolCompleted(toolName ?? "unknown", isError, result);

                    toolResults.Add(new JObject
                    {
                        ["type"] = "tool_result",
                        ["tool_use_id"] = toolId,
                        ["content"] = result
                    });
                }

                _conversationHistory.Add(new JObject { ["role"] = "user", ["content"] = toolResults });

                CortexPanel.Instance?.OnRoundProgress(round + 1, maxToolRounds);
                continue;
            }

            return string.Join("\n", textParts);
        }

        return "Too many tool iterations. Try again with a simpler request.";
    }

    private async Task<JObject?> CallClaudeApi()
    {
        var requestBody = new JObject
        {
            ["model"] = _model,
            ["max_tokens"] = 16000,
            ["system"] = SYSTEM_PROMPT,
            ["tools"] = GetToolDefinitions(),
            ["messages"] = JArray.FromObject(_conversationHistory),
            ["thinking"] = new JObject
            {
                ["type"] = "enabled",
                ["budget_tokens"] = 10000
            }
        };

        byte[] data = Encoding.UTF8.GetBytes(requestBody.ToString());
        int maxRetries = 3;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            _cts?.Token.ThrowIfCancellationRequested();

            var request = (HttpWebRequest)WebRequest.Create(ANTHROPIC_URL);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Headers.Add("x-api-key", _apiKey!);
            request.Headers.Add("anthropic-version", "2023-06-01");
            request.Timeout = 180000;

            using (var stream = await Task.Factory.FromAsync(
                request.BeginGetRequestStream, request.EndGetRequestStream, null))
            {
                await stream.WriteAsync(data, 0, data.Length);
            }

            try
            {
                using var response = (HttpWebResponse)await Task.Factory.FromAsync(
                    request.BeginGetResponse, request.EndGetResponse, null);
                using var reader = new StreamReader(response.GetResponseStream()!);
                string responseText = await reader.ReadToEndAsync();
                return JObject.Parse(responseText);
            }
            catch (WebException ex)
            {
                var httpResponse = ex.Response as HttpWebResponse;

                if (httpResponse != null &&
                    ((int)httpResponse.StatusCode == 429 || (int)httpResponse.StatusCode == 529) &&
                    attempt < maxRetries - 1)
                {
                    httpResponse.Close();
                    int delayMs = (int)Math.Pow(2, attempt + 1) * 1000;
                    CortexPanel.Instance?.OnRetrying(delayMs / 1000);
                    await Task.Delay(delayMs, _cts?.Token ?? CancellationToken.None);
                    continue;
                }

                if (ex.Response != null)
                {
                    using var reader = new StreamReader(ex.Response.GetResponseStream()!);
                    string errorText = await reader.ReadToEndAsync();
                    try
                    {
                        var errorJson = JObject.Parse(errorText);
                        throw new Exception($"API Error: {errorJson["error"]?["message"]}");
                    }
                    catch (JsonReaderException)
                    {
                        throw new Exception($"API Error ({(int?)httpResponse?.StatusCode}): {errorText}");
                    }
                }
                throw;
            }
        }

        throw new Exception("Max retries exceeded");
    }

    private async Task<string> ExecuteMcpCommand(string commandName, JObject parameters)
    {
        try
        {
            var jsonRpc = new JObject
            {
                ["jsonrpc"] = "2.0",
                ["id"] = Guid.NewGuid().ToString(),
                ["method"] = commandName,
                ["params"] = parameters
            };

            string request = jsonRpc.ToString(Formatting.None);

            using var client = new TcpClient();
            var connectTask = client.ConnectAsync("127.0.0.1", MCP_PORT);
            if (await Task.WhenAny(connectTask, Task.Delay(10000)) != connectTask)
                return "MCP command failed: Connection timeout (server not responding)";
            await connectTask;

            var stream = client.GetStream();

            byte[] requestData = Encoding.UTF8.GetBytes(request + "\n");
            await stream.WriteAsync(requestData, 0, requestData.Length);

            byte[] buffer = new byte[65536];
            var responseBuilder = new StringBuilder();

            client.ReceiveTimeout = 120000;

            while (true)
            {
                int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                if (bytesRead == 0) break;
                responseBuilder.Append(Encoding.UTF8.GetString(buffer, 0, bytesRead));

                string current = responseBuilder.ToString();
                if (current.Contains('\n'))
                    break;
            }

            string responseStr = responseBuilder.ToString().Trim();
            var responseJson = JObject.Parse(responseStr);

            if (responseJson["result"] != null)
                return responseJson["result"]!.ToString(Formatting.Indented);
            if (responseJson["error"] != null)
                return $"Error: {responseJson["error"]?["message"]}";

            return responseStr;
        }
        catch (Exception ex)
        {
            return $"MCP command failed: {ex.Message}";
        }
    }

    private JArray? _cachedToolDefinitions;

    private JArray GetToolDefinitions()
    {
        if (_cachedToolDefinitions != null)
            return _cachedToolDefinitions;

        _cachedToolDefinitions = LoadToolsFromSchemaFile() ?? BuildFallbackTools();
        return _cachedToolDefinitions;
    }

    private JArray? LoadToolsFromSchemaFile()
    {
        try
        {
            string dllDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
            string schemaPath = Path.Combine(dllDir, "tool_schemas.json");

            if (!File.Exists(schemaPath))
                return null;

            var tools = JArray.Parse(File.ReadAllText(schemaPath));
            return tools.Count > 0 ? tools : null;
        }
        catch
        {
            return null;
        }
    }

    private JArray BuildFallbackTools()
    {
        var tools = new JArray();
        var fallbackCommands = new[]
        {
            ("get_project_info", "Get project info: name, address, author, levels, phases, links"),
            ("analyze_model_statistics", "Analyze model: element counts by category, types, families, levels"),
            ("get_warnings", "Get all warnings/errors from the Revit model"),
            ("ai_element_filter", "Query elements by category, parameter filters, and conditions. Params: data.filterCategory (OST_* code), data.includeInstances (bool), data.maxElements (int)"),
            ("get_selected_elements", "Get currently selected elements with their parameters"),
            ("get_element_parameters", "Get parameters of specific elements. Params: elementIds (number array)"),
            ("get_current_view_info", "Get info about the active view"),
            ("get_phases", "Get all phases in the project"),
            ("create_level", "Create levels at specified elevations (mm)"),
            ("create_line_based_element", "Create walls or other line-based elements (mm)"),
            ("create_room", "Create rooms at specified positions (mm)"),
            ("delete_element", "Delete elements by ID"),
            ("export_room_data", "Export all room data: name, number, level, area, volume"),
            ("get_materials", "List all project materials with color and properties"),
            ("purge_unused", "Identify and optionally remove unused families, types, materials"),
            ("say_hello", "Show a dialog in Revit with a message (connection test)")
        };

        foreach (var (name, desc) in fallbackCommands)
        {
            tools.Add(new JObject
            {
                ["name"] = name,
                ["description"] = desc,
                ["input_schema"] = new JObject
                {
                    ["type"] = "object",
                    ["properties"] = new JObject()
                }
            });
        }

        return tools;
    }
}
