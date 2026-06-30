using System;
using System.ComponentModel;
using System.IO;
using System.Reflection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Tools;

/// <summary>
/// Regression guards for the send_code_to_revit tool guidance.
///
/// The 2026-06-30 audit found the model-facing description had drifted toward a
/// permissive "Execute custom C# code in the Revit context" wording that nudged the
/// model to pick the script tool over dedicated tools (44/54 logged calls failed).
/// These tests pin the de-escalation guidance in the channels the model actually
/// reads: the MCP [Description] attribute on the server method, the server-level
/// ServerInstructions handshake, and the in-Revit UI tool description.
/// </summary>
public class SendCodeDescriptionTests
{
    private static string ServerToolDescription()
    {
        var method = typeof(ProjectTools).GetMethod(
            nameof(ProjectTools.SendCodeToRevit),
            BindingFlags.Public | BindingFlags.Static);
        Assert.NotNull(method);
        var attr = method!.GetCustomAttribute<DescriptionAttribute>();
        Assert.NotNull(attr);
        return attr!.Description;
    }

    private static string ReadSource(params string[] segments)
    {
        var parts = new string[4 + segments.Length];
        parts[0] = parts[1] = parts[2] = parts[3] = "..";
        Array.Copy(segments, 0, parts, 4, segments.Length);
        var path = Path.GetFullPath(Path.Combine(parts));
        return File.ReadAllText(path);
    }

    [Fact]
    public void ServerDescription_MarksToolAsLastResort()
    {
        Assert.Contains("LAST RESORT", ServerToolDescription(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerDescription_RequiresExplicitUserConsent()
    {
        Assert.Contains("consent", ServerToolDescription(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerDescription_PointsToDedicatedAlternatives()
    {
        var desc = ServerToolDescription();
        Assert.Contains("set_element_parameters", desc, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ai_element_filter", desc, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ServerDescription_DoesNotSteerToModalFamilyEditing()
    {
        // Document.EditFamily deadlocks from the tool's external-event context, so the
        // description must not advertise family-internal editing as a valid escalation.
        Assert.DoesNotContain("editing a family's internal definition", ServerToolDescription());
    }

    [Fact]
    public void ServerDescription_DropsPermissiveLegacyWording()
    {
        Assert.DoesNotContain("Execute custom C# code in the Revit context", ServerToolDescription());
    }

    [Fact]
    public void ServerInstructions_DeprioritizeSendCode()
    {
        var program = ReadSource("RevitCortex.Server", "Program.cs");
        Assert.Contains("ServerInstructions", program);
        Assert.Contains("LAST RESORT", program, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PluginDescription_MarksToolAsLastResort()
    {
        var source = ReadSource("RevitCortex.Tools", "Elements", "SendCodeToRevitTool.cs");
        Assert.Contains("LAST RESORT", source, StringComparison.OrdinalIgnoreCase);
    }
}
