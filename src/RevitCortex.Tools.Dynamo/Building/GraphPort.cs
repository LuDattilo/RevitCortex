namespace RevitCortex.Tools.Dynamo.Building
{
    /// <summary>A typed graph input/output port (e.g. name "limit", type "Integer").</summary>
    public sealed class GraphPort
    {
        public string Name { get; }
        public string Type { get; }   // "String" | "Integer" | "Number" | "Boolean" | "Filename"

        public GraphPort(string name, string type)
        {
            Name = name ?? "";
            Type = string.IsNullOrEmpty(type) ? "String" : type;
        }
    }
}
