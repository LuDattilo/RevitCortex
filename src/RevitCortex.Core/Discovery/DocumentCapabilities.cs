using System.Collections.Generic;

namespace RevitCortex.Core.Discovery;

public class DocumentCapabilities
{
    public static readonly IReadOnlyCollection<string> KnownDynamicToolNames = new[]
    {
        "get_worksets",
        "manage_worksets",
        "set_element_workset",
        "get_phases",
        "set_element_phase",
        "get_linked_file_instances",
        "get_link_transform",
        "reload_linked_file_from",
        "pin_unpin_link_instance",
        "move_link_instance",
        "align_link_to_host",
        "highlight_linked_element",
        "show_cross_model_elements",
        "get_selected_linked_elements",
        "get_room_openings",
    };

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
