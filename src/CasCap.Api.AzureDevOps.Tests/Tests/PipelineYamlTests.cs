using AzurePipelinesToGitHubActionsConverter.Core.AzurePipelines;
using CasCap.Models;
using Xunit;

namespace CasCap.Api.AzureDevOps.Tests;

public class PipelineYamlTests
{
    [Fact]
    public void Pipeline_WithSingleStep_YamlContainsStepDetails()
    {
        var pipeline = new Pipeline
        {
            steps = new[]
            {
                new Step { task = "DotNetCoreCLI@2", displayName = "Build project" }
            }
        };

        var yaml = pipeline.ToString();

        Assert.Contains("steps:", yaml);
        Assert.Contains("DotNetCoreCLI@2", yaml);
        Assert.Contains("Build project", yaml);
    }

    [Fact]
    public void Pipeline_WithNamedVariable_YamlContainsVariable()
    {
        var pipeline = new Pipeline
        {
            variables = new List<Variable>
            {
                new Variable { name = "buildConfiguration", value = "Release" }
            },
            steps = new[] { new Step { task = "DotNetCoreCLI@2" } }
        };

        var yaml = pipeline.ToString();

        Assert.Contains("variables:", yaml);
        Assert.Contains("buildConfiguration", yaml);
        Assert.Contains("Release", yaml);
    }

    [Fact]
    public void Pipeline_WithVariableGroup_YamlContainsGroup()
    {
        var pipeline = new Pipeline
        {
            variables = new List<Variable>
            {
                new Variable { group = "MyVariableGroup" }
            },
            steps = new[] { new Step { task = "DotNetCoreCLI@2" } }
        };

        var yaml = pipeline.ToString();

        Assert.Contains("variables:", yaml);
        Assert.Contains("group:", yaml);
        Assert.Contains("MyVariableGroup", yaml);
    }

    [Fact]
    public void Pipeline_WithPool_YamlContainsPoolName()
    {
        var pipeline = new Pipeline
        {
            pool = new Pool { name = "ubuntu-latest" },
            steps = new[] { new Step { task = "DotNetCoreCLI@2" } }
        };

        var yaml = pipeline.ToString();

        Assert.Contains("pool:", yaml);
        Assert.Contains("ubuntu-latest", yaml);
    }

    [Fact]
    public void Pipeline_WithMultilineScriptInput_UsesLiteralBlockStyle()
    {
        var pipeline = new Pipeline
        {
            steps = new[]
            {
                new Step
                {
                    task = "CmdLine@2",
                    inputs = new Dictionary<string, string>
                    {
                        { "script", "echo line1\necho line2" }
                    }
                }
            }
        };

        var yaml = pipeline.ToString();

        // LiteralMultilineEventEmitter sets ScalarStyle.Literal for multiline strings
        // YamlDotNet renders strings without trailing newlines as |-
        Assert.Contains("|-", yaml);
        Assert.Contains("echo line1", yaml);
        Assert.Contains("echo line2", yaml);
    }

    [Fact]
    public void Pipeline_DefaultBoolValues_OmittedFromYaml()
    {
        var pipeline = new Pipeline
        {
            steps = new[]
            {
                new Step
                {
                    task = "DotNetCoreCLI@2",
                    continueOnError = false, // default value, should be omitted
                    timeoutInMinutes = 0     // default value, should be omitted
                }
            }
        };

        var yaml = pipeline.ToString();

        Assert.DoesNotContain("continueOnError", yaml);
        Assert.DoesNotContain("timeoutInMinutes", yaml);
    }

    [Fact]
    public void Pipeline_WithTriggerBranches_YamlContainsTrigger()
    {
        var pipeline = new Pipeline
        {
            trigger = new TriggerAzDO
            {
                branches = new IncludeExclude
                {
                    include = new[] { "main", "develop" }
                }
            },
            steps = new[] { new Step { task = "DotNetCoreCLI@2" } }
        };

        var yaml = pipeline.ToString();

        Assert.Contains("trigger:", yaml);
        Assert.Contains("branches:", yaml);
        Assert.Contains("main", yaml);
        Assert.Contains("develop", yaml);
    }

    [Fact]
    public void Pipeline_WithMultipleSteps_YamlContainsAllSteps()
    {
        var pipeline = new Pipeline
        {
            steps = new[]
            {
                new Step { task = "DotNetCoreCLI@2", displayName = "Restore" },
                new Step { task = "DotNetCoreCLI@2", displayName = "Build" },
                new Step { task = "DotNetCoreCLI@2", displayName = "Test" }
            }
        };

        var yaml = pipeline.ToString();

        Assert.Contains("Restore", yaml);
        Assert.Contains("Build", yaml);
        Assert.Contains("Test", yaml);
    }

    [Fact]
    public void Pipeline_WithStageJobsAndSteps_YamlContainsAllLevels()
    {
        var pipeline = new Pipeline
        {
            stages = new[]
            {
                new StageAzDO
                {
                    stage = "Build",
                    displayName = "Build Stage",
                    jobs = new[]
                    {
                        new Job
                        {
                            job = "BuildJob",
                            displayName = "Build Job",
                            steps = new[]
                            {
                                new Step { task = "DotNetCoreCLI@2", displayName = "Build project" }
                            }
                        }
                    }
                }
            }
        };

        var yaml = pipeline.ToString();

        Assert.Contains("stages:", yaml);
        Assert.Contains("Build", yaml);
        Assert.Contains("jobs:", yaml);
        Assert.Contains("BuildJob", yaml);
        Assert.Contains("steps:", yaml);
        Assert.Contains("DotNetCoreCLI@2", yaml);
    }

    [Fact]
    public void Pipeline_WithStepEnvironmentVariables_YamlContainsEnvVars()
    {
        var pipeline = new Pipeline
        {
            steps = new[]
            {
                new Step
                {
                    task = "DotNetCoreCLI@2",
                    env = new Dictionary<string, string>
                    {
                        { "MY_ENV_VAR", "my_value" }
                    }
                }
            }
        };

        var yaml = pipeline.ToString();

        Assert.Contains("env:", yaml);
        Assert.Contains("MY_ENV_VAR", yaml);
        Assert.Contains("my_value", yaml);
    }

    [Fact]
    public void Pipeline_WithContinueOnError_YamlContainsFlag()
    {
        var pipeline = new Pipeline
        {
            steps = new[]
            {
                new Step
                {
                    task = "DotNetCoreCLI@2",
                    continueOnError = true
                }
            }
        };

        var yaml = pipeline.ToString();

        Assert.Contains("continueOnError: true", yaml);
    }

    [Fact]
    public void Pipeline_WithNonDefaultCondition_YamlContainsCondition()
    {
        var pipeline = new Pipeline
        {
            steps = new[]
            {
                new Step
                {
                    task = "DotNetCoreCLI@2",
                    condition = "always()"
                }
            }
        };

        var yaml = pipeline.ToString();

        Assert.Contains("condition:", yaml);
        Assert.Contains("always()", yaml);
    }
}
