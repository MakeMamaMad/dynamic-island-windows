using System.IO;
using System.Text.Json;
using Xunit;

namespace DynamicIsland.Tests;

public class WorkspaceSnapshotTests
{
    private static readonly string[] Providers = ["codex", "claude_code", "deepseek_api", "grok_api", "gemini_free", "openrouter_free"];

    private static object Snapshot(DateTimeOffset time, bool online = true, bool running = false, bool complete = false, int schema = 1)
    {
        var active = running ? new object[] { new { id = "r-test", project_id = "p-test", task_id = "t-test",
            provider = "gemini_free", status = "running", started_at = time.AddSeconds(-6),
            project_name = "Проект", task_title = "Тест\u202e\n задачи" } } : [];
        var completed = complete ? new object[] { new { id = "r-test", provider = "gemini_free", status = "succeeded",
            started_at = time.AddSeconds(-6), finished_at = time, project_name = "Проект", task_title = "Тест" } } : [];
        return new { schema_version = schema, updated_at = time, runner_alive = online,
            agents = Providers.Select(p => new { provider = p, enabled = true, ready = true,
                active_runs = running && p == "gemini_free" ? 1 : 0 }), activity = new { active, completed } };
    }

    private static void WithFile(Action<string> check)
    {
        var dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try { check(Path.Combine(dir, "atoll-status.json")); }
        finally { Directory.Delete(dir, true); }
    }

    private static void Write(string path, object value)
    {
        File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(value));
        File.Move(path + ".tmp", path, true);
    }

    [Fact]
    public void ReadyRunningCompletedCycle()
    {
        WithFile(path =>
        {
            var now = DateTimeOffset.UtcNow;
            Write(path, Snapshot(now));
            var idle = MainWindow.ReadWorkspaceSnapshot(path);
            Assert.Equal(6, idle.Agents!.Count);
            Assert.All(idle.Agents, agent => Assert.Equal("Ready", agent.State));
            Assert.Equal(0, idle.Activity!.Running);
            Write(path, Snapshot(now, running: true));
            var active = MainWindow.ReadWorkspaceSnapshot(path);
            Assert.Equal("Running", active.Agents![4].State);
            Assert.Equal(1, active.Activity!.Running);
            Assert.Contains("Проект", active.Activity.Summary);
            Assert.Contains("В работе", active.Activity.Summary);
            Assert.DoesNotContain("\u202e", active.Activity.Summary);
            Write(path, Snapshot(now, complete: true));
            var done = MainWindow.ReadWorkspaceSnapshot(path);
            Assert.Equal("Ready", done.Agents![4].State);
            Assert.Equal(0, done.Activity!.Running);
            Assert.Equal("r-test", Assert.Single(done.Activity.Completed).Id);
            Assert.Contains("Завершено", done.Activity.History);
            Assert.Contains("00:00:06", done.Activity.History);
        });
    }

    [Fact]
    public void RejectsMissingMalformedOversizedStaleAndFutureSnapshots()
    {
        WithFile(path =>
        {
            Assert.Null(MainWindow.ReadWorkspaceSnapshot(path).Agents);
            File.WriteAllText(path, "{");
            Assert.Null(MainWindow.ReadWorkspaceSnapshot(path).Agents);
            File.WriteAllText(path, new string('x', 65537));
            Assert.Null(MainWindow.ReadWorkspaceSnapshot(path).Agents);
            foreach (var time in new[] { DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1) })
            {
                Write(path, Snapshot(time));
                Assert.Null(MainWindow.ReadWorkspaceSnapshot(path).Agents);
            }
            Write(path, Snapshot(DateTimeOffset.UtcNow, schema: 2));
            Assert.Null(MainWindow.ReadWorkspaceSnapshot(path).Agents);
        });
    }

    [Fact]
    public void OfflineSuppressesActivity()
    {
        WithFile(path =>
        {
            Write(path, Snapshot(DateTimeOffset.UtcNow, online: false, running: true));
            var result = MainWindow.ReadWorkspaceSnapshot(path);
            Assert.All(result.Agents!, agent => Assert.Equal("Runner offline", agent.State));
            Assert.Null(result.Activity);
        });
    }
}
