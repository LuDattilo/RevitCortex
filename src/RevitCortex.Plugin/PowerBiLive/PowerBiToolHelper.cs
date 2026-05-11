using System;
using RevitCortex.Core.Results;

namespace RevitCortex.Plugin.PowerBiLive;

/// <summary>
/// Shared utilities for all pbi_* tools.
/// </summary>
internal static class PowerBiToolHelper
{
    /// <summary>
    /// Checks AllowExternalWrites and returns a PermissionDenied failure if blocked.
    /// Returns null if writes are allowed.
    /// </summary>
    public static CortexResult<object>? CheckExternalWritesAllowed(PowerBiSettings settings)
    {
        if (!settings.AllowExternalWrites)
            return CortexResult<object>.Fail(CortexErrorCode.PermissionDenied,
                "Power BI external writes are disabled (AllowExternalWrites=false).",
                suggestion: "Set AllowExternalWrites=true in ~/.revitcortex/powerbi-live.json to enable publishing.");
        return null;
    }

    /// <summary>
    /// Runs an async factory on a dedicated background thread with no SynchronizationContext.
    /// Prevents MSAL and HttpClient continuations from marshaling back to the Revit/WPF
    /// dispatcher, which would deadlock when the caller blocks with GetAwaiter().GetResult().
    /// Re-throws any exception from the background thread preserving the original stack trace.
    /// </summary>
    public static T RunWithoutContext<T>(Func<System.Threading.Tasks.Task<T>> factory)
    {
        T result = default!;
        Exception? caught = null;

        var thread = new System.Threading.Thread(() =>
        {
            System.Threading.SynchronizationContext.SetSynchronizationContext(null);
            try
            {
                result = factory().ConfigureAwait(false).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                caught = ex;
            }
        });
        thread.IsBackground = true;
        thread.Start();
        thread.Join();

        if (caught != null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(caught).Throw();

        return result;
    }
}
