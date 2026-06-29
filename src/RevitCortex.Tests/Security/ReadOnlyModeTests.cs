using Newtonsoft.Json.Linq;
using RevitCortex.Core.Discovery;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Plugin;
using RevitCortex.Tests.Router;
using Xunit;

namespace RevitCortex.Tests.Security;

public class ReadOnlyModeTests
{
    private CortexRouter CreateRouter(out CortexSession session)
    {
        var store = new SessionStore();
        session = new CortexSession(store);
        var analyzer = new FakeAnalyzer();
        return new CortexRouter(session, analyzer);
    }

    [ToolSafety(false)]
    private sealed class DeclaredWriteWithReadPrefixTool : FakeTool
    {
        public DeclaredWriteWithReadPrefixTool()
        {
            Name = "get_declared_write";
        }
    }

    [ToolSafety(true)]
    private sealed class DeclaredReadWithWritePrefixTool : FakeTool
    {
        public DeclaredReadWithWritePrefixTool()
        {
            Name = "set_declared_read";
        }
    }

    [ToolSafety(false, true)]
    private sealed class DeclaredDestructiveTool : FakeTool
    {
        public DeclaredDestructiveTool()
        {
            Name = "delete_declared_destructive";
        }
    }

    [Fact]
    public void ReadOnlyMode_BlocksWriteTool()
    {
        var router = CreateRouter(out _);
        router.RegisterTool(new FakeTool { Name = "delete_element" });
        router.ReadOnlyMode = true;

        var result = router.Route("delete_element", new JObject());
        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
        Assert.Contains("read-only", result.Error.Message);
    }

    [Fact]
    public void ReadOnlyMode_AllowsReadTool()
    {
        var router = CreateRouter(out _);
        router.RegisterTool(new FakeTool { Name = "get_element_parameters" });
        router.ReadOnlyMode = true;

        var result = router.Route("get_element_parameters", new JObject());
        Assert.True(result.Success);
    }

    [Fact]
    public void ReadOnlyMode_Disabled_AllowsWriteTool()
    {
        var router = CreateRouter(out _);
        router.RegisterTool(new FakeTool { Name = "delete_element" });
        router.ReadOnlyMode = false;

        var result = router.Route("delete_element", new JObject());
        Assert.True(result.Success);
    }

    [Fact]
    public void ReadOnlyMode_BlocksDeclaredWriteToolWithReadPrefix()
    {
        var router = CreateRouter(out _);
        router.RegisterTool(new DeclaredWriteWithReadPrefixTool());
        router.ReadOnlyMode = true;

        var result = router.Route("get_declared_write", new JObject());

        Assert.False(result.Success);
        Assert.Equal(CortexErrorCode.PermissionDenied, result.Error!.Code);
    }

    [Fact]
    public void ReadOnlyMode_AllowsDeclaredReadToolWithWritePrefix()
    {
        var router = CreateRouter(out _);
        router.RegisterTool(new DeclaredReadWithWritePrefixTool());
        router.ReadOnlyMode = true;

        var result = router.Route("set_declared_read", new JObject());

        Assert.True(result.Success);
    }

    [Fact]
    public void ToolSafety_ExposesDestructiveMetadata()
    {
        var router = CreateRouter(out _);
        router.RegisterTool(new DeclaredDestructiveTool());

        Assert.True(router.IsToolDestructive("delete_declared_destructive"));
    }

    [Theory]
    [InlineData("get_element_parameters", true)]
    [InlineData("get_project_info", true)]
    [InlineData("list_families", true)]
    [InlineData("find_untagged_elements", true)]
    [InlineData("analyze_model_statistics", true)]
    [InlineData("check_model_health", true)]
    [InlineData("measure_between_elements", true)]
    [InlineData("export_elements_data", true)]
    [InlineData("say_hello", true)]
    [InlineData("delete_element", false)]
    [InlineData("set_element_parameters", false)]
    [InlineData("batch_rename", false)]
    [InlineData("create_view", false)]
    [InlineData("send_code_to_revit", false)]
    [InlineData("purge_unused", false)]
    [InlineData("color_elements", false)]
    [InlineData("list_rebar_bar_types", true)]
    [InlineData("get_rebar_host_data", true)]
    [InlineData("get_rebar_api_capabilities", true)]
    [InlineData("create_rebar_from_shape", false)]
    [InlineData("set_rebar_layout", false)]
    [InlineData("remove_rebar_system", false)]
    [InlineData("list_steel_connection_handlers", true)]
    [InlineData("get_steel_element_properties", true)]
    [InlineData("get_structural_steel_api_capabilities", true)]
    [InlineData("create_generic_steel_connection", false)]
    [InlineData("add_steel_solid_cut", false)]
    [InlineData("delete_steel_connection", false)]
    public void IsReadOnlyTool_ClassifiesCorrectly(string toolName, bool expectedReadOnly)
    {
        Assert.Equal(expectedReadOnly, CortexRouter.IsReadOnlyTool(toolName));
    }
}
