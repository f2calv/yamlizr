using AzurePipelinesToGitHubActionsConverter.Core.AzurePipelines;
using CasCap.Models;

namespace CasCap.Api.AzureDevOps.Tests.Unit;

/// <summary>Tests for the YAML rendering of a generated <see cref="Pipeline"/>.</summary>
[Trait("Category", "Serialization")]
public class PipelineSerializationTests
{
    [Fact]
    public void ToString_MultilineInput_UsesALiteralBlockScalar()
    {
        var pipeline = new Pipeline
        {
            steps =
            [
                new Step
                {
                    displayName = "sample step",
                    task = "SampleTask@1",
                    inputs = new Dictionary<string, string> { ["script"] = "echo one\necho two" },
                }
            ]
        };

        var yaml = pipeline.ToString();

        Assert.Contains("script: |", yaml);
        Assert.Contains("echo one", yaml);
        Assert.Contains("echo two", yaml);
        //a literal block must not be quoted or escaped back into a single line
        Assert.DoesNotContain(@"echo one\necho two", yaml);
    }

    [Fact]
    public void ToString_UnsetProperties_AreOmitted()
    {
        var pipeline = new Pipeline
        {
            steps = [new Step { displayName = "sample step", task = "SampleTask@1" }]
        };

        var yaml = pipeline.ToString();

        Assert.Contains("displayName: sample step", yaml);
        Assert.DoesNotContain("stages:", yaml);
        Assert.DoesNotContain("jobs:", yaml);
        Assert.DoesNotContain("condition:", yaml);
    }
}
