using System.Collections.Generic;

namespace RevitCortex.Tools.Dynamo.Building
{
    /// <summary>Everything needed to generate a Python-centric .dyn skeleton.</summary>
    public sealed class DynamoGraphSpec
    {
        public string Name { get; }
        public string PythonCode { get; }
        public IReadOnlyList<GraphPort> Inputs { get; }
        public IReadOnlyList<GraphPort> Outputs { get; }
        public string Engine { get; }

        public DynamoGraphSpec(
            string name,
            string pythonCode,
            IReadOnlyList<GraphPort> inputs,
            IReadOnlyList<GraphPort> outputs,
            string engine = "CPython3")
        {
            Name = string.IsNullOrEmpty(name) ? "RevitCortexGraph" : name;
            PythonCode = pythonCode ?? "";
            Inputs = inputs ?? new List<GraphPort>();
            Outputs = outputs ?? new List<GraphPort>();
            Engine = string.IsNullOrEmpty(engine) ? "CPython3" : engine;
        }
    }
}
