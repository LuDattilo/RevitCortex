using System.IO;

namespace RevitCortex.Tools.Dynamo.Runtime
{
    /// <summary>Resolves Dynamo-for-Revit install paths without loading any assembly.</summary>
    public static class DynamoPaths
    {
        public static string ProgramFilesAutodesk()
            => Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles),
                "Autodesk");

        public static string DynamoForRevitDir(string autodeskBase, int revitYear)
            => Path.Combine(autodeskBase, "Revit " + revitYear, "AddIns", "DynamoForRevit");
    }
}
