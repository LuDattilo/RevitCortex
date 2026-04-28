namespace RevitCortex.Core.Tools;

/// <summary>
/// Optional metadata for tools that need more than the standard dispatcher wait.
/// </summary>
public interface ICommandTimeoutTool
{
    int CommandTimeoutSeconds { get; }
}
