using Newtonsoft.Json.Linq;
using RevitCortex.Core.Results;
using RevitCortex.Core.Security;
using RevitCortex.Core.Session;
using RevitCortex.Core.Tools;
using RevitCortex.Tools.Dynamo.Runtime;

namespace RevitCortex.Tools.Dynamo.Tools
{
    /// <summary>Reports Dynamo presence/version/engine and whether the feature is enabled. Read-only.</summary>
    public sealed class DynamoGetStatusTool : ICortexTool
    {
        public string Name => "dynamo_get_status";
        public string Category => "Dynamo";
        public bool RequiresDocument => false;
        public bool IsDynamic => true;
        public string Description => "Report Dynamo for Revit status (present, version, CPython3 availability) and whether EnableDynamo is set. Read-only diagnostic.";

        public CortexResult<object> Execute(JObject input, CortexSession session)
        {
            int year = input["revitYear"]?.Value<int>() ?? 2025;
            var caps = new DynamoCapabilityProbe().Probe(year);
            var settings = CortexSettings.Load();
            return CortexResult<object>.Ok(new
            {
                enableDynamo = settings.EnableDynamo,
                isPresent = caps.IsPresent,
                dynamoVersion = caps.DynamoVersion,
                cpython3Expected = caps.CPython3Expected,
                dynamoForRevitDir = caps.DynamoForRevitDir,
                revitYear = year
            });
        }
    }
}
