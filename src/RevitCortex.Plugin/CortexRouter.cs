using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Results;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin.Threading;

namespace RevitCortex.Plugin;

public class CortexRouter
{
    private readonly Dictionary<string, ICortexTool> _tools = new();
    private readonly CortexSession _session;
    private readonly IDocumentAnalyzer _analyzer;
    private RevitThreadDispatcher? _dispatcher;
    private readonly HashSet<string> _disabledTools = new();

    public CortexRouter(CortexSession session, IDocumentAnalyzer analyzer)
    {
        _session = session;
        _analyzer = analyzer;
    }

    /// <summary>
    /// Scan an assembly for all ICortexTool implementations and register them.
    /// </summary>
    public void RegisterToolsFromAssembly(Assembly assembly)
    {
        var toolTypes = assembly.GetTypes()
            .Where(t => typeof(ICortexTool).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract);

        foreach (var type in toolTypes)
        {
            try
            {
                var tool = (ICortexTool)Activator.CreateInstance(type)!;
                _tools[tool.Name] = tool;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Trace.WriteLine(
                    $"[RevitCortex] Failed to register tool {type.Name}: {ex.Message}");
            }
        }
    }

    public CortexResult<object> Route(string toolName, JObject input)
    {
        if (!_tools.TryGetValue(toolName, out var tool))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' not found",
                suggestion: $"Available tools: {string.Join(", ", GetAvailableToolNames())}");

        if (_disabledTools.Contains(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' is disabled",
                suggestion: "Enable it in RevitCortex Settings > Tools");

        if (tool.RequiresDocument && _session.Store.Get<object>("activeDocument") == null)
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                "No document open in Revit",
                suggestion: "Open a Revit document before using this tool");

        if (tool.IsDynamic && !_session.Capabilities.IsToolEnabled(toolName))
            return CortexResult<object>.Fail(CortexErrorCode.InvalidInput,
                $"Tool '{toolName}' is not available for this document",
                suggestion: "This tool requires specific document features (e.g., worksets, phases)");

        if (_dispatcher != null)
            return _dispatcher.Execute(tool, input, _session);
        else
            return tool.Execute(input, _session);
    }

    public void SetDispatcher(RevitThreadDispatcher dispatcher)
    {
        _dispatcher = dispatcher;
    }

    public void OnDocumentChanged(object document, string? locale = null)
    {
        var caps = new DocumentCapabilities();
        _analyzer.Analyze(document, caps);

        _session.Reinitialize(caps, locale ?? "en");
        _session.Store.Set("activeDocument", document);
    }

    public IReadOnlyList<string> GetAvailableToolNames()
    {
        return _tools.Values
            .Where(t => !_disabledTools.Contains(t.Name))
            .Where(t => !t.IsDynamic || _session.Capabilities.IsToolEnabled(t.Name))
            .Select(t => t.Name)
            .OrderBy(n => n)
            .ToList();
    }

    public int TotalToolCount => _tools.Count;

    /// <summary>
    /// Returns all registered tools with their name, category, description, and enabled state.
    /// </summary>
    public IReadOnlyList<(string Name, string Category, string Description, bool IsEnabled)> GetAllToolInfo()
    {
        return _tools.Values
            .OrderBy(t => t.Category)
            .ThenBy(t => t.Name)
            .Select(t => (t.Name, t.Category, t.Description, !_disabledTools.Contains(t.Name)))
            .ToList();
    }

    public void SetDisabledTools(IEnumerable<string> toolNames)
    {
        _disabledTools.Clear();
        foreach (var name in toolNames)
            _disabledTools.Add(name);
    }

    public IReadOnlyCollection<string> DisabledTools => _disabledTools;
}
