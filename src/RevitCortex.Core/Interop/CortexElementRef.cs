namespace RevitCortex.Core.Interop
{
    /// <summary>
    /// Minimal cross-application element identity contract shared by
    /// RevitCortex and NavisCortex responses.
    /// </summary>
    public class CortexElementRef
    {
        public string? SourceApp { get; set; }
        public string? SourceFile { get; set; }
        public string? NavisInstanceGuid { get; set; }
        public string? RevitElementId { get; set; }
        public string? RevitUniqueId { get; set; }
        public string? IfcGuid { get; set; }
        public string? Category { get; set; }
        public string? Family { get; set; }
        public string? Type { get; set; }
    }
}
