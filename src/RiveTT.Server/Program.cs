using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using RiveTT.Server.Connection;

var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddSingleton<RevitConnectionManager>();
builder.Services
    .AddMcpServer(options =>
    {
        options.ServerInfo = new()
        {
            Name = "RiveTT",
            Version = "0.3.0"
        };
        options.ServerInstructions =
            "RiveTT connects automatically to the active Revit session (2026.5+ or 2027) through a local Windows named pipe. " +
            "It is always in automatic mode: commands never open an authorization dialog. " +
            "BUT every Revit session starts READ-ONLY: tools that can modify the model are refused with " +
            "PermissionDenied until a human presses Ecriture in the RiveTT ribbon panel (Add-Ins tab). " +
            "No tool can lift that lock, dryRun included; read execution.writesAllowed, or " +
            "get_server_capabilities.readOnlyMode, and ask the user to unlock rather than retrying. " +
            "Prefer the dedicated architectural tools, validate the result after each write, and treat " +
            "send_code_to_revit as a LAST RESORT when no dedicated tool exists. " +
            "Every write is still executed inside Revit transactions and recorded in the audit log.";
    })
    .WithStdioServerTransport()
    .WithToolsFromAssembly();
await builder.Build().RunAsync();
