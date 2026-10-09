using CasCap.Models;
using System.Text.Json;

namespace CasCap.Api.AzureDevOps.Tests.Unit;

/// <summary>Tests for the Azure DevOps task-catalog JSON contract.</summary>
[Trait("Category", "Serialization")]
public class TaskCatalogSerializationTests
{
    [Fact]
    public void Serialize_RenamedProperties_PreserveAzureDevOpsPropertyNames()
    {
        var catalogue = new Tasks
        {
            Value =
            [
                new TaskObj
                {
                    InputMap = [],
                    Inputs = [new TaskInput()],
                    Version = new TaskVersion(),
                }
            ],
        };

        using var document = JsonDocument.Parse(JsonSerializer.Serialize(catalogue));
        var root = document.RootElement;
        var task = root.GetProperty("value")[0];
        var input = task.GetProperty("inputs")[0];
        var version = task.GetProperty("version");

        Assert.Equal(["count", "value"], PropertyNames(root));
        Assert.Equal(
            [
                "contentsUploaded", "deprecated", "preview", "serverOwned", "showEnvironmentVariables",
                "id", "demands", "runsOn", "satisfies", "visibility", "inputs", "author", "category",
                "contributionIdentifier", "contributionVersion", "definitionType", "description", "friendlyName",
                "helpMarkDown", "helpUrl", "iconUrl", "instanceNameFormat", "minimumAgentVersion", "name",
                "releaseNotes", "version",
            ],
            PropertyNames(task));
        Assert.Equal(
            [
                "required", "options", "aliases", "defaultValue", "groupName", "helpMarkDown", "label", "name",
                "type", "visibleRule",
            ],
            PropertyNames(input));
        Assert.Equal(["major", "minor", "patch"], PropertyNames(version));
        Assert.False(task.TryGetProperty("InputMap", out _));
    }

    private static string[] PropertyNames(JsonElement element)
        => [.. element.EnumerateObject().Select(property => property.Name)];
}