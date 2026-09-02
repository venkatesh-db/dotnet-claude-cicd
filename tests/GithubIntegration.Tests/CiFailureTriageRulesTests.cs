using GithubIntegration.Core.Triage;
using Xunit;

namespace GithubIntegration.Tests;

public class CiFailureTriageRulesTests
{
    [Fact]
    public void LabelsFor_NullReferenceLog_ReturnsBugNullReferenceLabel()
    {
        var labels = CiFailureTriageRules.LabelsFor("System.NullReferenceException: Object reference not set");

        Assert.Contains("bug:null-reference", labels);
    }

    [Fact]
    public void LabelsFor_CompileErrorLog_ReturnsBuildCompileErrorLabel()
    {
        var labels = CiFailureTriageRules.LabelsFor("Program.cs(10,5): error CS0103: The name 'x' does not exist");

        Assert.Contains("build:compile-error", labels);
    }

    [Fact]
    public void LabelsFor_UnrecognizedLog_ReturnsNeedsManualReview()
    {
        var labels = CiFailureTriageRules.LabelsFor("some unrelated log output");

        Assert.Equal(new[] { "triage:needs-manual-review" }, labels);
    }

    [Fact]
    public void LabelsFor_EmptyLog_ReturnsNeedsManualReview()
    {
        var labels = CiFailureTriageRules.LabelsFor(string.Empty);

        Assert.Equal(new[] { "triage:needs-manual-review" }, labels);
    }

    [Fact]
    public void LabelsFor_MultipleSignatures_ReturnsAllMatchingLabels()
    {
        var labels = CiFailureTriageRules.LabelsFor("error CS0103 followed by a Timeout waiting for response");

        Assert.Contains("build:compile-error", labels);
        Assert.Contains("bug:timeout", labels);
        Assert.Equal(2, labels.Count);
    }

    [Fact]
    public void BuildTriage_ReturnsRecordWithComputedLabels()
    {
        var triage = CiFailureTriageRules.BuildTriage(
            "venkatesh-db",
            "dotnet-claude-cicd",
            12345,
            "CI",
            "error CS0103: name does not exist",
            "Compilation failed due to an undefined symbol.");

        Assert.Equal("venkatesh-db", triage.RepoOwner);
        Assert.Equal(12345, triage.WorkflowRunId);
        Assert.Contains("build:compile-error", triage.Labels);
    }
}
