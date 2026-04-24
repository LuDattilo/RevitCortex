using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using RevitCortex.Server.Connection;
using RevitCortex.Server.Tools;
using Xunit;

namespace RevitCortex.Tests.Server
{
    public class ServerToolContractTests
    {
        private static MethodInfo GetMethod(Type declaringType, string methodName)
        {
            return Assert.Single(declaringType.GetMethods(BindingFlags.Public | BindingFlags.Static), m => m.Name == methodName);
        }

        private static ParameterInfo GetParameter(MethodInfo method, string parameterName)
        {
            return Assert.Single(method.GetParameters(), p => p.Name == parameterName);
        }

        private static void AssertDescription(MethodInfo method, string expected)
        {
            var attribute = method.GetCustomAttribute<DescriptionAttribute>();
            Assert.NotNull(attribute);
            Assert.Equal(expected, attribute!.Description);
        }

        [Fact]
        public void GetCurrentViewElements_ExposesExplicitCategoryLists()
        {
            var method = GetMethod(typeof(ViewTools), nameof(ViewTools.GetCurrentViewElements));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("limit", name),
                name => Assert.Equal("modelCategoryList", name),
                name => Assert.Equal("annotationCategoryList", name),
                name => Assert.Equal("categoryFilter", name),
                name => Assert.Equal("fields", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(RevitConnectionManager), GetParameter(method, "revit").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "limit").ParameterType);
            Assert.Equal(typeof(string[]), GetParameter(method, "modelCategoryList").ParameterType);
            Assert.Equal(typeof(string[]), GetParameter(method, "annotationCategoryList").ParameterType);
            Assert.Equal(typeof(string), GetParameter(method, "categoryFilter").ParameterType);
            Assert.Equal(typeof(string[]), GetParameter(method, "fields").ParameterType);
            Assert.Equal(typeof(CancellationToken), GetParameter(method, "ct").ParameterType);

            Assert.True(GetParameter(method, "modelCategoryList").HasDefaultValue);
            Assert.Null(GetParameter(method, "modelCategoryList").DefaultValue);
            Assert.True(GetParameter(method, "annotationCategoryList").HasDefaultValue);
            Assert.Null(GetParameter(method, "annotationCategoryList").DefaultValue);
            Assert.True(GetParameter(method, "categoryFilter").HasDefaultValue);
            Assert.Null(GetParameter(method, "categoryFilter").DefaultValue);

            AssertDescription(method, "List elements visible in the currently active view.");
        }

        [Fact]
        public void GetScheduleData_ExposesMaxRows()
        {
            var method = GetMethod(typeof(ViewTools), nameof(ViewTools.GetScheduleData));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("scheduleId", name),
                name => Assert.Equal("maxRows", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(long), GetParameter(method, "scheduleId").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "maxRows").ParameterType);
            Assert.True(GetParameter(method, "maxRows").HasDefaultValue);
            Assert.Null(GetParameter(method, "maxRows").DefaultValue);

            AssertDescription(method, "Export schedule data as JSON from an existing schedule view.");
        }

        [Fact]
        public void WorkflowModelAudit_ExposesStructuredAuditFlags()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.WorkflowModelAudit));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("includeWarnings", name),
                name => Assert.Equal("includeFamilies", name),
                name => Assert.Equal("maxWarnings", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(bool?), GetParameter(method, "includeWarnings").ParameterType);
            Assert.Equal(typeof(bool?), GetParameter(method, "includeFamilies").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "maxWarnings").ParameterType);

            Assert.True(GetParameter(method, "includeWarnings").HasDefaultValue);
            Assert.Null(GetParameter(method, "includeWarnings").DefaultValue);
            Assert.True(GetParameter(method, "includeFamilies").HasDefaultValue);
            Assert.Null(GetParameter(method, "includeFamilies").DefaultValue);
            Assert.True(GetParameter(method, "maxWarnings").HasDefaultValue);
            Assert.Null(GetParameter(method, "maxWarnings").DefaultValue);

            AssertDescription(method, "Run a complete model audit workflow.");
        }

        [Fact]
        public void HighCostWrappers_ExposeCompactFlags()
        {
            var familyTypes = GetMethod(typeof(ProjectTools), nameof(ProjectTools.GetAvailableFamilyTypes));
            var schedulableFields = GetMethod(typeof(ProjectTools), nameof(ProjectTools.ListSchedulableFields));
            var roomOpenings = GetMethod(typeof(ElementTools), nameof(ElementTools.GetRoomOpenings));

            Assert.Contains("compact", familyTypes.GetParameters().Select(p => p.Name));
            Assert.Contains("compact", schedulableFields.GetParameters().Select(p => p.Name));
            Assert.Contains("summaryOnly", schedulableFields.GetParameters().Select(p => p.Name));
            Assert.Contains("compact", roomOpenings.GetParameters().Select(p => p.Name));
            Assert.Contains("summaryOnly", roomOpenings.GetParameters().Select(p => p.Name));
        }
    }
}
