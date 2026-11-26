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
                if (_shutDown)
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
            ApplyWorkspaceActivity(status.Activity);
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

    private static (string Status, List<(string State, string Count)>? Agents, WorkspaceActivity? Activity) ReadWorkspaceSnapshot()
    {
        List<(string State, string Count)>? unavailable = null;
        try
        {
            using var file = new FileStream(WorkspaceSnapshotPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            if (file.Length > 65536) return ("Invalid status snapshot", unavailable, null);
            using var buffer = new MemoryStream();
            var bytes = new byte[4096];
            int read;
            while ((read = file.Read(bytes, 0, bytes.Length)) > 0)
            {
                if (buffer.Length + read > 65536) return ("Invalid status snapshot", unavailable, null);
                buffer.Write(bytes, 0, read);
            }
            buffer.Position = 0;
            using var document = JsonDocument.Parse(buffer);
            var root = document.RootElement;
            if (root.GetProperty("schema_version").GetInt32() != 1)
                return ("Unsupported status schema", unavailable, null);
            var timestamp = root.GetProperty("updated_at").GetDateTimeOffset();
            var age = DateTimeOffset.UtcNow - timestamp;
            if (age > TimeSpan.FromSeconds(30) || age < TimeSpan.FromSeconds(-5))
                return ("Status snapshot stale or clock mismatch", unavailable, null);
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
                if (active < 0) return ("Invalid status snapshot", unavailable, null);
                string state = !enabled ? "Disabled" : !runner ? "Runner offline" : active > 0 ? "Running" : ready ? "Ready" : "Unavailable";
                rows.Add((state, runner ? $"{active} active" : "—"));
            }
            return (runner ? "Runner online" : "Runner offline", rows, runner ? ParseWorkspaceActivity(root) : null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException or FormatException or KeyNotFoundException or ArgumentException)
        {
            return ("Local status unavailable", unavailable, null);
        }
    }

    private sealed record WorkspaceActivity(int Running, string Summary, List<(string Id, string Provider, string Status)> Completed);
    private readonly HashSet<string> _workspaceSeen = new();
    private bool _workspaceBaseline;
    private static readonly Dictionary<string, string> WorkspaceProviders = new()
    {
        ["codex"] = "Codex", ["claude_code"] = "Claude Code", ["deepseek_api"] = "DeepSeek",
        ["grok_api"] = "Grok", ["gemini_free"] = "Gemini", ["openrouter_free"] = "OpenRouter"
    };

    private static string SafeWorkspaceId(JsonElement item, string field)
    {
        var value = item.GetProperty(field).GetString() ?? "";
        return value.Length is > 0 and <= 80 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '-' || c == '_') ? value : "";
    }

    private static WorkspaceActivity? ParseWorkspaceActivity(JsonElement root)
    {
        if (!root.TryGetProperty("activity", out var activity)) return null;
        try
        {
            var active = activity.GetProperty("active").EnumerateArray().Take(64);
            var projects = new HashSet<string>();
            var lines = new List<string>();
            var running = 0;
            foreach (var item in active)
            {
                var provider = item.GetProperty("provider").GetString() ?? "";
                var project = SafeWorkspaceId(item, "project_id");
                var task = SafeWorkspaceId(item, "task_id");
                var state = item.GetProperty("status").GetString();
                if (!WorkspaceProviders.TryGetValue(provider, out var name) || project == "" || task == "" || state is not ("pending" or "running")) continue;
                projects.Add(project);
                if (state == "running") running++;
                lines.Add($"{name} · {state} · {project} / {task}");
            }
            var completed = new List<(string, string, string)>();
            foreach (var item in activity.GetProperty("completed").EnumerateArray().Take(32))
            {
                var id = SafeWorkspaceId(item, "id");
                var provider = item.GetProperty("provider").GetString() ?? "";
                var state = item.GetProperty("status").GetString();
                if (id != "" && WorkspaceProviders.ContainsKey(provider) && state is "succeeded" or "failed" or "cancelled" or "timed_out")
                    completed.Add((id, provider, state));
            }
            var summary = $"{projects.Count} projects · {lines.Count} active or queued runs";
            if (lines.Count > 0) summary += Environment.NewLine + string.Join(Environment.NewLine, lines);
            return new WorkspaceActivity(running, summary, completed);
        }
        catch (Exception ex) when (ex is InvalidOperationException or KeyNotFoundException or FormatException)
        { return null; }
    }

    private void ApplyWorkspaceActivity(WorkspaceActivity? activity)
    {
        var summary = activity?.Summary ?? "Activity export not available";
        if (WorkspaceActivityList.Text != summary) WorkspaceActivityList.Text = summary;
        var visible = activity is { Running: > 0 } ? Visibility.Visible : Visibility.Collapsed;
        var changed = WorkspaceCompactActivity.Visibility != visible;
        WorkspaceCompactActivity.Visibility = visible;
        var indicator = activity is { Running: > 0 } ? $"AI {activity.Running}" : "";
        if (WorkspaceCompactActivity.Text != indicator) WorkspaceCompactActivity.Text = indicator;
        if (changed && _mode == Mode.Compact) MorphTo(TargetSize(Mode.Compact));
        if (activity == null) { _workspaceBaseline = false; _workspaceSeen.Clear(); return; }
        var fresh = activity.Completed.Where(item => !_workspaceSeen.Contains(item.Id)).ToList();
        _workspaceSeen.Clear();
        foreach (var item in activity.Completed) _workspaceSeen.Add(item.Id);
        if (!_workspaceBaseline) { _workspaceBaseline = true; return; }
        if (fresh.Count == 0 || _hidden || _mode is not (Mode.Compact or Mode.Expanded)) return;
        var failed = fresh.Count(item => item.Status is "failed" or "timed_out");
        var first = fresh[0];
        var title = fresh.Count == 1 ? WorkspaceProviders[first.Provider] + " · " + first.Status : "AI-WORKSPACE · " + fresh.Count + " finished";
        ShowToast(failed > 0 ? "\uE783" : "\uE73E", title, failed > 0 ? $"{failed} failed or timed out" : "Run status updated",
            () => OpenTab(TabWorkspace), ms: 4500, keep: true);
    }
}
