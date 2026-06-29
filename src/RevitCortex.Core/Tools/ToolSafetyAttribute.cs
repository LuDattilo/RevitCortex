using System;

namespace RevitCortex.Core.Tools;

/// <summary>
/// Declares the safety contract for a RevitCortex tool.
/// ReadOnly is authoritative for read-only mode; Destructive is metadata
/// available to routing, auditing, and UI surfaces.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class ToolSafetyAttribute : Attribute
{
    public ToolSafetyAttribute(bool readOnly, bool destructive = false)
    {
        ReadOnly = readOnly;
        Destructive = destructive;
    }

    public bool ReadOnly { get; }
    public bool Destructive { get; }
}

public sealed class ToolSafetyInfo
{
    public ToolSafetyInfo(bool readOnly, bool destructive)
    {
        ReadOnly = readOnly;
        Destructive = destructive;
    }

    public bool ReadOnly { get; }
    public bool Destructive { get; }
}

public interface IToolSafetyAware
{
    ToolSafetyInfo GetToolSafety();
}
