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
        // The AssemblyResolve handler is process-wide, so it must be hooked exactly once
        // for the whole AppDomain. Subscribing per instance leaks handlers across runs.
        private static readonly object _resolverLock = new object();
        private static bool _staticResolverHooked;
        private static string _resolveDir = "";

        private readonly string _dynamoForRevitDir;

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
            lock (_resolverLock)
            {
                // Always point the resolver at this loader's dir (latest wins).
                _resolveDir = _dynamoForRevitDir;

                if (_staticResolverHooked) return;
                AppDomain.CurrentDomain.AssemblyResolve += ResolveFromDynamoDir;
                _staticResolverHooked = true;
            }
        }

        private static Assembly? ResolveFromDynamoDir(object? sender, ResolveEventArgs args)
        {
            try
            {
                var dir = _resolveDir;
                if (string.IsNullOrEmpty(dir)) return null;

                var simpleName = new AssemblyName(args.Name).Name + ".dll";
                foreach (var root in new[] { dir, Path.Combine(dir, "Revit") })
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
