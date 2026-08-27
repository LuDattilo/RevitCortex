using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using RiveTT.Server.Connection;
using RiveTT.Server.Tools;
using Xunit;

namespace RiveTT.Tests.Server
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
                name => Assert.Equal("pageSize", name),
                name => Assert.Equal("limit", name),
                name => Assert.Equal("cursor", name),
                name => Assert.Equal("modelCategoryList", name),
                name => Assert.Equal("annotationCategoryList", name),
                name => Assert.Equal("categoryFilter", name),
                name => Assert.Equal("fields", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(RevitConnectionManager), GetParameter(method, "revit").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "pageSize").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "limit").ParameterType);
            Assert.Equal(typeof(string), GetParameter(method, "cursor").ParameterType);
            Assert.Equal(typeof(JsonElement?), GetParameter(method, "modelCategoryList").ParameterType);
            Assert.Equal(typeof(JsonElement?), GetParameter(method, "annotationCategoryList").ParameterType);
            Assert.Equal(typeof(string), GetParameter(method, "categoryFilter").ParameterType);
            Assert.Equal(typeof(JsonElement?), GetParameter(method, "fields").ParameterType);
            Assert.Equal(typeof(CancellationToken), GetParameter(method, "ct").ParameterType);

            Assert.True(GetParameter(method, "modelCategoryList").HasDefaultValue);
            Assert.Null(GetParameter(method, "modelCategoryList").DefaultValue);
            Assert.True(GetParameter(method, "annotationCategoryList").HasDefaultValue);
            Assert.Null(GetParameter(method, "annotationCategoryList").DefaultValue);
            Assert.True(GetParameter(method, "categoryFilter").HasDefaultValue);
            Assert.Null(GetParameter(method, "categoryFilter").DefaultValue);

            AssertDescription(method,
                "List elements visible in the currently active view. categoryFilter is a single-category " +
                "shortcut (OST code, English name or localized label); modelCategoryList/" +
                "annotationCategoryList take several. Pages via pageSize/cursor: nextCursor in the response, " +
                "passed back as cursor, reaches elements beyond the first page.");
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
                name => Assert.Equal("includeAvailableFields", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(long), GetParameter(method, "scheduleId").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "maxRows").ParameterType);
            Assert.True(GetParameter(method, "maxRows").HasDefaultValue);
            Assert.Null(GetParameter(method, "maxRows").DefaultValue);

            // availableFields is opt-in: it lists every schedulable parameter of the
            // project and blew past the client output limit on a 10-row request.
            Assert.Equal(typeof(bool), GetParameter(method, "includeAvailableFields").ParameterType);
            Assert.True(GetParameter(method, "includeAvailableFields").HasDefaultValue);
            Assert.IsType<bool>(GetParameter(method, "includeAvailableFields").DefaultValue);

            AssertDescription(method,
                "Export schedule data as JSON from an existing schedule view. availableFields is omitted " +
                "unless includeAvailableFields=true: it lists every schedulable parameter of the project " +
                "and used to dwarf a 10-row request.");
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
                name => Assert.Equal("compact", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(bool), GetParameter(method, "includeWarnings").ParameterType);
            Assert.Equal(typeof(bool), GetParameter(method, "includeFamilies").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "maxWarnings").ParameterType);

            Assert.True(GetParameter(method, "includeWarnings").HasDefaultValue);
            Assert.IsType<bool>(GetParameter(method, "includeWarnings").DefaultValue);
            Assert.True(GetParameter(method, "includeFamilies").HasDefaultValue);
            Assert.IsType<bool>(GetParameter(method, "includeFamilies").DefaultValue);
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

        [Fact]
        public void GetElementParameters_ExposesCompactFlag()
        {
            var method = GetMethod(typeof(ElementTools), nameof(ElementTools.GetElementParameters));
            var parameters = method.GetParameters();

            Assert.Contains("compact", parameters.Select(p => p.Name));
            Assert.Equal(typeof(bool), GetParameter(method, "compact").ParameterType);
            Assert.True(GetParameter(method, "compact").HasDefaultValue);
            Assert.Equal(false, GetParameter(method, "compact").DefaultValue);
        }

        [Fact]
        public void GetElementSolidGeometry_ExposesElementIdAndMaxSolids()
        {
            var method = GetMethod(typeof(ElementTools), nameof(ElementTools.GetElementSolidGeometry));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("elementId", name),
                name => Assert.Equal("maxSolids", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(RevitConnectionManager), GetParameter(method, "revit").ParameterType);
            Assert.Equal(typeof(long), GetParameter(method, "elementId").ParameterType);
            Assert.Equal(typeof(int), GetParameter(method, "maxSolids").ParameterType);
            Assert.Equal(typeof(CancellationToken), GetParameter(method, "ct").ParameterType);

            Assert.True(GetParameter(method, "maxSolids").HasDefaultValue);
            Assert.Equal(20, GetParameter(method, "maxSolids").DefaultValue);

            Assert.NotNull(method.GetCustomAttribute<DescriptionAttribute>());
        }

        [Fact]
        public void ResolveElementsByUniqueId_ExposesBatchUniqueIds()
        {
            var method = GetMethod(typeof(ElementTools), nameof(ElementTools.ResolveElementsByUniqueId));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("uniqueIds", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(string[]), GetParameter(method, "uniqueIds").ParameterType);
            AssertDescription(method, "Resolve Revit UniqueId strings to ElementId records for cross-app workflows.");
        }

        [Fact]
        public void ShowCrossModelElements_ExposesHostAndLinkedTargets()
        {
            var method = GetMethod(typeof(LinkTools), nameof(LinkTools.ShowCrossModelElements));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("hostElementIds", name),
                name => Assert.Equal("linkedElements", name),
                name => Assert.Equal("select", name),
                name => Assert.Equal("isolate", name),
                name => Assert.Equal("createSectionBox", name),
                name => Assert.Equal("createLinkedMarkers", name),
                name => Assert.Equal("usePostCommandIsolate", name),
                name => Assert.Equal("offset", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(JsonElement?), GetParameter(method, "hostElementIds").ParameterType);
            Assert.Equal(typeof(JsonElement?), GetParameter(method, "linkedElements").ParameterType);
            Assert.Equal(typeof(bool), GetParameter(method, "select").ParameterType);
            Assert.Equal(typeof(bool), GetParameter(method, "isolate").ParameterType);
            Assert.Equal(typeof(bool), GetParameter(method, "createSectionBox").ParameterType);
            Assert.Equal(typeof(bool), GetParameter(method, "createLinkedMarkers").ParameterType);
            Assert.Equal(typeof(bool), GetParameter(method, "usePostCommandIsolate").ParameterType);
            Assert.Equal(typeof(double?), GetParameter(method, "offset").ParameterType);
        }

        [Fact]
        public void AuditFamilies_ExposesCompactFlag()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.AuditFamilies));
            var parameters = method.GetParameters();

            Assert.Contains("compact", parameters.Select(p => p.Name));
            Assert.Equal(typeof(bool), GetParameter(method, "compact").ParameterType);
            Assert.True(GetParameter(method, "compact").HasDefaultValue);
            Assert.Equal(false, GetParameter(method, "compact").DefaultValue);
        }

        [Fact]
        public void GetAvailableFamilyTypes_ExposesToolNativeParameters()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.GetAvailableFamilyTypes));
            var parameters = method.GetParameters();

            Assert.Collection(
                parameters.Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("categoryList", name),
                name => Assert.Equal("familyNameFilter", name),
                name => Assert.Equal("limit", name),
                name => Assert.Equal("compact", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(JsonElement?), GetParameter(method, "categoryList").ParameterType);
            Assert.Equal(typeof(string), GetParameter(method, "familyNameFilter").ParameterType);
            Assert.Equal(typeof(int?), GetParameter(method, "limit").ParameterType);

            Assert.True(GetParameter(method, "categoryList").HasDefaultValue);
            Assert.Null(GetParameter(method, "categoryList").DefaultValue);
            Assert.True(GetParameter(method, "familyNameFilter").HasDefaultValue);
            Assert.Null(GetParameter(method, "familyNameFilter").DefaultValue);
            Assert.True(GetParameter(method, "limit").HasDefaultValue);
            Assert.Null(GetParameter(method, "limit").DefaultValue);
        }

        [Fact]
        public void ModifySchedule_DescribesPluginNativeSortingActions()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.ModifySchedule));
            var action = GetParameter(method, "action");

            var description = action.GetCustomAttribute<DescriptionAttribute>()?.Description;
            Assert.NotNull(description);
            Assert.Contains("set_sorting", description);
            Assert.Contains("clear_sorting", description);
            Assert.Contains("set_filter", description);
            Assert.Contains("clear_filter", description);
            Assert.DoesNotContain("set_sort |", description);

            AssertDescription(method,
                "Modify schedule fields, sorting, filters, or rename the schedule. Supported actions: add_field, remove_field, set_sorting, clear_sorting, set_filter, clear_filter, rename.");
        }

        // P4.1 in PLAN_CORRECTION.md: open_family/open_template/close_document/edit_family.

        [Fact]
        public void OpenFamily_TakesAFilePathAndDryRun()
        {
            var method = GetMethod(typeof(DocumentTools), nameof(DocumentTools.OpenFamily));

            Assert.Collection(
                method.GetParameters().Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("filePath", name),
                name => Assert.Equal("dryRun", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(string), GetParameter(method, "filePath").ParameterType);
            Assert.False(GetParameter(method, "filePath").HasDefaultValue);
            Assert.Equal(typeof(bool), GetParameter(method, "dryRun").ParameterType);
            Assert.Equal(true, GetParameter(method, "dryRun").DefaultValue);
        }

        [Fact]
        public void OpenTemplate_TakesAFilePathAndDryRun()
        {
            var method = GetMethod(typeof(DocumentTools), nameof(DocumentTools.OpenTemplate));

            Assert.Collection(
                method.GetParameters().Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("filePath", name),
                name => Assert.Equal("dryRun", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(string), GetParameter(method, "filePath").ParameterType);
            Assert.False(GetParameter(method, "filePath").HasDefaultValue);
        }

        [Fact]
        public void CloseDocument_FilePathIsOptional_DefaultsToTheActiveDocument()
        {
            var method = GetMethod(typeof(DocumentTools), nameof(DocumentTools.CloseDocument));

            Assert.Collection(
                method.GetParameters().Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("filePath", name),
                name => Assert.Equal("saveModified", name),
                name => Assert.Equal("dryRun", name),
                name => Assert.Equal("ct", name));

            Assert.True(GetParameter(method, "filePath").HasDefaultValue);
            Assert.Null(GetParameter(method, "filePath").DefaultValue);
            Assert.Equal(false, GetParameter(method, "saveModified").DefaultValue);
            Assert.Equal(true, GetParameter(method, "dryRun").DefaultValue);
        }

        [Fact]
        public void EditFamily_TakesFamilyIdOrNameAndAChangesArray()
        {
            var method = GetMethod(typeof(ProjectTools), nameof(ProjectTools.EditFamily));

            Assert.Collection(
                method.GetParameters().Select(p => p.Name),
                name => Assert.Equal("revit", name),
                name => Assert.Equal("familyId", name),
                name => Assert.Equal("familyName", name),
                name => Assert.Equal("changes", name),
                name => Assert.Equal("dryRun", name),
                name => Assert.Equal("ct", name));

            Assert.Equal(typeof(long?), GetParameter(method, "familyId").ParameterType);
            Assert.Equal(typeof(string), GetParameter(method, "familyName").ParameterType);
            Assert.Equal(typeof(JsonElement?), GetParameter(method, "changes").ParameterType);
            Assert.True(GetParameter(method, "changes").HasDefaultValue);
        }
    }
}
