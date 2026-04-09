using System.Collections.Generic;

namespace RevitCortex.Core.Discovery;

public class DocumentCapabilities
{
    public bool HasWorksets { get; set; }
    public bool HasPhases { get; set; }
    public bool HasDesignOptions { get; set; }
    public bool HasLinkedModels { get; set; }
    public HashSet<string> PresentCategories { get; set; } = new();
    public HashSet<string> SharedParameterNames { get; set; } = new();

    private readonly HashSet<string> _enabledTools = new();

    public void EnableTool(string toolName) => _enabledTools.Add(toolName);
    public void DisableTool(string toolName) => _enabledTools.Remove(toolName);
    public bool IsToolEnabled(string toolName) => _enabledTools.Contains(toolName);
    public IReadOnlyCollection<string> EnabledTools => _enabledTools;

    public void Reset()
    {
        HasWorksets = false;
        HasPhases = false;
        HasDesignOptions = false;
        HasLinkedModels = false;
        PresentCategories.Clear();
        SharedParameterNames.Clear();
        _enabledTools.Clear();
    }
}
