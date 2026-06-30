using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RevitCortex.Server.Connection;

var port = RevitConnectionManager.ResolvePort();

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton(new RevitConnectionManager(port));
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "RevitCortex",
            Version = "2.0.0"
        };
        options.ServerInstructions =
            "RevitCortex exposes hundreds of dedicated Revit tools. ALWAYS prefer the dedicated tool that matches the task " +
            "(parameters, filtering/queries, model statistics, views, schedules, tags, dimensions, rebar, steel, IFC, Power BI). " +
            "Destructive tools accept a dryRun option — preview before committing. " +
            "send_code_to_revit is a LAST RESORT: never select it autonomously. Escalate to it only when no dedicated tool covers " +
            "the operation (exotic geometry creation, read-only inspection of an uncovered Revit API, or a one-off operation no " +
            "dedicated tool covers) — never for modal family editing (Document.EditFamily deadlocks from the external-event context) " +
            "— and only after proposing the dedicated-tool alternative to the user and obtaining their explicit consent.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
