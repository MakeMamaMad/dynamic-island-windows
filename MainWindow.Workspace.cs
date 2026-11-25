using System.IO;
using System.Text.Json;
using System.Windows;

namespace DynamicIsland;

public partial class MainWindow
{
    private bool _workspaceReading;
    private static string WorkspaceSnapshotPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "AI-WORKSPACE", "atoll-status.json");

    private async void WorkspaceRefresh_Click(object sender, RoutedEventArgs e) => await RefreshWorkspaceAsync();

    private async Task RefreshWorkspaceAsync()
    {
        if (_workspaceReading || _shutDown) return;
        _workspaceReading = true;
        try
        {
            var status = await Task.Run(ReadWorkspaceSnapshot);
            if (_shutDown) return;
            WorkspaceStatus.Text = status.Status;
            WorkspaceAgents.Text = status.Agents;
        }
        finally { _workspaceReading = false; }
    }

    private static (string Status, string Agents) ReadWorkspaceSnapshot()
    {
        const string unavailable = "No verified agent status available.";
        try
        {
            using var file = new FileStream(WorkspaceSnapshotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 65536) return ("Invalid status snapshot", unavailable);
            using var buffer = new MemoryStream();
            var bytes = new byte[4096];
            int read;
            while ((read = file.Read(bytes, 0, bytes.Length)) > 0)
            {
                if (buffer.Length + read > 65536) return ("Invalid status snapshot", unavailable);
                buffer.Write(bytes, 0, read);
            }
            buffer.Position = 0;
            using var document = JsonDocument.Parse(buffer);
            var root = document.RootElement;
            if (root.GetProperty("schema_version").GetInt32() != 1)
                return ("Unsupported status schema", unavailable);
            var timestamp = root.GetProperty("updated_at").GetDateTimeOffset();
            var age = DateTimeOffset.UtcNow - timestamp;
            if (age > TimeSpan.FromSeconds(30) || age < TimeSpan.FromSeconds(-5))
                return ("Status snapshot stale or clock mismatch", unavailable);
            bool runner = root.GetProperty("runner_alive").GetBoolean();
            var agents = root.GetProperty("agents");
            var rows = new List<string>();
            var providers = new (string Id, string Label)[]
            {
                ("codex", "Codex"), ("claude_code", "Claude Code"),
                ("deepseek_api", "DeepSeek"), ("grok_api", "Grok"),
                ("gemini_free", "Gemini"), ("openrouter_free", "OpenRouter")
            };
            foreach (var (id, label) in providers)
            {
                var matches = agents.EnumerateArray().Where(a => a.GetProperty("provider").GetString() == id).ToArray();
                if (matches.Length != 1) { rows.Add(label + "  |  Unknown"); continue; }
                var agent = matches[0];
                bool enabled = agent.GetProperty("enabled").GetBoolean();
                bool ready = agent.GetProperty("ready").GetBoolean();
                int active = agent.GetProperty("active_runs").GetInt32();
                if (active < 0) return ("Invalid status snapshot", unavailable);
                string state = !enabled ? "Disabled" : !runner ? "Runner offline" : active > 0 ? "Running" : ready ? "Ready" : "Unavailable";
                rows.Add($"{label}  |  {state}  |  {active} active");
            }
            return (runner ? "Runner online" : "Runner offline", string.Join(Environment.NewLine, rows));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
        {
            return ("Local status unavailable", unavailable);
        }
    }
}
