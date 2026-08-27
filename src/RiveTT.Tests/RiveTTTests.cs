using System.IO;
using RiveTT.Core.Session;
using Xunit;

namespace RiveTT.Tests;

public sealed class RiveTTTests
{
    [Fact]
    public void Automatic_mode_never_requires_a_confirmation_dialog()
    {
        var session = new CortexSession(new SessionStore());

        Assert.True(session.RequestConfirmation("delete", 100, critical: true));
    }

    [Fact]
    public void Core_assembly_uses_the_RiveTT_identity()
    {
        Assert.Equal("RiveTT.Core", typeof(CortexSession).Assembly.GetName().Name);
    }

    [Fact]
    public void Named_pipe_restricts_connections_to_the_current_user()
    {
        var source = ReadSource("RiveTT.Plugin", "Communication", "RevitNamedPipeService.cs");

        Assert.Contains("PipeOptions.CurrentUserOnly", source);
    }

    [Fact]
    public void Explicit_wall_level_treats_base_offset_as_relative()
    {
        var source = ReadSource("RiveTT.Tools", "Elements", "CreateLineBasedElementTool.cs");

        Assert.Contains("baseLevelId > 0", source);
        Assert.Contains("baseOffsetMm / MmPerFoot", source);
    }

    [Fact]
    public void Roslyn_loader_uses_the_renamed_tools_assembly()
    {
        var source = ReadSource("RiveTT.Tools", "CodeExecution", "RoslynExecutor.cs");

        // The guard is that the loader points at the RENAMED assembly. The old name is
        // what must be absent — the repo-wide RevitCortex -> RiveTT rename rewrote the
        // literal inside DoesNotContain too, which inverted the test: it then demanded
        // the absence of the very line Contains requires, and no source could satisfy both.
        Assert.Contains("RiveTT.Tools.dll", source);
        Assert.DoesNotContain("RevitCortex.Tools.dll", source);
    }

    [Fact]
    public void Server_does_not_expose_unsafe_create_project_activation()
    {
        var source = ReadSource("RiveTT.Server", "Tools", "DocumentTools.cs");

        Assert.DoesNotContain("Name = \"create_project\"", source);
    }

    private static string ReadSource(string project, params string[] relativeParts)
    {
        var parts = new string[relativeParts.Length + 5];
        parts[0] = "..";
        parts[1] = "..";
        parts[2] = "..";
        parts[3] = "..";
        parts[4] = project;
        relativeParts.CopyTo(parts, 5);
        return File.ReadAllText(Path.GetFullPath(Path.Combine(parts)));
    }
}
