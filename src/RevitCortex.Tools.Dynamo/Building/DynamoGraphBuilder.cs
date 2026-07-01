using System;
using System.Collections.Generic;

namespace RevitCortex.Tools.Dynamo.Building
{
    /// <summary>
    /// Deterministically builds a valid Python-centric .dyn JSON document.
    /// Never loads any Dynamo DLL — pure string/JSON construction.
    /// </summary>
    public sealed class DynamoGraphBuilder
    {
        private static readonly HashSet<string> AllowedTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "String", "Integer", "Number", "Boolean", "Filename"
        };

        public DynamoValidationResult ValidateSpec(DynamoGraphSpec spec)
        {
            var errors = new List<string>();
            if (spec == null) return DynamoValidationResult.Fail("spec is null");

            if (string.IsNullOrWhiteSpace(spec.PythonCode))
                errors.Add("pythonCode is empty");

            CheckPorts("input", spec.Inputs, errors);
            CheckPorts("output", spec.Outputs, errors);

            return errors.Count == 0
                ? DynamoValidationResult.Ok()
                : DynamoValidationResult.Fail(errors.ToArray());
        }

        private static void CheckPorts(string kind, IReadOnlyList<GraphPort> ports, List<string> errors)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var p in ports)
            {
                if (string.IsNullOrWhiteSpace(p.Name))
                    errors.Add($"{kind} port has empty name");
                else if (!seen.Add(p.Name))
                    errors.Add($"duplicate {kind} port name: {p.Name}");
                if (!AllowedTypes.Contains(p.Type))
                    errors.Add($"unknown {kind} port type: {p.Type}");
            }
        }

        // Implemented in Task 6.
        public string BuildDynJson(DynamoGraphSpec spec)
        {
            throw new NotImplementedException("BuildDynJson is implemented in Task 6");
        }
    }
}
