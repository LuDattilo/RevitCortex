using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using RiveTT.Core.Results;
using Newtonsoft.Json.Linq;

namespace RiveTT.Tools.Utilities;

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
        => SuppressWarnings(tx, null);

    public static FailureCapture SuppressWarnings(Transaction tx,
        ISet<string>? allowedWarningIds)
    {
        var capture = new FailureCapture(allowedWarningIds);
        var options = tx.GetFailureHandlingOptions();
        options.SetFailuresPreprocessor(capture);
        options.SetClearAfterRollback(true);
        tx.SetFailureHandlingOptions(options);
        return capture;
    }

    public static FailureCapture FromInput(Transaction tx, JObject input)
    {
        var policy = (input["warningPolicy"]?.Value<string>() ?? "suppress_all").ToLowerInvariant();
        if (policy == "suppress_all") return SuppressWarnings(tx);
        if (policy != "allow_list")
            throw new System.ArgumentException("warningPolicy must be suppress_all or allow_list");
        var allowed = input["allowedWarningIds"]?.ToObject<HashSet<string>>()
            ?? new HashSet<string>();
        return SuppressWarnings(tx, allowed);
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

    public static CortexResult<object> ToFailure(
        FailureCapture capture, string message, string repairHint)
    {
        return CortexResult<object>.Fail(
            CortexErrorCode.TransactionFailed,
            $"{message}: {Describe(capture)}",
            suggestion: repairHint,
            context: new Dictionary<string, object>
            {
                ["warnings"] = capture.Warnings.ToArray(),
                ["errors"] = capture.Errors.ToArray(),
                ["rolledBack"] = true,
                ["failedElementIds"] = capture.FailedElementIds.OrderBy(id => id).ToArray(),
                ["repairHints"] = new[] { repairHint },
                ["warningsSuppressed"] = capture.WarningsSuppressed
            });
    }

    public sealed class FailureCapture : IFailuresPreprocessor
    {
        private readonly ISet<string>? _allowedWarningIds;
        public FailureCapture(ISet<string>? allowedWarningIds = null)
        {
            _allowedWarningIds = allowedWarningIds;
        }

        public List<string> Errors { get; } = new List<string>();
        public List<string> Warnings { get; } = new List<string>();
        public HashSet<long> FailedElementIds { get; } = new HashSet<long>();
        public int WarningsSuppressed { get; private set; }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            var hasError = false;
            foreach (var failure in failuresAccessor.GetFailureMessages())
            {
                var severity = failure.GetSeverity();
                if (severity == FailureSeverity.Warning)
                {
                    Warnings.Add(failure.GetDescriptionText());
                    CaptureIds(failure);
                    var failureId = failure.GetFailureDefinitionId().Guid.ToString("D");
                    if (_allowedWarningIds == null || _allowedWarningIds.Contains(failureId))
                    {
                        WarningsSuppressed++;
                        failuresAccessor.DeleteWarning(failure);
                    }
                    else
                    {
                        hasError = true;
                        Errors.Add($"Unapproved warning {failureId}: {failure.GetDescriptionText()}");
                    }
                }
                else if (severity == FailureSeverity.Error
                         || severity == FailureSeverity.DocumentCorruption)
                {
                    hasError = true;
                    Errors.Add(failure.GetDescriptionText());
                    CaptureIds(failure);
                }
            }

            return hasError
                ? FailureProcessingResult.ProceedWithRollBack
                : FailureProcessingResult.Continue;
        }

        private void CaptureIds(FailureMessageAccessor failure)
        {
            foreach (var id in failure.GetFailingElementIds())
                FailedElementIds.Add(ToolHelpers.GetElementIdValue(id));
            foreach (var id in failure.GetAdditionalElementIds())
                FailedElementIds.Add(ToolHelpers.GetElementIdValue(id));
        }
    }
}
