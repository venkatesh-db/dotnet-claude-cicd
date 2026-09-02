using GithubIntegration.Core.Models;

namespace GithubIntegration.Core.Triage;

public static class CiFailureTriageRules
{
    private static readonly (string Pattern, string Label)[] SignatureLabels = new (string, string)[]
    {
        ("NullReferenceException", "bug:null-reference"),
        ("Timeout", "bug:timeout"),
        ("dotnet restore", "build:dependency"),
        ("error CS", "build:compile-error"),
        ("Assert.", "test:assertion-failure"),
        ("OutOfMemory", "infra:resource-limit"),
    };

    public static IReadOnlyList<string> LabelsFor(string failureLog)
    {
        if (string.IsNullOrWhiteSpace(failureLog))
        {
            return new List<string> { "triage:needs-manual-review" };
        }

        var labels = SignatureLabels
            .Where(s => failureLog.Contains(s.Pattern, StringComparison.OrdinalIgnoreCase))
            .Select(s => s.Label)
            .Distinct()
            .ToList();

        return labels.Count > 0 ? labels : new List<string> { "triage:needs-manual-review" };
    }

    public static CiFailureTriage BuildTriage(
        string owner,
        string repo,
        long workflowRunId,
        string workflowName,
        string failureLog,
        string rootCauseSummary)
    {
        return new CiFailureTriage(
            owner,
            repo,
            workflowRunId,
            workflowName,
            rootCauseSummary,
            LabelsFor(failureLog));
    }
}
