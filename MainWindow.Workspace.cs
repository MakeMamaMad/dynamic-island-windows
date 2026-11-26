using System.IO;
using System.Text.Json;
using System.Windows;

namespace DynamicIsland;

public partial class MainWindow
{
    private bool _workspaceReading;
    private System.Windows.Threading.DispatcherTimer? _workspaceTimer;

    private void StartWorkspaceRefresh()
    {
        if (_workspaceTimer == null)
        {
            _workspaceTimer = new System.Windows.Threading.DispatcherTimer(
                System.Windows.Threading.DispatcherPriority.Background)
            { Interval = TimeSpan.FromSeconds(5) };
            _workspaceTimer.Tick += (_, _) =>
            {
                if (_shutDown || _mode != Mode.Expanded || TabWorkspace.IsChecked != true)
                {
                    _workspaceTimer.Stop();
                    return;
                }
                _ = RefreshWorkspaceAsync();
            };
        }
        _workspaceTimer.Start();
        _ = RefreshWorkspaceAsync();
    }
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
            if (WorkspaceStatus.Text != status.Status) WorkspaceStatus.Text = status.Status;
            System.Windows.Controls.TextBlock[] states = [WorkspaceAgentState0, WorkspaceAgentState1, WorkspaceAgentState2, WorkspaceAgentState3, WorkspaceAgentState4, WorkspaceAgentState5];
            System.Windows.Controls.TextBlock[] counts = [WorkspaceAgentCount0, WorkspaceAgentCount1, WorkspaceAgentCount2, WorkspaceAgentCount3, WorkspaceAgentCount4, WorkspaceAgentCount5];
            for (int i = 0; i < states.Length; i++)
            {
                var agent = status.Agents != null ? status.Agents[i] : ("Unknown", "—");
                if (states[i].Text != agent.Item1)
                {
                    states[i].Text = agent.Item1;
                    states[i].Foreground = agent.Item1 switch
                    {
                        "Ready" => System.Windows.Media.Brushes.MediumSeaGreen,
                        "Running" => System.Windows.Media.Brushes.DodgerBlue,
                        _ => System.Windows.Media.Brushes.Gray
                    };
                }
                if (counts[i].Text != agent.Item2) counts[i].Text = agent.Item2;
            }
        }
        finally { _workspaceReading = false; }
    }

    private static (string Status, List<(string State, string Count)>? Agents) ReadWorkspaceSnapshot()
    {
        List<(string State, string Count)>? unavailable = null;
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
            var rows = new List<(string State, string Count)>();
            var providers = new (string Id, string Label)[]
            {
                ("codex", "Codex"), ("claude_code", "Claude Code"),
                ("deepseek_api", "DeepSeek"), ("grok_api", "Grok"),
                ("gemini_free", "Gemini"), ("openrouter_free", "OpenRouter")
            };
            foreach (var (id, label) in providers)
            {
                var matches = agents.EnumerateArray().Where(a => a.GetProperty("provider").GetString() == id).ToArray();
                if (matches.Length != 1) { rows.Add(("Unknown", "—")); continue; }
                var agent = matches[0];
                bool enabled = agent.GetProperty("enabled").GetBoolean();
                bool ready = agent.GetProperty("ready").GetBoolean();
                int active = agent.GetProperty("active_runs").GetInt32();
                if (active < 0) return ("Invalid status snapshot", unavailable);
                string state = !enabled ? "Disabled" : !runner ? "Runner offline" : active > 0 ? "Running" : ready ? "Ready" : "Unavailable";
                rows.Add((state, runner ? $"{active} active" : "—"));
            }
            return (runner ? "Runner online" : "Runner offline", rows);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
        {
            return ("Local status unavailable", unavailable);
        }
    }
}
