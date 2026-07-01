namespace RevitCortex.Tools.Dynamo.Building
{
    /// <summary>Verified .dyn JSON schema constants (source: DynamoDS/Dynamo master).</summary>
    public static class DynJsonSchema
    {
        public const string PythonNodeConcreteType = "PythonNodeModels.PythonNode, PythonNodeModels";
        public const string PythonNodeType = "PythonScriptNode";
        public const string EngineCPython3 = "CPython3";
        public const string StringInputConcreteType = "CoreNodeModels.Input.StringInput, CoreNodeModels";
        public const string StringInputNodeType = "StringInputNode";
        public const string IntegerSliderConcreteType = "CoreNodeModels.Input.IntegerSlider, CoreNodeModels";
        public const string NumberInputNodeType = "NumberInputNode";
        public const string WatchConcreteType = "CoreNodeModels.Watch, CoreNodeModels";
        public const string WatchNodeType = "ExtensionNode";
    }
}
