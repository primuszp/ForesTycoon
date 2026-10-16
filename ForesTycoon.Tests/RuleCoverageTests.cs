using System.Text.Json;

namespace ForesTycoon.Tests;

public class RuleCoverageTests
{
    [Fact]
    public void EveryCatalogItemHasOneReviewedClassificationAndExistingEvidence()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder != null && !File.Exists(Path.Combine(folder.FullName, "ForesTycoon.sln"))) folder = folder.Parent;
        Assert.NotNull(folder);
        string root = folder.FullName;
        using var coverage = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, "docs/rule-coverage.json")));
        var ids = new HashSet<string>(StringComparer.Ordinal);
        string[] statuses = { "graph", "extension", "pending", "engine", "data", "mixed", "presentation" };
        foreach (var group in coverage.RootElement.GetProperty("groups").EnumerateArray()) {
            string status = group.GetProperty("status").GetString()!;
            Assert.Contains(status, statuses);
            Assert.False(string.IsNullOrWhiteSpace(group.GetProperty("editor").GetString()));
            Assert.False(string.IsNullOrWhiteSpace(group.GetProperty("remaining").GetString()));
            Assert.NotEmpty(group.GetProperty("tests").EnumerateArray());
            foreach (var test in group.GetProperty("tests").EnumerateArray()) Assert.True(File.Exists(Path.Combine(root, test.GetString()!)));
            foreach (var id in group.GetProperty("ids").EnumerateArray()) {
                Assert.True(ids.Add(id.GetString()!), "Duplicate coverage: " + id);
                var rule = CurrentGameRules.Build().Rules.Single(r => r.Id == id.GetString());
                if (status == "graph") Assert.True(rule.Execution is GameRuleExecution.EditableGraph or GameRuleExecution.EditableExpressions);
                if (status == "presentation") Assert.Equal(GameRuleExecution.Presentation, rule.Execution);
            }
        }
        Assert.Equal(CurrentGameRules.Build().Rules.Select(r => r.Id).Order(), ids.Order());
        foreach (var entry in coverage.RootElement.GetProperty("outsideCatalog").EnumerateArray()) {
            string source = File.ReadAllText(Path.Combine(root, entry.GetProperty("source").GetString()!));
            Assert.Contains(entry.GetProperty("symbol").GetString()!, source);
            foreach (var test in entry.GetProperty("tests").EnumerateArray()) Assert.True(File.Exists(Path.Combine(root, test.GetString()!)));
        }
    }
}
