using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace RevitCortex.Tools.Utilities;

/// <summary>
/// Centralized failure handling for tool transactions. Without a preprocessor,
/// any Revit warning raised during Commit() opens a modal TaskDialog on the UI
/// thread, freezing the MCP bridge until a human clicks it. SuppressWarnings
/// installs a preprocessor that deletes warnings and rolls the transaction back
/// on errors — both without UI. After Commit(), callers must check the returned
/// TransactionStatus: a rolled-back commit surfaces the captured errors as a
/// structured failure instead of a silent success.
/// </summary>
public static class TransactionFailureHandling
{
    /// <summary>
    /// Installs the warning-suppressing preprocessor on the transaction.
    /// Call after creating the transaction (before or after Start()).
    /// Returns the capture object: after a Commit() that does not return
    /// TransactionStatus.Committed, <see cref="FailureCapture.Errors"/> holds
    /// the Revit error descriptions for the Fail message.
    /// </summary>
    public static FailureCapture SuppressWarnings(Transaction tx)
    {
        var capture = new FailureCapture();
        var options = tx.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(capture);
        options.SetClearAfterRollback(true);
        tx.SetFailureHandlingOptions(options);
        return capture;
    }

    /// <summary>Compact "; "-joined summary of captured errors for Fail messages.</summary>
    public static string Describe(FailureCapture capture)
    {
        if (capture.Errors.Count == 0)
            return "Revit rolled back the transaction (no error description available)";
        var take = capture.Errors.Count > 3 ? 3 : capture.Errors.Count;
        var head = string.Join("; ", capture.Errors.GetRange(0, take));
        return capture.Errors.Count > take ? head + $"; (+{capture.Errors.Count - take} more)" : head;
    }

    public sealed class FailureCapture : IFailuresPreprocessor
    {
        public List<string> Errors { get; } = new List<string>();
        public int WarningsSuppressed { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var hasError = false;
            foreach (var failure in failuresAccessor.GetFailureMessages())
            {
                var severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    WarningsSuppressed++;
                    failuresAccessor.DeleteWarning(failure);
                }
                else if (severity == FailureSeverity.Error
                         || severity == FailureSeverity.DocumentCorruption)
                {
                    hasError = true;
                    Errors.Add(failure.GetDescriptionText());
                }
            }

            return hasError
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }
    }
}
