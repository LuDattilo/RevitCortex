using System;
using System.IO;
using System.Reflection;

namespace RevitCortex.Tools.Dynamo.Runtime
{
    /// <summary>
    /// Lazily loads Dynamo assemblies via reflection from the Revit install path.
    /// No compile-time dependency on Dynamo. Any failure is returned to the caller,
    /// never allowed to propagate to plugin load.
    /// </summary>
    public sealed class DynamoRuntimeLoader
    {
        private readonly string _dynamoForRevitDir;
        private bool _resolverHooked;

        public DynamoRuntimeLoader(string dynamoForRevitDir)
        {
            _dynamoForRevitDir = dynamoForRevitDir ?? "";
        }

        /// <summary>Returns null on success, or an error message string on failure.</summary>
        public string? EnsureLoaded()
        {
            try
            {
                if (string.IsNullOrEmpty(_dynamoForRevitDir) || !Directory.Exists(_dynamoForRevitDir))
                    return "Dynamo for Revit folder not found: " + _dynamoForRevitDir;

                HookResolver();

                var revitDs = Path.Combine(_dynamoForRevitDir, "Revit", "DynamoRevitDS.dll");
                if (!File.Exists(revitDs))
                    revitDs = Path.Combine(_dynamoForRevitDir, "DynamoRevitDS.dll");
                if (!File.Exists(revitDs))
                    return "DynamoRevitDS.dll not found under " + _dynamoForRevitDir;

                Assembly.LoadFrom(revitDs);
                return null;
            }
            catch (Exception ex)
            {
                return "Failed to load Dynamo runtime: " + ex.Message;
            }
        }

        private void HookResolver()
        {
            if (_resolverHooked) return;
            AppDomain.CurrentDomain.AssemblyResolve += ResolveFromDynamoDir;
            _resolverHooked = true;
        }

        private Assembly? ResolveFromDynamoDir(object? sender, ResolveEventArgs args)
        {
            try
            {
                var simpleName = new AssemblyName(args.Name).Name + ".dll";
                foreach (var root in new[] { _dynamoForRevitDir, Path.Combine(_dynamoForRevitDir, "Revit") })
                {
                    var candidate = Path.Combine(root, simpleName);
                    if (File.Exists(candidate)) return Assembly.LoadFrom(candidate);
                }
            }
            catch { }
            return null;
        }
    }
}
