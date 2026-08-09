using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace NativeTerminalSpike;

public sealed class SpikeForm : Form
{
    private const string VirtualHostName = "terminal.spike.invalid";

    private readonly SpikeOptions _options;
    private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private readonly List<string> _log = [];

    private ShellSession? _session;
    private string _userDataFolder = string.Empty;

    // Per-chunk bookkeeping for the throughput measurement.
    private readonly Dictionary<long, (double SentMs, int Bytes)> _inFlight = [];
    private long _seq;
    private Scenario? _current;
    private TaskCompletionSource<Scenario>? _scenarioDone;
    private readonly StringBuilder _tail = new();

    private double _envReadyMs;
    private double _pageReadyMs;

    public SpikeForm(SpikeOptions options)
    {
        _options = options;
        Text = "mRemoteNG native SSH terminal — spike";
        Width = 1100;
        Height = 700;
        StartPosition = FormStartPosition.CenterScreen;
        Controls.Add(_web);
    }

    private void Log(string message)
    {
        _log.Add($"[{_clock.Elapsed.TotalSeconds,7:F3}s] {message}");
    }

    protected override async void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        try
        {
            await InitialiseAsync();
        }
        catch (Exception ex)
        {
            Log($"FATAL {ex.GetType().Name}: {ex.Message}");
            Finish(success: false);
        }
    }

    private async Task InitialiseAsync()
    {
        _userDataFolder = Path.Combine(Path.GetTempPath(), "mrng-terminal-spike", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_userDataFolder);

        double t0 = _clock.Elapsed.TotalMilliseconds;

        CoreWebView2Environment env;
        try
        {
            env = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
        }
        catch (WebView2RuntimeNotFoundException ex)
        {
            // Task 1.4, demonstrated rather than asserted: the runtime-absent case is a distinct
            // exception type and must not be folded into a generic failure the way HTTPBase does.
            Log($"WEBVIEW2_RUNTIME_MISSING: {ex.Message}");
            Finish(success: false);
            return;
        }

        await _web.EnsureCoreWebView2Async(env);
        _envReadyMs = _clock.Elapsed.TotalMilliseconds - t0;
        Log($"WebView2 environment + control ready in {_envReadyMs:F1} ms");

        CoreWebView2Settings settings = _web.CoreWebView2.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.IsStatusBarEnabled = false;
        settings.AreHostObjectsAllowed = false;      // design.md D2: message channel only
        settings.IsWebMessageEnabled = true;

        _web.CoreWebView2.WebMessageReceived += OnWebMessage;

        string assets = Path.Combine(AppContext.BaseDirectory, "assets");
        double navStart = _clock.Elapsed.TotalMilliseconds;

        if (_options.Delivery == AssetDelivery.VirtualHost)
        {
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                VirtualHostName, assets, CoreWebView2HostResourceAccessKind.Deny);
            _web.CoreWebView2.Navigate($"https://{VirtualHostName}/index.html");
        }
        else
        {
            _web.CoreWebView2.NavigateToString(BuildInlineDocument(assets));
        }

        Log($"navigation started ({_options.Delivery}) at {navStart:F1} ms");
    }

    /// <summary>
    /// Mode B for task 1.3: one self-contained document. Note the CSP has to permit inline script
    /// and style, because NavigateToString gives the document an opaque origin and 'self' matches
    /// nothing.
    /// </summary>
    private static string BuildInlineDocument(string assets)
    {
        string Read(string name) => File.ReadAllText(Path.Combine(assets, name));

        StringBuilder sb = new();
        sb.Append("<!doctype html><html><head><meta charset=\"utf-8\">");
        sb.Append("<meta http-equiv=\"Content-Security-Policy\" content=\"default-src 'none'; ");
        sb.Append("script-src 'unsafe-inline'; style-src 'unsafe-inline'; img-src data:; connect-src 'none'; base-uri 'none'; form-action 'none'\">");
        sb.Append("<style>").Append(Read("xterm.css")).Append("</style>");
        sb.Append("<style>").Append(Read("host.css")).Append("</style>");
        sb.Append("</head><body><div id=\"term\"></div>");
        sb.Append("<script>").Append(Read("xterm.js")).Append("</script>");
        sb.Append("<script>").Append(Read("addon-fit.js")).Append("</script>");
        sb.Append("<script>").Append(Read("bridge.js")).Append("</script>");
        sb.Append("</body></html>");
        return sb.ToString();
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        JsonNode? node;
        try
        {
            node = JsonNode.Parse(e.WebMessageAsJson);
        }
        catch
        {
            return;
        }

        if (node is null) return;
        string type = node["t"]?.GetValue<string>() ?? string.Empty;

        switch (type)
        {
            case "ready":
                _pageReadyMs = _clock.Elapsed.TotalMilliseconds;
                int cols = node["cols"]?.GetValue<int>() ?? 80;
                int rows = node["rows"]?.GetValue<int>() ?? 24;
                Log($"page ready in {_pageReadyMs:F1} ms total, {cols}x{rows}");
                _ = StartSessionAsync((uint)cols, (uint)rows);
                break;

            case "a":
                long seq = node["s"]?.GetValue<long>() ?? -1;
                OnAck(seq);
                break;

            case "i":
                _session?.Write(node["d"]?.GetValue<string>() ?? string.Empty);
                break;

            case "r":
                _session?.Resize(
                    (uint)(node["cols"]?.GetValue<int>() ?? 80),
                    (uint)(node["rows"]?.GetValue<int>() ?? 24));
                break;
        }
    }

    private async Task StartSessionAsync(uint cols, uint rows)
    {
        try
        {
            _session = new ShellSession(_options);
            _session.DataReceived += OnShellData;
            _session.Closed += reason => BeginInvoke(() => Log($"session closed: {reason}"));

            double t0 = _clock.Elapsed.TotalMilliseconds;
            await Task.Run(() => _session.Connect(cols, rows));
            Log($"ssh connected + shell opened in {_clock.Elapsed.TotalMilliseconds - t0:F1} ms");

            if (_options.Benchmark)
                await RunBenchmarkAsync();
            else
                _web.CoreWebView2.PostWebMessageAsJson("{\"t\":\"focus\"}");
        }
        catch (Exception ex)
        {
            Log($"FATAL session: {ex.GetType().Name}: {ex.Message}");
            Finish(success: false);
        }
    }

    private void OnShellData(string text, int bytes)
    {
        if (!IsHandleCreated) return;
        BeginInvoke(() => PushToPage(text, bytes));
    }

    private void PushToPage(string text, int bytes)
    {
        Scenario? scenario = _current;
        long seq = ++_seq;
        double now = _clock.Elapsed.TotalMilliseconds;

        if (scenario is not null)
        {
            scenario.Bytes += bytes;
            scenario.Chars += text.Length;
            scenario.Replacements += CountReplacements(text);
            if (scenario.FirstByteMs is null) scenario.FirstByteMs = now;
            scenario.LastByteMs = now;
            _inFlight[seq] = (now, bytes);

            _tail.Append(text);
            if (_tail.Length > 4096) _tail.Remove(0, _tail.Length - 4096);
            if (scenario.SentinelSeenMs is null && _tail.ToString().Contains(scenario.Sentinel, StringComparison.Ordinal))
                scenario.SentinelSeenMs = now;
        }

        JsonObject msg = new() { ["t"] = "d", ["s"] = seq, ["d"] = text };
        _web.CoreWebView2.PostWebMessageAsJson(msg.ToJsonString());
    }

    private static int CountReplacements(string s)
    {
        int n = 0;
        foreach (char c in s)
            if (c == '�') n++;
        return n;
    }

    private void OnAck(long seq)
    {
        double now = _clock.Elapsed.TotalMilliseconds;
        Scenario? scenario = _current;

        if (_inFlight.Remove(seq, out (double SentMs, int Bytes) sent) && scenario is not null)
        {
            scenario.Lags.Add(now - sent.SentMs);
            if (scenario.LastAckMs is not null)
                scenario.Gaps.Add(now - scenario.LastAckMs.Value);
            scenario.LastAckMs = now;
            scenario.RenderedBytes += sent.Bytes;
        }

        if (scenario is not null && scenario.SentinelSeenMs is not null && _inFlight.Count == 0)
        {
            scenario.CompletedMs = now;
            _current = null;
            _scenarioDone?.TrySetResult(scenario);
        }
    }

    private async Task<Scenario> RunScenarioAsync(string name, string sentinel, string command, int timeoutSeconds = 120)
    {
        Scenario scenario = new(name, sentinel);
        _tail.Clear();
        _inFlight.Clear();
        _current = scenario;
        _scenarioDone = new TaskCompletionSource<Scenario>(TaskCreationOptions.RunContinuationsAsynchronously);

        scenario.StartMs = _clock.Elapsed.TotalMilliseconds;
        _session!.Write(command + "\n");

        Task completed = await Task.WhenAny(_scenarioDone.Task, Task.Delay(TimeSpan.FromSeconds(timeoutSeconds)));
        if (completed != _scenarioDone.Task)
        {
            scenario.TimedOut = true;
            scenario.CompletedMs = _clock.Elapsed.TotalMilliseconds;
            _current = null;
            Log($"scenario {name} TIMED OUT after {timeoutSeconds}s");
        }

        Log($"scenario {name}: {scenario.Bytes:N0} bytes, {scenario.WallMs:F0} ms, " +
            $"{scenario.RenderMBps:F2} MB/s rendered, max lag {scenario.MaxLagMs:F0} ms, max gap {scenario.MaxGapMs:F0} ms");
        return scenario;
    }

    private async Task RunBenchmarkAsync()
    {
        List<Scenario> results = [];

        // Settle the shell: consume the login banner and prompt before measuring anything.
        await RunScenarioAsync("warmup", "WARM" + "UP_OK", "printf 'WARM%s\\n' 'UP_OK'", 30);
        await Task.Delay(500);

        // The sentinels are split across a printf format and its argument so the shell's own echo
        // of the command line cannot produce a premature match.
        results.Add(await RunScenarioAsync(
            "cat-ascii-5.3MB", "SEN" + "T_ASCII",
            "cat /config/bench/ascii.txt; printf 'SEN%s\\n' 'T_ASCII'"));

        await Task.Delay(500);

        results.Add(await RunScenarioAsync(
            "cat-utf8-2.2MB", "SEN" + "T_UTF8",
            "cat /config/bench/utf8.txt; printf 'SEN%s\\n' 'T_UTF8'"));

        await Task.Delay(500);

        results.Add(await RunScenarioAsync(
            "fullscreen-redraw-200", "SEN" + "T_REDRAW",
            "for i in $(seq 1 200); do printf '\\033[H\\033[2J'; cat /config/bench/screen.txt; done; printf 'SEN%s\\n' 'T_REDRAW'"));

        WriteResults(results);
        Finish(success: results.TrueForAll(r => !r.TimedOut));
    }

    private void WriteResults(List<Scenario> results)
    {
        JsonObject root = new()
        {
            ["delivery"] = _options.Delivery.ToString(),
            ["webview2EnvironmentMs"] = Math.Round(_envReadyMs, 1),
            ["pageReadyMs"] = Math.Round(_pageReadyMs, 1),
            ["target"] = $"{_options.User}@{_options.Host}:{_options.Port}",
            ["auth"] = _options.PrivateKeyPath is { Length: > 0 } ? "publickey" : "password"
        };

        JsonArray arr = [];
        foreach (Scenario s in results) arr.Add(s.ToJson());
        root["scenarios"] = arr;

        string json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_options.ResultsPath, json);
        File.WriteAllLines(Path.ChangeExtension(_options.ResultsPath, ".log"), _log);
    }

    private void Finish(bool success)
    {
        if (_log.Count > 0 && !File.Exists(Path.ChangeExtension(_options.ResultsPath, ".log")))
            File.WriteAllLines(Path.ChangeExtension(_options.ResultsPath, ".log"), _log);

        Environment.ExitCode = success ? 0 : 1;
        if (_options.Benchmark) Close();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _session?.Dispose();
        base.OnFormClosed(e);
    }
}
