using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;


using System.Windows.Threading;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace IMVUCompanion;

public enum LogCategory
{
    Info,
    Join,
    Command,
    Sent,
    Warning,
    Error
}

public partial class MainWindow : Window
{
    // ===== Win32 / native interop for simple window picker + input/OCR (no ports/flags) =====
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc enumProc, IntPtr lParam);
    private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

    [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool SetCursorPos(int X, int Y);

    [DllImport("user32.dll")]
    private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, int dwExtraInfo);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    private static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

    private const uint WM_CHAR = 0x0102;
    private const uint WM_KEYDOWN = 0x0100;
    private const uint WM_KEYUP = 0x0101;
    private const int VK_RETURN = 0x0D;

    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_UNICODE = 0x0004;

    // Simple window info for dropdown
    public class WindowInfo
    {
        public IntPtr Hwnd { get; set; }
        public uint ProcessId { get; set; }
        public string Title { get; set; } = "";
        public string ProcessName { get; set; } = "";
        public RECT Rect;
        public string Display => $"{ProcessName}: {Title}";
    }

    private sealed class CdpTarget
    {
        public int Port { get; set; }
        public string Title { get; set; } = "";
        public string Url { get; set; } = "";
    }

    private static readonly int[] DefaultCdpPorts = { 9222, 9223, 9224, 9225, 9226, 9227, 9228, 9229, 9230 };

    public class CommandEntry
    {
        public string Command { get; set; } = "";
        public string Response { get; set; } = "";
        public string Display => string.IsNullOrWhiteSpace(Command) ? Response : $"{Command} → {Response}";
    }

    private DispatcherTimer _aliveTimer;
    private System.Timers.Timer _robustHeartbeatTimer;
    private DispatcherTimer? _pollTimer;   // for native OCR polling on selected window

    private bool _botRunning = false;
    private readonly HashSet<string> _seenLines = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _greetedJoiners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _recentBotMessages = new(StringComparer.OrdinalIgnoreCase);
    private readonly Queue<string> _recentBotMessageOrder = new();
    private readonly SemaphoreSlim _chatSendLock = new(1, 1);
    private sealed class ChatWorkItem
    {
        public string Speaker { get; init; } = "";
        public string Message { get; init; } = "";
        public bool IsWhisper { get; init; }
        public string WhisperRowRef { get; init; } = "";
        public string JoinUserId { get; init; } = "";
    }
    private Channel<ChatWorkItem>? _chatWorkChannel;
    private Task? _chatQueueTask;
    private CancellationTokenSource? _chatQueueCts;
    private bool _appLanguageSyncing;
    private const int MaxRecentBotMessages = 64;
    private const int JoinGreetDelayMs = 1000;

    private static readonly Regex[] JoinNamePatterns =
    {
        new Regex(@"^(.+?)\s+joined\s+the\s+chat\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^(.+?)\s+has\s+joined(?:\s+the\s+room)?\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^(.+?)\s+joined(?:\s+the\s+room)?\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^(.+?)\s+entered\s+the\s+room\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^(.+?)\s+has\s+entered(?:\s+the\s+room)?\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
        new Regex(@"^(.+?)\s+is\s+now\s+in\s+the\s+chat\s*\.?\s*$", RegexOptions.IgnoreCase | RegexOptions.Compiled),
    };

    private CancellationTokenSource? _botCts;
    private bool _exiting = false;
    private readonly DateTime _sessionStartedAt = DateTime.Now;
    // Message templates for events like Welcoming - user editable, random pick, {name} placeholder
    private Dictionary<string, Dictionary<string, List<string>>> _messageTemplates = new Dictionary<string, Dictionary<string, List<string>>>(StringComparer.OrdinalIgnoreCase);
    private readonly Random _random = new Random();
    private const string MessagesFile = "messages.json";
    private string _currentEvent = "Welcoming";
    private string _currentLanguage = "en";
    private string _joinEvent = "Welcoming";
    private sealed class WelcomeExtraConfig
    {
        public bool SendExtra { get; set; }
        public bool AsWhisper { get; set; } = true;
        public Dictionary<string, string> Messages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    private WelcomeExtraConfig _welcomeExtra = new();
    private bool _welcomeExtraUiSyncing;

    private ObservableCollection<string> _currentMessagesView = new ObservableCollection<string>();

    // !Commands: category -> language -> list of command/response pairs
    private Dictionary<string, Dictionary<string, List<CommandEntry>>> _commandCategories =
        new Dictionary<string, Dictionary<string, List<CommandEntry>>>(StringComparer.OrdinalIgnoreCase);
    private const string CommandsFile = "commands.json";
    private string _currentCommandCategory = "General";
    private string _commandLanguage = "en";
    private string _activeCommandCategory = "General";
    private ObservableCollection<CommandEntry> _currentCommandsView = new ObservableCollection<CommandEntry>();

    public MainWindow()
    {
        InitializeComponent();
        Loaded += MainWindow_Loaded;
    }

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            if (this.Height < this.MinHeight) this.Height = this.MinHeight;
            if (this.Width < this.MinWidth) this.Width = this.MinWidth;
            this.Show(); this.Activate();

            LogBox.Document = new FlowDocument { PagePadding = new Thickness(4) };

            File.AppendAllText(@"C:\Users\serve\imvu_companion_crash.log", $"[{DateTime.Now}] LOADED: Web structured DOM (no Classic :/name:text) + internal profile launch for simple no-relogin. Event only. Cleanup done.\n\n");

            _aliveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _aliveTimer.Tick += (s, args) => { try { if (AliveText != null) AliveText.Text = DateTime.Now.ToString("MM.dd.yyyy - HH:mm:ss"); } catch { } };
            _aliveTimer.Start();

            _robustHeartbeatTimer = new System.Timers.Timer(1500);
            _robustHeartbeatTimer.Elapsed += (s, args) => { try { File.AppendAllText(@"C:\Users\serve\imvu_companion_crash.log", $"[{DateTime.Now:HH:mm:ss}] ROBUST: alive\n"); } catch { } };
            _robustHeartbeatTimer.Start();

            this.Closing += (ss, ee) => { if (!_exiting) ee.Cancel = true; };

            // Load user editable messages for events like Welcoming
            LoadMessages();
            PopulateEventCombo();
            if (EventCombo.Items.Count > 0)
            {
                EventCombo.SelectedIndex = 0;
            }
            else
            {
                _currentEvent = "";
            }
            SelectAppLanguageCombo(_currentLanguage);
            RefreshWelcomeExtraUi();
            RefreshMessagesList();  // ensure initial list (or empty if no events in .json)
            if (EventNameEditBox != null) EventNameEditBox.Text = _currentEvent;

            LoadCommands();
            PopulateCategoryCombo();
            if (CommandCategoryCombo.Items.Count > 0)
            {
                foreach (ComboBoxItem cbi in CommandCategoryCombo.Items)
                {
                    if (cbi.Content?.ToString()?.Equals(_activeCommandCategory, StringComparison.OrdinalIgnoreCase) == true)
                    {
                        CommandCategoryCombo.SelectedItem = cbi;
                        break;
                    }
                }
                if (CommandCategoryCombo.SelectedItem == null) CommandCategoryCombo.SelectedIndex = 0;
            }
            else
            {
                _currentCommandCategory = "";
            }
            RefreshCommandsList();
            if (CategoryNameEditBox != null) CategoryNameEditBox.Text = _currentCommandCategory;

            InitAiProvidersUi();

            AppendLog($"{AppVersion.ShortLabel} — {_sessionStartedAt:MM.dd.yyyy - HH:mm:ss}", LogCategory.Info);
            InitAutoUpdateUi();

            _ = InitWebViewAsync();
        }
        catch (Exception ex) { MessageBox.Show("Load error: " + ex.Message); }
    }

    private static System.Windows.Media.Brush BrushForCategory(LogCategory cat) => cat switch
    {
        LogCategory.Join => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x4A, 0xDE, 0x80)),
        LogCategory.Command => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFA, 0xCC, 0x15)),
        LogCategory.Sent => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x7D, 0xD3, 0xFC)),
        LogCategory.Warning => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFB, 0x92, 0x3C)),
        LogCategory.Error => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xF8, 0x71, 0x71)),
        _ => new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xC0, 0xC0, 0xE0))
    };

    private void AppendLog(string msg, LogCategory cat = LogCategory.Info)
    {
        try
        {
            void writeUi()
            {
                if (LogBox.Document == null) LogBox.Document = new FlowDocument { PagePadding = new Thickness(4) };
                var para = new Paragraph(new Run(msg) { Foreground = BrushForCategory(cat) }) { Margin = new Thickness(0), LineHeight = 14 };
                LogBox.Document.Blocks.Add(para);
                while (LogBox.Document.Blocks.Count > 400) LogBox.Document.Blocks.Remove(LogBox.Document.Blocks.FirstBlock);
                LogBox.ScrollToEnd();
            }
            if (Dispatcher.CheckAccess()) writeUi();
            else Dispatcher.BeginInvoke(writeUi);
            File.AppendAllText(@"C:\Users\serve\imvu_companion_crash.log", $"[{DateTime.Now:HH:mm:ss}] {msg}\n");
        }
        catch { }
    }

    private void UpdateStatusText(string text)
    {
        try
        {
            void setUi() { if (StatusText != null) StatusText.Text = text; }
            if (Dispatcher.CheckAccess()) setUi();
            else Dispatcher.BeginInvoke(setUi);
        }
        catch { }
    }

    private void SetBusy(bool busy, string? status = null)
    {
        void setUi()
        {
            BotToggleBtn.IsEnabled = !busy;
            if (status != null) UpdateStatusText(status);
        }
        if (Dispatcher.CheckAccess()) setUi();
        else Dispatcher.BeginInvoke(setUi);
    }

#if false // Legacy external-browser CDP path (removed in v3.0)
    private WindowInfo? GetSelectedWindow()
    {
        if (WindowCombo?.SelectedItem is WindowInfo wi && wi.Hwnd != IntPtr.Zero)
            return wi;
        if (_selectedWindow != null && _selectedWindow.Hwnd != IntPtr.Zero)
            return _selectedWindow;
        return null;
    }

    private static string NormalizeTitle(string title)
    {
        if (string.IsNullOrWhiteSpace(title)) return "";
        title = Regex.Replace(title, @"[\u200B-\u200D\uFEFF]", "");
        title = Regex.Replace(title, @"^\(\d+\)\s*", "");
        title = Regex.Replace(title, @"\s*[-–—]\s*(Google Chrome|Microsoft.? Edge|Mozilla Firefox|Firefox).*$", "", RegexOptions.IgnoreCase);
        return title.Trim();
    }

    private static int ScoreTitleMatch(string windowTitle, string pageTitle)
    {
        var w = NormalizeTitle(windowTitle).ToLowerInvariant();
        var p = NormalizeTitle(pageTitle).ToLowerInvariant();
        if (string.IsNullOrEmpty(w) || string.IsNullOrEmpty(p)) return 0;
        if (w == p) return 100;
        if (w.Contains(p) || p.Contains(w)) return 80;
        var wTokens = w.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var pTokens = p.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return wTokens.Count(t => pTokens.Contains(t)) * 12;
    }

    private static bool IsChromiumProcess(string procName)
    {
        procName = procName.ToLowerInvariant();
        return IsChromeProcess(procName) || IsEdgeProcess(procName) || procName.Contains("brave");
    }

    private static bool IsChromeProcess(string procName) =>
        procName.Equals("chrome", StringComparison.OrdinalIgnoreCase);

    private static bool IsEdgeProcess(string procName) =>
        procName.Equals("msedge", StringComparison.OrdinalIgnoreCase) ||
        procName.Contains("edge", StringComparison.OrdinalIgnoreCase) && !IsChromeProcess(procName);

    private bool IsChromeWindow(WindowInfo wi) =>
        IsChromeProcess(wi.ProcessName) ||
        (_lastDebugBrowser == "chrome" && !IsEdgeProcess(wi.ProcessName));

    private bool IsEdgeWindow(WindowInfo wi) =>
        IsEdgeProcess(wi.ProcessName) ||
        (_lastDebugBrowser == "edge" && !IsChromeProcess(wi.ProcessName));

    private string ResolveBrowserKind(WindowInfo wi)
    {
        if (IsChromeWindow(wi) && !IsEdgeWindow(wi)) return "chrome";
        if (IsEdgeWindow(wi) && !IsChromeWindow(wi)) return "edge";
        if (IsChromeProcess(wi.ProcessName)) return "chrome";
        if (IsEdgeProcess(wi.ProcessName)) return "edge";
        return _lastDebugBrowser ?? "edge";
    }

    private int DefaultDebugPortFor(WindowInfo wi) => ResolveBrowserKind(wi) == "chrome" ? 9223 : 9222;

    private static int? ParseDebugPortFromCommandLine(string? cmdLine)
    {
        if (string.IsNullOrEmpty(cmdLine)) return null;
        var m = Regex.Match(cmdLine, @"--remote-debugging-port=(\d+)");
        return m.Success && int.TryParse(m.Groups[1].Value, out int port) ? port : null;
    }

    private static async Task<int?> TryGetDebugPortForPidAsync(int pid)
    {
        if (pid <= 0) return null;
        try
        {
            return await Task.Run(() =>
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "powershell",
                    Arguments = $"-NoProfile -Command \"(Get-CimInstance Win32_Process -Filter 'ProcessId = {pid}').CommandLine\"",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                using var proc = Process.Start(psi);
                if (proc == null) return (int?)null;
                if (!proc.WaitForExit(1500)) { try { proc.Kill(); } catch { } return null; }
                return ParseDebugPortFromCommandLine(proc.StandardOutput.ReadToEnd());
            });
        }
        catch { return null; }
    }

    private static async Task<List<CdpTarget>> ListCdpTargetsOnPortAsync(int port)
    {
        try
        {
            var json = await CdpHttp.GetStringAsync($"http://127.0.0.1:{port}/json/list");
            using var doc = JsonDocument.Parse(json);
            var list = new List<CdpTarget>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.TryGetProperty("type", out var type) && type.GetString() != "page") continue;
                string title = el.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                string url = el.TryGetProperty("url", out var u) ? u.GetString() ?? "" : "";
                if (string.IsNullOrWhiteSpace(url) || url.StartsWith("chrome-extension://", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new CdpTarget { Port = port, Title = title, Url = url });
            }
            return list;
        }
        catch { return new List<CdpTarget>(); }
    }

    private static int ScoreTarget(WindowInfo wi, CdpTarget t)
    {
        int score = ScoreTitleMatch(wi.Title, t.Title);
        if (t.Url.Contains("imvu.com", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (t.Url.Contains("/chat", StringComparison.OrdinalIgnoreCase) || t.Url.Contains("room-", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (t.Url.Contains("imvu.com", StringComparison.OrdinalIgnoreCase) && wi.Title.Contains("imvu", StringComparison.OrdinalIgnoreCase)) score += 10;
        if (t.Title.Contains("chat", StringComparison.OrdinalIgnoreCase) && wi.Title.Contains("chat", StringComparison.OrdinalIgnoreCase)) score += 20;
        if (wi.Title.Contains("chat", StringComparison.OrdinalIgnoreCase) &&
            (t.Url.Contains("chat", StringComparison.OrdinalIgnoreCase) || t.Title.Contains("imvu", StringComparison.OrdinalIgnoreCase)))
            score += 15;
        return score;
    }

    private static int? ReadDebugPortFile(string path)
    {
        if (!File.Exists(path)) return null;
        try
        {
            var line = File.ReadAllLines(path).FirstOrDefault()?.Trim();
            return int.TryParse(line, out int port) && port > 0 ? port : null;
        }
        catch { return null; }
    }

    private static string ChromeDebugProfileDir() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IMVUCompanion", "ChromeDebug");

    private static int? DiscoverChromeDebugPortFromDisk() =>
        ReadDebugPortFile(Path.Combine(ChromeDebugProfileDir(), "DevToolsActivePort"));

    private static int? DiscoverEdgeDebugPortFromDisk()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return ReadDebugPortFile(Path.Combine(local, "Microsoft", "Edge", "User Data", "DevToolsActivePort"))
            ?? ReadDebugPortFile(Path.Combine(local, "IMVUCompanion", "EdgeDebug", "DevToolsActivePort"));
    }

    private static async Task<int?> FindDebugPortFromBrowserProcessesAsync(string procName)
    {
        foreach (var p in Process.GetProcessesByName(procName))
        {
            var port = await TryGetDebugPortForPidAsync(p.Id);
            if (port.HasValue) return port;
        }
        return null;
    }

    private async Task<(int? port, string? url, string diagnostic)> FindCdpTargetForWindowAsync(WindowInfo wi)
    {
        string browserKind = ResolveBrowserKind(wi);
        bool isChrome = browserKind == "chrome";
        var portsToTry = new List<int>();

        if (isChrome)
        {
            int? disk = DiscoverChromeDebugPortFromDisk();
            if (disk.HasValue) portsToTry.Add(disk.Value);
            int? procPort = await FindDebugPortFromBrowserProcessesAsync("chrome");
            if (procPort.HasValue && !portsToTry.Contains(procPort.Value)) portsToTry.Add(procPort.Value);
            foreach (int p in new[] { 9223, 9224, 9225 }) if (!portsToTry.Contains(p)) portsToTry.Add(p);
        }
        else
        {
            int? disk = DiscoverEdgeDebugPortFromDisk();
            if (disk.HasValue) portsToTry.Add(disk.Value);
            int? procPort = await FindDebugPortFromBrowserProcessesAsync("msedge");
            if (procPort.HasValue && !portsToTry.Contains(procPort.Value)) portsToTry.Add(procPort.Value);
            foreach (int p in new[] { 9222, 9224, 9225 }) if (!portsToTry.Contains(p)) portsToTry.Add(p);
        }

        var pidPort = await TryGetDebugPortForPidAsync((int)wi.ProcessId);
        if (pidPort.HasValue && !portsToTry.Contains(pidPort.Value)) portsToTry.Insert(0, pidPort.Value);

        var portTasks = portsToTry.Distinct().Select(async port =>
        {
            var targets = await ListCdpTargetsOnPortAsync(port);
            return (port, targets);
        });
        var portResults = await Task.WhenAll(portTasks);

        var livePorts = portResults.Where(r => r.targets.Count > 0).Select(r => r.port).ToList();
        var diag = livePorts.Count > 0
            ? $"{browserKind} debug ports alive: {string.Join(", ", livePorts)}"
            : $"no {browserKind} debug ports responded (tried {string.Join(", ", portsToTry.Distinct())})";

        CdpTarget? best = null;
        int bestScore = 0;
        foreach (var (port, targets) in portResults)
        {
            foreach (var t in targets)
            {
                int score = ScoreTarget(wi, t);
                if (score > bestScore) { bestScore = score; best = t; }
            }
        }

        if (best != null && bestScore >= 12) return (best.Port, best.Url, diag);

        foreach (var (port, targets) in portResults)
        {
            var imvu = targets.Where(t => t.Url.Contains("imvu.com", StringComparison.OrdinalIgnoreCase)).ToList();
            if (imvu.Count == 0) continue;
            var pick = imvu.OrderByDescending(t => ScoreTarget(wi, t)).First();
            AppendLog($"CDP fallback: IMVU tab on port {port} ({pick.Title})", LogCategory.Info);
            return (port, pick.Url, diag);
        }

        if (livePorts.Count > 0)
        {
            var (port, targets) = portResults.First(r => r.port == livePorts[0]);
            var any = targets.FirstOrDefault(t => !t.Url.StartsWith("edge://", StringComparison.OrdinalIgnoreCase) && !t.Url.StartsWith("chrome://", StringComparison.OrdinalIgnoreCase));
            if (any != null)
            {
                AppendLog($"CDP fallback: first tab on port {port} ({any.Title})", LogCategory.Info);
                return (port, any.Url, diag);
            }
            diag += $"; tabs on {livePorts[0]}: {string.Join(" | ", targets.Select(t => t.Url))}";
        }

        return (null, null, diag);
    }

    private static void KillChromeBotProfileProcesses()
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = "-NoProfile -Command \"Get-CimInstance Win32_Process -Filter \\\"name='chrome.exe'\\\" | Where-Object { $_.CommandLine -like '*IMVUCompanion*ChromeDebug*' } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue }\"",
                CreateNoWindow = true,
                UseShellExecute = false
            };
            Process.Start(psi)?.WaitForExit(8000);
        }
        catch { }
        Thread.Sleep(1500);
    }

    private static void KillChromiumProcesses(string browser)
    {
        string procName = browser == "chrome" ? "chrome" : "msedge";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = "taskkill",
                    Arguments = $"/F /T /IM {procName}.exe",
                    CreateNoWindow = true,
                    UseShellExecute = false
                })?.WaitForExit(5000);
            }
            catch { }
            foreach (var p in Process.GetProcessesByName(procName))
            {
                try { p.Kill(); } catch { }
            }
            Thread.Sleep(1500);
            if (Process.GetProcessesByName(procName).Length == 0) break;
        }
        Thread.Sleep(1000);
    }

    private static bool IsDebugPortAlive(int port)
    {
        try
        {
            using var tcp = new System.Net.Sockets.TcpClient();
            var task = tcp.ConnectAsync("127.0.0.1", port);
            return task.Wait(1500) && tcp.Connected;
        }
        catch { return false; }
    }

    private async Task<bool> WaitForDebugPortAsync(int port, int seconds = 12, string? browserLabel = null)
    {
        for (int i = 0; i < seconds; i++)
        {
            if (IsDebugPortAlive(port))
            {
                var targets = await ListCdpTargetsOnPortAsync(port);
                if (targets.Count > 0 || i >= 2) return true;
            }
            if (i == 3 || i == 8)
                AppendLog($"Waiting for {(browserLabel ?? "browser")} debug port {port}... ({i + 1}s)", LogCategory.Info);
            await Task.Delay(1000);
        }
        bool alive = IsDebugPortAlive(port);
        if (!alive)
            AppendLog($"Port {port} never opened. {(browserLabel == "chrome" ? "Chrome needs the bot profile — click Restart Chrome+Debug." : "Try Restart Edge+Debug.")}", LogCategory.Warning);
        return alive;
    }

    private async Task<IBrowser?> ConnectCdpWithTimeoutAsync(int port, int timeoutMs = 8000)
    {
        if (_playwright == null) _playwright = await Playwright.CreateAsync();
        if (_browser != null && _connectedCdpPort == port) return _browser;

        if (_browser != null)
        {
            try { await _browser.CloseAsync(); } catch { }
            _browser = null;
            _imvuPage = null;
            _boundPageUrl = null;
        }

        var connectTask = _playwright.Chromium.ConnectOverCDPAsync($"http://127.0.0.1:{port}");
        if (await Task.WhenAny(connectTask, Task.Delay(timeoutMs)) != connectTask)
        {
            AppendLog($"CDP connect timed out after {timeoutMs / 1000}s on port {port}.", LogCategory.Error);
            return null;
        }
        _browser = await connectTask;
        _connectedCdpPort = port;
        return _browser;
    }

    private async Task<IPage?> ResolvePageAsync(IBrowser browser, WindowInfo wi, string? targetUrl)
    {
        var pages = browser.Contexts.SelectMany(c => c.Pages).ToList();
        if (!string.IsNullOrEmpty(targetUrl))
        {
            var exact = pages.FirstOrDefault(p => p.Url.Equals(targetUrl, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;
            var partial = pages.FirstOrDefault(p =>
                p.Url.Contains("imvu.com", StringComparison.OrdinalIgnoreCase) &&
                (targetUrl.Contains(p.Url, StringComparison.OrdinalIgnoreCase) || p.Url.Contains(targetUrl, StringComparison.OrdinalIgnoreCase)));
            if (partial != null) return partial;
        }

        bool wantChat = wi.Title.Contains("chat", StringComparison.OrdinalIgnoreCase);
        if (wantChat)
        {
            foreach (var p in pages)
            {
                string title = await p.TitleAsync();
                if (p.Url.Contains("/chat", StringComparison.OrdinalIgnoreCase) ||
                    p.Url.Contains("room", StringComparison.OrdinalIgnoreCase) ||
                    title.Contains("chat", StringComparison.OrdinalIgnoreCase))
                    return p;
            }
        }

        IPage? best = null;
        int bestScore = 0;
        foreach (var p in pages)
        {
            string title = await p.TitleAsync();
            int score = ScoreTitleMatch(wi.Title, title);
            if (p.Url.Contains("imvu.com", StringComparison.OrdinalIgnoreCase)) score += 15;
            if (p.Url.Contains("/chat", StringComparison.OrdinalIgnoreCase)) score += 20;
            if (score > bestScore) { bestScore = score; best = p; }
        }
        return bestScore >= 10 ? best : pages.FirstOrDefault(p => p.Url.Contains("imvu.com", StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> AttachToSelectedWindowAsync(bool rebindObserver)
    {
        if (_attachInProgress) return false;
        var wi = GetSelectedWindow();
        if (wi == null)
        {
            AppendLog("Select a chat window from the dropdown first (Refresh if empty).", LogCategory.Warning);
            return false;
        }

        if (!IsChromiumProcess(wi.ProcessName))
        {
            AppendLog($"Firefox/other browsers: CDP attach not supported yet for {wi.ProcessName}. Use Chrome or Edge with +Debug launch.", LogCategory.Warning);
            return false;
        }

        _attachInProgress = true;
        SetBusy(true, "Connecting...");
        try
        {
            _selectedWindow = wi;
            _targetHwnd = wi.Hwnd;

            AppendLog($"Finding CDP tab for: {wi.Display}", LogCategory.Info);
            var (port, targetUrl, diagnostic) = await FindCdpTargetForWindowAsync(wi);
            if (port == null)
            {
                AppendLog($"CDP probe: {diagnostic}", LogCategory.Info);
                string browserKind = ResolveBrowserKind(wi);
                int debugPort = browserKind == "chrome" ? 9223 : 9222;
                string priorTitle = wi.Title;

                AppendLog($"Browser has no debug port. Auto-restarting {browserKind} on port {debugPort}...", LogCategory.Warning);
                _lastDebugBrowser = browserKind;
                if (browserKind == "chrome") KillChromeBotProfileProcesses();
                LaunchBrowserWithDebug(browserKind, debugPort, killExisting: browserKind != "chrome");

                int waitSec = browserKind == "chrome" ? 25 : 18;
                if (!await WaitForDebugPortAsync(debugPort, waitSec, browserKind))
                {
                    AppendLog($"Debug port {debugPort} did not open. Click Restart {(browserKind == "chrome" ? "Chrome" : "Edge")}+Debug manually, wait for browser, open IMVU chat, Refresh, then Start Bot.", LogCategory.Error);
                    return false;
                }

                AppendLog($"Debug port {debugPort} is ready. Open your IMVU chat room in the browser window that just opened.", LogCategory.Info);
                RefreshWindows();
                TryReselectWindowByTitle(priorTitle);
                wi = GetSelectedWindow() ?? wi;
                _selectedWindow = wi;
                _targetHwnd = wi.Hwnd;

                (port, targetUrl, diagnostic) = await FindCdpTargetForWindowAsync(wi);
                if (port == null)
                {
                    AppendLog($"CDP probe after restart: {diagnostic}", LogCategory.Warning);
                    AppendLog("Open your IMVU chat room, click Refresh, select the chat window, then Start Bot again.", LogCategory.Warning);
                    return false;
                }
            }

            var browser = await ConnectCdpWithTimeoutAsync(port.Value);
            if (browser == null) return false;

            _imvuPage = await ResolvePageAsync(browser, wi, targetUrl);
            if (_imvuPage == null)
            {
                var urls = string.Join(" | ", browser.Contexts.SelectMany(c => c.Pages).Select(p => p.Url));
                AppendLog("Connected but no tab matched. Open tabs: " + urls, LogCategory.Warning);
                return false;
            }

            _boundPageUrl = null;
            AppendLog($"Attached (port {port}): {wi.Display} → {_imvuPage.Url}", LogCategory.Info);

            if (rebindObserver)
            {
                _seenLines.Clear();
                _greetedJoiners.Clear();
                await DiscoverElements();
                await SetupChatObserver();
                await InitialChatScan();
            }
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("Attach error: " + ex.Message, LogCategory.Error);
            return false;
        }
        finally
        {
            _attachInProgress = false;
            SetBusy(false);
            UpdateStatusText("Ready");
        }
    }
#endif

    private async void BotToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        if (!_botRunning) await StartBot(); else StopBot();
    }

    private async Task StartBot()
    {
        try
        {
            if (!await EnsureChatPageAsync()) return;

            SetBusy(true, "Starting…");
            AppendLog("Starting bot on embedded IMVU chat…", LogCategory.Info);

            _observerBoundUrl = null;
            _botRunning = true;
            BotToggleBtn.Content = "Stop Bot";
            UpdateBotToggleGlow(true);
            _seenLines.Clear();
            _greetedJoiners.Clear();
            _botCts = new CancellationTokenSource();
            StartChatQueue();

            await DiscoverElements();
            await SetupChatObserver();
            UpdatePageStatus();
            await InitialChatScan();
        }
        catch (Exception ex)
        {
            AppendLog("Start error: " + ex.Message, LogCategory.Error);
            _botRunning = false;
            BotToggleBtn.Content = "Start Bot";
            UpdateBotToggleGlow(false);
        }
        finally
        {
            SetBusy(false);
            UpdatePageStatus();
        }
    }

    private void StopBot()
    {
        _botRunning = false;
        BotToggleBtn.Content = "Start Bot";
        UpdateBotToggleGlow(false);
        _botCts?.Cancel();
        StopChatQueue();
        _ = StopBotCleanupAsync();
        AppendLog("Stopped.", LogCategory.Info);
        UpdatePageStatus();
    }

    private async Task StopBotCleanupAsync()
    {
        try { await SetJoinPollPausedAsync(true); } catch { }
        try { await TeardownChatObserverWebView(); } catch { }
        try { await ForceDismissWhisperUiAsync(); } catch { }
        try { await SetJoinPollPausedAsync(false); } catch { }
    }

    private CancellationToken BotCancellationToken =>
        _botCts?.Token ?? CancellationToken.None;

    private bool IsBotActive => _botRunning && !BotCancellationToken.IsCancellationRequested;

    // ===== Message templates management (strictly from one .json, nested event->lang->list<string>) =====
    private void LoadMessages()
    {
        _messageTemplates.Clear();
        _joinEvent = "Welcoming";
        bool fileExisted = File.Exists(MessagesFile);
        try
        {
            if (fileExisted)
            {
                string json = File.ReadAllText(MessagesFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                // Load joinEvent if present
                if (root.TryGetProperty("joinEvent", out var je) && je.ValueKind == JsonValueKind.String)
                {
                    _joinEvent = je.GetString() ?? "Welcoming";
                }

                // Load events: support new { "events": {..} } or direct old flat
                JsonElement eventsEl = default;
                if (root.TryGetProperty("events", out eventsEl) && eventsEl.ValueKind == JsonValueKind.Object)
                {
                    // new structure
                }
                else
                {
                    eventsEl = root;
                }

                var eventsDict = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<string>>>>(eventsEl.GetRawText());
                if (eventsDict != null)
                {
                    _messageTemplates = eventsDict;
                }

                if (root.TryGetProperty("welcomeExtra", out var we) && we.ValueKind == JsonValueKind.Object)
                {
                    if (we.TryGetProperty("sendExtra", out var se) &&
                        (se.ValueKind == JsonValueKind.True || se.ValueKind == JsonValueKind.False))
                        _welcomeExtra.SendExtra = se.GetBoolean();
                    if (we.TryGetProperty("asWhisper", out var aw) &&
                        (aw.ValueKind == JsonValueKind.True || aw.ValueKind == JsonValueKind.False))
                        _welcomeExtra.AsWhisper = aw.GetBoolean();
                    if (we.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Object)
                    {
                        var extraMsgs = JsonSerializer.Deserialize<Dictionary<string, string>>(msgs.GetRawText());
                        if (extraMsgs != null) _welcomeExtra.Messages = extraMsgs;
                    }
                }
            }
        }
        catch (Exception ex) { AppendLog("Load messages err: " + ex.Message); }

        // Seed defaults ONLY if file did not exist at all (first launch ever). 
        // If file exists (even edited/empty/custom events), respect EXACTLY what's in the JSON. No hidden copies or forced "Welcoming".
        if (!fileExisted)
        {
            _messageTemplates["Welcoming"] = new Dictionary<string, List<string>>
            {
                ["en"] = new List<string> { "Welcome {name} to the room!", "Hey {name}, glad you joined!", "Hello {name}!" },
                ["ru"] = new List<string> { "Добро пожаловать {name} в комнату!", "Привет {name}, рад тебя видеть!", "Здравствуй {name}!" }
            };
            _joinEvent = "Welcoming";
            _welcomeExtra.Messages["en"] = "Glad you're here!";
            _welcomeExtra.Messages["ru"] = "Рад тебя видеть!";
            SaveMessages(); // write initial
        }

        // Ensure joinEvent points to something valid WITHOUT forcing "Welcoming" key if user removed/renamed it.
        if (string.IsNullOrEmpty(_joinEvent) || !_messageTemplates.ContainsKey(_joinEvent))
        {
            _joinEvent = _messageTemplates.Keys.FirstOrDefault() ?? "";
        }

        SetAppLanguage("en", refreshUi: false);
    }

    private void SaveMessages()
    {
        try
        {
            SaveWelcomeExtraMessageForCurrentLang();
            var toSave = new
            {
                joinEvent = _joinEvent,
                welcomeExtra = new
                {
                    sendExtra = _welcomeExtra.SendExtra,
                    asWhisper = _welcomeExtra.AsWhisper,
                    messages = _welcomeExtra.Messages
                },
                events = _messageTemplates
            };
            string json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(MessagesFile, json);
            AppendLog("Message templates saved to single .json (all events + languages).");
        }
        catch (Exception ex) { AppendLog("Save messages err: " + ex.Message); }
    }

    private List<string> GetCurrentTemplates(string eventType)
    {
        if (_messageTemplates.TryGetValue(eventType, out var langDict) &&
            langDict.TryGetValue(_currentLanguage, out var list) && list.Count > 0)
            return list;
        // fallback to en
        if (langDict != null && langDict.TryGetValue("en", out var enList) && enList.Count > 0)
            return enList;
        return new List<string> { "Welcome {name}!" };
    }

    private void PopulateEventCombo()
    {
        EventCombo.Items.Clear();
        foreach (var key in _messageTemplates.Keys)
        {
            EventCombo.Items.Add(new ComboBoxItem { Content = key });
        }
        // Do NOT auto-add "Welcoming" or any preset here. Only what's in the JSON.
        // User adds via "Add Event" button. This ensures dropdown exactly matches the .json on (re)load.
    }

    private void RefreshMessagesList()
    {
        if (MessagesList == null || MessageEditBox == null || _currentMessagesView == null) return;

        if (string.IsNullOrEmpty(_currentEvent))
        {
            _currentMessagesView.Clear();
            MessagesList.ItemsSource = _currentMessagesView;
            MessageEditBox.Text = "";
            return;
        }

        // Ensure structure for current event+lang (only for valid event name)
        if (!_messageTemplates.ContainsKey(_currentEvent))
            _messageTemplates[_currentEvent] = new Dictionary<string, List<string>>();
        if (!_messageTemplates[_currentEvent].ContainsKey(_currentLanguage))
            _messageTemplates[_currentEvent][_currentLanguage] = new List<string>();

        var list = _messageTemplates[_currentEvent][_currentLanguage];
        _currentMessagesView.Clear();
        foreach (var m in list) _currentMessagesView.Add(m);
        MessagesList.ItemsSource = _currentMessagesView;
        MessageEditBox.Text = "";
    }

    private void EventCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (EventCombo == null || EventCombo.SelectedItem == null) return;
        if (EventCombo.SelectedItem is ComboBoxItem item)
        {
            _currentEvent = item.Content?.ToString() ?? "";
            if (EventNameEditBox != null) EventNameEditBox.Text = _currentEvent;
            RefreshMessagesList();
        }
    }

    private void SetAppLanguage(string lang, bool refreshUi = true)
    {
        if (string.IsNullOrEmpty(lang)) return;
        if (refreshUi && !_welcomeExtraUiSyncing)
            SaveWelcomeExtraMessageForCurrentLang();
        _currentLanguage = lang;
        _commandLanguage = lang;
        if (refreshUi)
        {
            RefreshMessagesList();
            RefreshCommandsList();
            RefreshWelcomeExtraMessageBox();
        }
    }

    private ButtonGlowAnimator? _botGlowAnimator;

    private void UpdateBotToggleGlow(bool running)
    {
        if (BotToggleBtn == null) return;
        (_botGlowAnimator ??= new ButtonGlowAnimator(BotToggleBtn)).SetActive(running);
    }

    private void SaveWelcomeExtraMessageForCurrentLang()
    {
        if (WelcomeExtraMessageBox == null || string.IsNullOrEmpty(_currentLanguage)) return;
        _welcomeExtra.Messages[_currentLanguage] = WelcomeExtraMessageBox.Text ?? "";
    }

    private void RefreshWelcomeExtraMessageBox()
    {
        if (WelcomeExtraMessageBox == null) return;
        _welcomeExtraUiSyncing = true;
        try
        {
            if (_welcomeExtra.Messages.TryGetValue(_currentLanguage, out var msg))
                WelcomeExtraMessageBox.Text = msg;
            else
                WelcomeExtraMessageBox.Text = "";
        }
        finally { _welcomeExtraUiSyncing = false; }
    }

    private void RefreshWelcomeExtraUi()
    {
        if (WelcomeExtraModeCombo == null) return;
        _welcomeExtraUiSyncing = true;
        try
        {
            string modeTag = _welcomeExtra.SendExtra ? "send" : "none";
            foreach (ComboBoxItem item in WelcomeExtraModeCombo.Items)
            {
                if (item.Tag is string t && t == modeTag)
                {
                    WelcomeExtraModeCombo.SelectedItem = item;
                    break;
                }
            }

            if (WelcomeExtraDeliveryCombo != null)
            {
                string deliveryTag = _welcomeExtra.AsWhisper ? "whisper" : "public";
                foreach (ComboBoxItem item in WelcomeExtraDeliveryCombo.Items)
                {
                    if (item.Tag is string t && t == deliveryTag)
                    {
                        WelcomeExtraDeliveryCombo.SelectedItem = item;
                        break;
                    }
                }
            }

            UpdateWelcomeExtraPanelsVisibility();
            RefreshWelcomeExtraMessageBox();
        }
        finally { _welcomeExtraUiSyncing = false; }
    }

    private void UpdateWelcomeExtraPanelsVisibility()
    {
        bool show = _welcomeExtra.SendExtra;
        if (WelcomeExtraDeliveryCombo != null)
            WelcomeExtraDeliveryCombo.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (WelcomeExtraMessageBox != null)
            WelcomeExtraMessageBox.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SyncWelcomeExtraFromUi()
    {
        if (_welcomeExtraUiSyncing) return;
        if (WelcomeExtraModeCombo?.SelectedItem is ComboBoxItem modeItem && modeItem.Tag is string modeTag)
            _welcomeExtra.SendExtra = modeTag == "send";
        if (WelcomeExtraDeliveryCombo?.SelectedItem is ComboBoxItem delItem && delItem.Tag is string delTag)
            _welcomeExtra.AsWhisper = delTag == "whisper";
        SaveWelcomeExtraMessageForCurrentLang();
        UpdateWelcomeExtraPanelsVisibility();
    }

    private string GetWelcomeExtraMessage()
    {
        if (_welcomeExtra.Messages.TryGetValue(_currentLanguage, out var msg) && !string.IsNullOrWhiteSpace(msg))
            return msg.Trim();
        if (_welcomeExtra.Messages.TryGetValue("en", out var en) && !string.IsNullOrWhiteSpace(en))
            return en.Trim();
        return "";
    }

    private void WelcomeExtraModeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_welcomeExtraUiSyncing || WelcomeExtraModeCombo?.SelectedItem == null) return;
        SyncWelcomeExtraFromUi();
    }

    private void WelcomeExtraDeliveryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_welcomeExtraUiSyncing || WelcomeExtraDeliveryCombo?.SelectedItem == null) return;
        SyncWelcomeExtraFromUi();
    }

    private void WelcomeExtraMessageBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_welcomeExtraUiSyncing) return;
        SaveWelcomeExtraMessageForCurrentLang();
    }

    private void SelectAppLanguageCombo(string lang)
    {
        if (AppLanguageCombo == null) return;
        _appLanguageSyncing = true;
        try
        {
            foreach (ComboBoxItem item in AppLanguageCombo.Items)
            {
                if (item.Tag is string t && string.Equals(t, lang, StringComparison.OrdinalIgnoreCase))
                {
                    AppLanguageCombo.SelectedItem = item;
                    return;
                }
            }
            if (AppLanguageCombo.Items.Count > 0)
                AppLanguageCombo.SelectedIndex = 0;
        }
        finally { _appLanguageSyncing = false; }
    }

    private void AppLanguageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_appLanguageSyncing || AppLanguageCombo?.SelectedItem == null) return;
        if (AppLanguageCombo.SelectedItem is ComboBoxItem item && item.Tag is string lang)
            SetAppLanguage(lang);
    }

    private void MessagesList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (MessagesList == null || MessageEditBox == null) return;
        if (MessagesList.SelectedItem is string sel)
        {
            MessageEditBox.Text = sel;
        }
    }

    private void AddMessage_Click(object sender, RoutedEventArgs e)
    {
        if (!_messageTemplates.ContainsKey(_currentEvent))
            _messageTemplates[_currentEvent] = new Dictionary<string, List<string>>();
        if (!_messageTemplates[_currentEvent].ContainsKey(_currentLanguage))
            _messageTemplates[_currentEvent][_currentLanguage] = new List<string>();

        var underlying = _messageTemplates[_currentEvent][_currentLanguage];
        string text = MessageEditBox.Text.Trim();
        if (string.IsNullOrEmpty(text)) text = "New message for {name}";
        if (!underlying.Contains(text))
        {
            underlying.Add(text);
            _currentMessagesView.Add(text);
        }
    }

    private void RemoveMessage_Click(object sender, RoutedEventArgs e)
    {
        if (MessagesList.SelectedItem is string sel)
        {
            if (_messageTemplates.TryGetValue(_currentEvent, out var langDict) &&
                langDict.TryGetValue(_currentLanguage, out var underlying))
            {
                underlying.Remove(sel);
                _currentMessagesView.Remove(sel);
            }
        }
    }

    private void UpdateMessage_Click(object sender, RoutedEventArgs e)
    {
        if (MessagesList.SelectedItem is string oldSel)
        {
            if (_messageTemplates.TryGetValue(_currentEvent, out var langDict) &&
                langDict.TryGetValue(_currentLanguage, out var underlying))
            {
                string newText = MessageEditBox.Text.Trim();
                if (!string.IsNullOrEmpty(newText))
                {
                    int idx = underlying.IndexOf(oldSel);
                    if (idx >= 0)
                    {
                        underlying[idx] = newText;
                        _currentMessagesView[idx] = newText;
                    }
                }
            }
        }
    }

    private void SaveMessages_Click(object sender, RoutedEventArgs e)
    {
        SaveMessages();
    }

    private void AddEvent_Click(object sender, RoutedEventArgs e)
    {
        string newEvt = NewEventTextBox.Text.Trim();
        if (string.IsNullOrEmpty(newEvt)) return;
        if (!_messageTemplates.ContainsKey(newEvt))
        {
            _messageTemplates[newEvt] = new Dictionary<string, List<string>>
            {
                { _currentLanguage, new List<string> { "New template for {name}" } }
            };
            PopulateEventCombo();
            // select the new event
            foreach (ComboBoxItem cbi in EventCombo.Items)
            {
                if (cbi.Content?.ToString() == newEvt)
                {
                    EventCombo.SelectedItem = cbi;
                    break;
                }
            }
            if (EventNameEditBox != null) EventNameEditBox.Text = newEvt;
            NewEventTextBox.Text = "";
        }
    }

    private void RenameEvent_Click(object sender, RoutedEventArgs e)
    {
        if (EventNameEditBox == null) return;
        string newName = EventNameEditBox.Text.Trim();
        if (string.IsNullOrEmpty(newName) || newName == _currentEvent) return;
        if (_messageTemplates.ContainsKey(newName)) 
        {
            AppendLog("Event name already exists: " + newName);
            return;
        }
        if (_messageTemplates.ContainsKey(_currentEvent))
        {
            var data = _messageTemplates[_currentEvent];
            _messageTemplates.Remove(_currentEvent);
            _messageTemplates[newName] = data;
            if (_joinEvent == _currentEvent) _joinEvent = newName;
            _currentEvent = newName;
            PopulateEventCombo();
            // re-select
            foreach (ComboBoxItem cbi in EventCombo.Items)
            {
                if (cbi.Content?.ToString() == newName)
                {
                    EventCombo.SelectedItem = cbi;
                    break;
                }
            }
            EventNameEditBox.Text = newName;
            RefreshMessagesList();
            SaveMessages();
            AppendLog("Event renamed to " + newName);
        }
    }

    // ===== !Commands management (category -> lang -> list<CommandEntry>) =====
    private static List<CommandEntry> DefaultCommandsEn() => new List<CommandEntry>
    {
        new CommandEntry { Command = "!hi", Response = "Hello there!" },
        new CommandEntry { Command = "!hello", Response = "Hey! How's it going?" },
        new CommandEntry { Command = "!help", Response = "Commands: !hi !hello !wave !thanks !bbot" },
        new CommandEntry { Command = "!wave", Response = "*waves enthusiastically*" },
        new CommandEntry { Command = "!thanks", Response = "You're welcome!" },
        new CommandEntry { Command = "!bot", Response = "I'm here. Type !help for commands." }
    };

    private void LoadCommands()
    {
        _commandCategories.Clear();
        _activeCommandCategory = "General";
        bool fileExisted = File.Exists(CommandsFile);
        try
        {
            if (fileExisted)
            {
                string json = File.ReadAllText(CommandsFile);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                if (root.TryGetProperty("activeCategory", out var ac) && ac.ValueKind == JsonValueKind.String)
                    _activeCommandCategory = ac.GetString() ?? "General";

                JsonElement catsEl = default;
                if (root.TryGetProperty("categories", out catsEl) && catsEl.ValueKind == JsonValueKind.Object) { }
                else catsEl = root;

                var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<CommandEntry>>>>(catsEl.GetRawText());
                if (loaded != null) _commandCategories = loaded;
            }
        }
        catch (Exception ex) { AppendLog("Load commands err: " + ex.Message); }

        if (!fileExisted)
        {
            _commandCategories["General"] = new Dictionary<string, List<CommandEntry>>
            {
                ["en"] = DefaultCommandsEn(),
                ["ru"] = new List<CommandEntry>
                {
                    new CommandEntry { Command = "!hi", Response = "Привет!" },
                    new CommandEntry { Command = "!hello", Response = "Здравствуй! Как дела?" },
                    new CommandEntry { Command = "!help", Response = "Команды: !hi !hello !wave !thanks" },
                    new CommandEntry { Command = "!wave", Response = "*машет рукой*" },
                    new CommandEntry { Command = "!thanks", Response = "Пожалуйста!" },
                    new CommandEntry { Command = "!bot", Response = "Я здесь. Напиши !help для списка команд." }
                }
            };
            _activeCommandCategory = "General";
            SaveCommands();
        }

        if (string.IsNullOrEmpty(_activeCommandCategory) || !_commandCategories.ContainsKey(_activeCommandCategory))
            _activeCommandCategory = _commandCategories.Keys.FirstOrDefault() ?? "General";

        _currentCommandCategory = _activeCommandCategory;
    }

    private void SaveCommands()
    {
        try
        {
            _activeCommandCategory = _currentCommandCategory;
            var toSave = new { activeCategory = _activeCommandCategory, categories = _commandCategories };
            string json = JsonSerializer.Serialize(toSave, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(CommandsFile, json);
            AppendLog("!Commands saved to commands.json.");
        }
        catch (Exception ex) { AppendLog("Save commands err: " + ex.Message); }
    }

    private Dictionary<string, string> GetActiveCommandReplies()
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        string cat = string.IsNullOrEmpty(_activeCommandCategory) ? _currentCommandCategory : _activeCommandCategory;
        if (!_commandCategories.TryGetValue(cat, out var langDict))
            return result;

        List<CommandEntry>? list = null;
        if (langDict.TryGetValue(_commandLanguage, out var langList) && langList.Count > 0)
            list = langList;
        else if (langDict.TryGetValue("en", out var enList) && enList.Count > 0)
            list = enList;

        if (list == null) return result;
        foreach (var entry in list)
        {
            string cmd = NormalizeCommand(entry.Command);
            if (!string.IsNullOrWhiteSpace(cmd) && !string.IsNullOrWhiteSpace(entry.Response))
                result[cmd] = entry.Response;
        }
        return result;
    }

    private static string NormalizeCommand(string cmd)
    {
        cmd = (cmd ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrEmpty(cmd)) return "";
        if (!cmd.StartsWith("!")) cmd = "!" + cmd;
        return cmd;
    }

    private void PopulateCategoryCombo()
    {
        if (CommandCategoryCombo == null) return;
        CommandCategoryCombo.Items.Clear();
        foreach (var key in _commandCategories.Keys)
            CommandCategoryCombo.Items.Add(new ComboBoxItem { Content = key });
    }

    private void RefreshCommandsList()
    {
        if (CommandsList == null || CommandEditBox == null || CommandResponseEditBox == null || _currentCommandsView == null) return;

        if (string.IsNullOrEmpty(_currentCommandCategory))
        {
            _currentCommandsView.Clear();
            CommandsList.ItemsSource = _currentCommandsView;
            CommandEditBox.Text = "";
            CommandResponseEditBox.Text = "";
            return;
        }

        if (!_commandCategories.ContainsKey(_currentCommandCategory))
            _commandCategories[_currentCommandCategory] = new Dictionary<string, List<CommandEntry>>();
        if (!_commandCategories[_currentCommandCategory].ContainsKey(_commandLanguage))
            _commandCategories[_currentCommandCategory][_commandLanguage] = new List<CommandEntry>();

        var list = _commandCategories[_currentCommandCategory][_commandLanguage];
        _currentCommandsView.Clear();
        foreach (var c in list) _currentCommandsView.Add(c);
        CommandsList.ItemsSource = _currentCommandsView;
        CommandEditBox.Text = "";
        CommandResponseEditBox.Text = "";
    }

    private void CommandCategoryCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandCategoryCombo == null || CommandCategoryCombo.SelectedItem == null) return;
        if (CommandCategoryCombo.SelectedItem is ComboBoxItem item)
        {
            _currentCommandCategory = item.Content?.ToString() ?? "";
            _activeCommandCategory = _currentCommandCategory;
            if (CategoryNameEditBox != null) CategoryNameEditBox.Text = _currentCommandCategory;
            RefreshCommandsList();
        }
    }

    private void CommandsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CommandsList == null || CommandEditBox == null || CommandResponseEditBox == null) return;
        if (CommandsList.SelectedItem is CommandEntry sel)
        {
            CommandEditBox.Text = sel.Command;
            CommandResponseEditBox.Text = sel.Response;
        }
    }

    private void AddCommand_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(_currentCommandCategory)) return;
        if (!_commandCategories.ContainsKey(_currentCommandCategory))
            _commandCategories[_currentCommandCategory] = new Dictionary<string, List<CommandEntry>>();
        if (!_commandCategories[_currentCommandCategory].ContainsKey(_commandLanguage))
            _commandCategories[_currentCommandCategory][_commandLanguage] = new List<CommandEntry>();

        var underlying = _commandCategories[_currentCommandCategory][_commandLanguage];
        string cmd = NormalizeCommand(CommandEditBox.Text.Trim());
        string resp = CommandResponseEditBox.Text.Trim();
        if (string.IsNullOrEmpty(cmd)) cmd = "!newcmd";
        if (string.IsNullOrEmpty(resp)) resp = "Response here";

        var entry = new CommandEntry { Command = cmd, Response = resp };
        underlying.Add(entry);
        _currentCommandsView.Add(entry);
        CommandEditBox.Text = cmd;
        CommandResponseEditBox.Text = resp;
    }

    private void RemoveCommand_Click(object sender, RoutedEventArgs e)
    {
        if (CommandsList.SelectedItem is CommandEntry sel)
        {
            if (_commandCategories.TryGetValue(_currentCommandCategory, out var langDict) &&
                langDict.TryGetValue(_commandLanguage, out var underlying))
            {
                underlying.Remove(sel);
                _currentCommandsView.Remove(sel);
            }
        }
    }

    private void UpdateCommand_Click(object sender, RoutedEventArgs e)
    {
        if (CommandsList.SelectedItem is CommandEntry sel)
        {
            if (_commandCategories.TryGetValue(_currentCommandCategory, out var langDict) &&
                langDict.TryGetValue(_commandLanguage, out var underlying))
            {
                string newCmd = NormalizeCommand(CommandEditBox.Text.Trim());
                string newResp = CommandResponseEditBox.Text.Trim();
                if (!string.IsNullOrEmpty(newCmd) && !string.IsNullOrEmpty(newResp))
                {
                    int idx = underlying.IndexOf(sel);
                    if (idx >= 0)
                    {
                        underlying[idx] = new CommandEntry { Command = newCmd, Response = newResp };
                        RefreshCommandsList();
                    }
                }
            }
        }
    }

    private void SaveCommands_Click(object sender, RoutedEventArgs e) => SaveCommands();

    private void AddCategory_Click(object sender, RoutedEventArgs e)
    {
        string newCat = NewCategoryTextBox.Text.Trim();
        if (string.IsNullOrEmpty(newCat)) return;
        if (!_commandCategories.ContainsKey(newCat))
        {
            _commandCategories[newCat] = new Dictionary<string, List<CommandEntry>>
            {
                { _commandLanguage, new List<CommandEntry> { new CommandEntry { Command = "!newcmd", Response = "Response here" } } }
            };
            PopulateCategoryCombo();
            foreach (ComboBoxItem cbi in CommandCategoryCombo.Items)
            {
                if (cbi.Content?.ToString() == newCat)
                {
                    CommandCategoryCombo.SelectedItem = cbi;
                    break;
                }
            }
            if (CategoryNameEditBox != null) CategoryNameEditBox.Text = newCat;
            NewCategoryTextBox.Text = "";
        }
    }

    private void RenameCategory_Click(object sender, RoutedEventArgs e)
    {
        if (CategoryNameEditBox == null) return;
        string newName = CategoryNameEditBox.Text.Trim();
        if (string.IsNullOrEmpty(newName) || newName == _currentCommandCategory) return;
        if (_commandCategories.ContainsKey(newName))
        {
            AppendLog("Category name already exists: " + newName);
            return;
        }
        if (_commandCategories.ContainsKey(_currentCommandCategory))
        {
            var data = _commandCategories[_currentCommandCategory];
            _commandCategories.Remove(_currentCommandCategory);
            _commandCategories[newName] = data;
            if (_activeCommandCategory == _currentCommandCategory) _activeCommandCategory = newName;
            _currentCommandCategory = newName;
            PopulateCategoryCombo();
            foreach (ComboBoxItem cbi in CommandCategoryCombo.Items)
            {
                if (cbi.Content?.ToString() == newName)
                {
                    CommandCategoryCombo.SelectedItem = cbi;
                    break;
                }
            }
            CategoryNameEditBox.Text = newName;
            RefreshCommandsList();
            SaveCommands();
            AppendLog("Category renamed to " + newName);
        }
    }

    private async Task SetupChatObserver() => await SetupChatObserverWebView();

#if false // legacy observer body
    private async Task SetupChatObserverLegacy()
    {
        if (false) return;
        try
        {
            // Use YOUR exact classes: chat-stream2 for the chat room content.
            // Structured extraction (NO ":" / "name: text" - erased per user: web has no usernames or : in chat lines outside Classic).
            // For added nodes, find message row, extract speaker from user DOM el, msgText from text el.
            // Send "speaker\tmsgText" via binding if command (starts !) or join (for greet).
            // Pure event-driven MutationObserver - no constant scan/poll.
            await _imvuPage.EvaluateAsync("""
() => {
    const bad = /radio|on air|now playing|http|www\.|listen|click here|powered by|imvu.com/i;
    const joinPhrases = /joined\s+the\s+chat|has\s+joined|joined\s+the\s+room|entered\s+the\s+room|has\s+entered/i;
    const cont = document.querySelector('div.chat-stream2, [class*="chat-stream2"]') || document.body;
    window._seenJoinKeys = new Set();
    window._seenCmdKeys = new Set();
    function firstLine(t) { return (t || '').trim().split(/[\n\r]/)[0].trim(); }
    function isJoinLine(t) {
        t = firstLine(t);
        if (!t || t.length > 150 || t.length < 6 || bad.test(t) || t.includes('!')) return false;
        if (/left\s+the\s+chat/i.test(t)) return false;
        return joinPhrases.test(t);
    }
    function getJoinNameFromRow(row) {
        if (!row) return '';
        const sels = ['.cs2-name', '[class*="cs2-name"]', '[class*="username"]', '[class*="display-name"]'];
        for (const sel of sels) {
            const nameEl = row.querySelector(sel);
            if (!nameEl) continue;
            let sp = firstLine(nameEl.textContent || nameEl.innerText || '');
            if (sp.length >= 1 && sp.length <= 60 && !bad.test(sp)) return sp;
        }
        return '';
    }
    function parseJoinRow(row) {
        if (!row) return null;
        const txt = firstLine(row.innerText || row.textContent || '');
        if (!isJoinLine(txt)) return null;
        let name = '';
        const m = txt.match(/^(.+?)\s+(joined\s+the\s+chat|has\s+joined(?:\s+the\s+room)?|joined(?:\s+the\s+room)?|entered\s+the\s+room|has\s+entered(?:\s+the\s+room)?)\s*\.?\s*$/i);
        if (m) name = m[1].trim();
        if (!name) name = getJoinNameFromRow(row);
        if (!name) return null;
        return { name, text: txt };
    }
    function findJoinInAddedNode(n) {
        if (!n) return null;
        const el = n.nodeType === 1 ? n : n.parentElement;
        if (!el) return null;
        const candidates = [];
        if (el.closest) {
            const row = el.closest('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="system"], [class*="event"], li');
            if (row && row !== cont) candidates.push(row);
        }
        candidates.push(el);
        for (const c of candidates) {
            const j = parseJoinRow(c);
            if (j) return j;
        }
        return null;
    }
    function getSpeakerFromItem(item) {
        if (!item) return '';
        let userCand = item.querySelector('.cs2-name, [class*="cs2-name"]');
        if (!userCand) {
            userCand = item.querySelector('[class*="user"], [data-user], [data-username]');
        }
        let sp = '';
        if (userCand) {
            sp = firstLine(userCand.textContent || userCand.innerText || '');
        }
        if (sp.length < 1 || sp.length > 60 || bad.test(sp)) sp = '';
        return sp;
    }
    function getMsgTextFromItem(item, speaker) {
        if (!item) return '';
        const textCand = item.querySelector('[class*="text"], [class*="body"], [class*="content"], [class*="msg"], p, span:last-child, div:last-child');
        let txt = textCand ? (textCand.innerText || '').trim() : (item.innerText || '').trim();
        // For command messages, ensure we get the part starting with ! from full text (robust to child selection)
        let fullTxt = (item.innerText || '').trim();
        if (fullTxt.includes('!')) {
            let i = fullTxt.indexOf('!');
            txt = fullTxt.substring(i).trim();
        }
        if (speaker && txt.indexOf(speaker) === 0) {
            txt = txt.substring(speaker.length).replace(/^[\s\-:]+/, '').trim();
        }
        return txt;
    }
    function isCommandLine(t) {
        t = firstLine(t);
        return t && t.length >= 2 && t.length <= 300 && !bad.test(t) && t.includes('!');
    }
    if (window._o) { try { window._o.disconnect(); } catch(e){} }
    window._o = new MutationObserver((ms) => {
        for (let m of ms) {
            for (let n of m.addedNodes) {
                if (n.nodeType !== 1 && n.nodeType !== 3) continue;
                const join = findJoinInAddedNode(n);
                if (join) {
                    const key = join.name.toLowerCase();
                    if (window._seenJoinKeys.has(key)) continue;
                    window._seenJoinKeys.add(key);
                    try { window.newChatMessage(join.name + "\t" + join.text); } catch(e){}
                    continue;
                }
                if (n.nodeType !== 1) continue;
                let item = n.closest ? n.closest('[class*="msg"], [class*="message"], [class*="chat-line"], [class*="system"], li') : n;
                if (!item || item === cont) item = n;
                let rowTxt = firstLine(item.innerText || item.textContent || '');
                if (!isCommandLine(rowTxt)) continue;
                let cmdTxt = rowTxt;
                if (rowTxt.includes('!')) cmdTxt = rowTxt.substring(rowTxt.indexOf('!')).trim();
                const speaker = getSpeakerFromItem(item);
                const dedupe = (speaker || '') + '\t' + cmdTxt;
                if (!window._seenCmdKeys) window._seenCmdKeys = new Set();
                if (window._seenCmdKeys.has(dedupe.toLowerCase())) continue;
                window._seenCmdKeys.add(dedupe.toLowerCase());
                try { window.newChatMessage(speaker + "\t" + cmdTxt); } catch(e){}
            }
        }
    });
    window._o.observe(cont, { childList: true, subtree: true });
    window._lastChatContainer = (cont.className || cont.tagName) + ' (chat-stream2 targeted, structured no-:-web)';
}
""");
            AppendLog("Observer active on .chat-stream2 (event driven, your exact container).");

            try
            {
                var hint = await _imvuPage.EvaluateAsync<string>("() => window._lastChatContainer || 'chat-stream2'");
                AppendLog("Observer container: " + hint);
            }
            catch { }
        }
        catch (Exception ex) { AppendLog("Observer fail: " + ex.Message); }
    }
#endif

    private async Task InitialChatScan()
    {
        if (!IsWebViewReady) return;
        try
        {
            var ls = await GetChatLinesAsync(5);
            foreach (var l in ls) 
            {
                var parts = l.Split(new[] {'\t'}, 2);
                string sp = parts.Length > 1 ? parts[0] : "";
                string m = parts.Length > 1 ? parts[1] : l;
                if (!_seenLines.Add(l)) continue;
                // Seed dedup sets from history — do not greet users already in chat when bot starts
                if (TryResolveJoiner(sp, m, out string histJoiner))
                    _greetedJoiners.Add(histJoiner);
                else
                    EnqueueChatLine(sp, m);
            }
            AppendLog("Initial done (structured web DOM, no : ). found " + ls.Length + " msgs");
        }
        catch (Exception ex) { AppendLog("Initial error: " + ex.Message); }
    }

    private static bool IsJoinLine(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return false;
        string line = Regex.Replace(msg.Trim(), @"\s+", " ").ToLowerInvariant();
        if (line.Contains("left the chat")) return false;
        return line.Contains("joined the chat") || line.Contains("has joined") ||
               line.Contains("joined the room") || line.Contains("entered the room") ||
               line.Contains("has entered") || line.Contains("is now in the chat");
    }

    private static string FoldImvuName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string s = name.Trim().Normalize(System.Text.NormalizationForm.FormKC);
        s = Regex.Replace(s, @"[\u200B-\u200D\uFEFF\u00AD\u2060\u180E]", "");
        s = Regex.Replace(s, @"\s+", " ").Trim().ToLowerInvariant();
        return s;
    }

    private static string SanitizeJoinerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        string cleaned = Regex.Replace(name.Trim(), @"[\u200B-\u200D\uFEFF\u00AD\u2060\u180E]", "");
        cleaned = Regex.Replace(cleaned, @"\s+", " ").Trim();
        if (cleaned.Length < 1 || cleaned.Length > 60) return "";
        if (IsJoinLine(cleaned)) return "";
        if (!HasVisibleJoinerName(cleaned) && string.IsNullOrEmpty(FoldImvuName(cleaned))) return "";
        return cleaned;
    }

    private static bool HasVisibleJoinerName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string cleaned = Regex.Replace(name.Trim(), @"[\u200B-\u200D\uFEFF\u00AD\u2060\u180E]", "");
        return Regex.IsMatch(cleaned, @"\p{L}|\p{N}");
    }

    private static bool TryParseJoinerName(string msg, out string joiner)
    {
        joiner = "";
        if (string.IsNullOrWhiteSpace(msg)) return false;
        string line = Regex.Replace(msg.Trim(), @"\s+", " ");
        foreach (var rx in JoinNamePatterns)
        {
            var m = rx.Match(line);
            if (m.Success)
            {
                joiner = SanitizeJoinerName(m.Groups[1].Value);
                if (!string.IsNullOrEmpty(joiner)) return true;
            }
        }
        return false;
    }

    private static bool TryResolveJoiner(string speaker, string msg, out string joiner)
    {
        joiner = "";
        if (!IsJoinLine(msg)) return false;
        if (TryParseJoinerName(msg, out joiner) && !string.IsNullOrWhiteSpace(joiner))
            return true;
        joiner = SanitizeJoinerName(speaker);
        if (!string.IsNullOrEmpty(joiner) &&
            !joiner.Equals("user", StringComparison.OrdinalIgnoreCase))
            return true;
        joiner = "";
        return false;
    }

    // NOTE: For web/Next/Desktop (non-Classic), NO ":" or "name: text" parsing for speaker. 
    // Speaker extracted from DOM user element in message row (see JS in observer/GetChatLines).
    // Command detected if msgText starts with "!". Reply uses speaker from DOM.
    // Join name is parsed from the join system line text only (never from nearby .cs2-name).
    private static string NormalizeChatText(string s) =>
        Regex.Replace((s ?? "").Trim(), @"\s+", " ");

    private static bool IsValidSpeaker(string speaker)
    {
        if (string.IsNullOrWhiteSpace(speaker)) return false;
        string sp = speaker.Trim();
        if (sp.Equals("user", StringComparison.OrdinalIgnoreCase)) return false;
        if (sp.Contains('!')) return false;
        if (sp.Contains("Commands:", StringComparison.OrdinalIgnoreCase)) return false;
        if (sp.Length > 50) return false;
        return true;
    }

    private static string NormalizeSpeaker(string speaker)
    {
        speaker = speaker.Trim();
        if (speaker.EndsWith(" to me", StringComparison.OrdinalIgnoreCase))
            return speaker[..^6].Trim();
        return speaker;
    }

    private bool IsBotOwnMessage(string speaker, string msg)
    {
        if (!string.IsNullOrWhiteSpace(_botDisplayName))
        {
            string bot = _botDisplayName.Trim();
            string botFold = FoldImvuName(bot);
            string spFold = FoldImvuName(speaker);
            if (!string.IsNullOrEmpty(botFold) && spFold == botFold) return true;
            if (string.Equals(speaker.Trim(), bot, StringComparison.OrdinalIgnoreCase)) return true;
            if (speaker.Trim().StartsWith(bot + " ", StringComparison.OrdinalIgnoreCase)) return true;
        }

        if (!IsValidSpeaker(speaker)) return true;

        string norm = NormalizeChatText(msg);
        if (string.IsNullOrEmpty(norm)) return false;
        if (_recentBotMessages.Contains(norm)) return true;

        int space = norm.IndexOf(' ');
        if (space > 0)
        {
            string tail = norm[(space + 1)..];
            if (_recentBotMessages.Contains(tail)) return true;
        }
        return false;
    }

    private void RegisterBotOutbound(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        string norm = NormalizeChatText(text);
        if (string.IsNullOrEmpty(norm)) return;

        void Track(string s)
        {
            if (string.IsNullOrEmpty(s)) return;
            if (_recentBotMessages.Add(s))
                _recentBotMessageOrder.Enqueue(s);
        }

        Track(norm);

        int space = norm.IndexOf(' ');
        if (space > 0)
            Track(norm[(space + 1)..]);

        var tokens = Regex.Matches(norm, @"!\S+");
        if (tokens.Count > 1)
            Track(string.Join(" ", tokens.Select(m => m.Value)));

        while (_recentBotMessageOrder.Count > MaxRecentBotMessages)
        {
            string old = _recentBotMessageOrder.Dequeue();
            _recentBotMessages.Remove(old);
        }
    }

    private static string ResolveSpeaker(string speaker, string msg)
    {
        if (!IsValidSpeaker(speaker)) return "";
        return NormalizeSpeaker(speaker);
    }

    private void StartChatQueue()
    {
        StopChatQueue();
        var linked = _botCts != null
            ? CancellationTokenSource.CreateLinkedTokenSource(_botCts.Token)
            : new CancellationTokenSource();
        _chatQueueCts = linked;
        _chatWorkChannel = Channel.CreateUnbounded<ChatWorkItem>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });
        _chatQueueTask = RunChatQueueAsync(linked.Token);
    }

    private void StopChatQueue()
    {
        try { _chatQueueCts?.Cancel(); } catch { }
        try { _chatWorkChannel?.Writer.TryComplete(); } catch { }
        _chatWorkChannel = null;
        _chatQueueTask = null;
        _chatQueueCts?.Dispose();
        _chatQueueCts = null;
    }

    private void EnqueueChatLine(string speaker, string msg, bool isWhisper = false, string whisperRowRef = "", string joinUserId = "")
    {
        if (!_botRunning || _chatWorkChannel == null || string.IsNullOrWhiteSpace(msg)) return;
        _chatWorkChannel.Writer.TryWrite(new ChatWorkItem
        {
            Speaker = speaker ?? "",
            Message = msg,
            IsWhisper = isWhisper,
            WhisperRowRef = whisperRowRef ?? "",
            JoinUserId = joinUserId ?? ""
        });
    }

    private async Task RunChatQueueAsync(CancellationToken ct)
    {
        if (_chatWorkChannel == null) return;
        try
        {
            await foreach (var work in _chatWorkChannel.Reader.ReadAllAsync(ct))
                await ProcessChatLineCoreAsync(work.Speaker, work.Message, work.IsWhisper, work.WhisperRowRef, work.JoinUserId, ct);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppendLog("Queue err: " + ex.Message, LogCategory.Error); }
    }

    private async Task ProcessChatLineCoreAsync(string speaker, string msg, bool isWhisper = false, string whisperRowRef = "", string joinUserId = "", CancellationToken ct = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(msg) || !IsBotActive) return;

            if (IsBotOwnMessage(speaker, msg)) return;

            // Greet on join — name from join text, avatar alt, or speaker column; each user once per session
            if (IsJoinLine(msg))
            {
                if (!TryResolveJoiner(speaker, msg, out string joiner) || string.IsNullOrWhiteSpace(joiner))
                {
                    AppendLog("[JOIN skip] no joiner name in: " + msg, LogCategory.Warning);
                    return;
                }

                await HandleJoinGreetAsync(joiner, whisperRowRef, joinUserId, ct);
                return;
            }

            if (!TryGetFirstCommandToken(msg, out string firstCmd)) return;

            string sp = ResolveSpeaker(speaker, msg);
            if (string.IsNullOrEmpty(sp))
            {
                AppendLog($"[CMD skip] no speaker for: {msg}", LogCategory.Warning);
                return;
            }

            string wTag = isWhisper ? " whisper" : "";

            // AI command — only when first token is !bbot (case-insensitive)
            if (string.Equals(firstCmd, AiCommandToken, StringComparison.OrdinalIgnoreCase))
            {
                TryParseBbotCommand(msg, out string aiPrompt);
                string promptLog = string.IsNullOrEmpty(aiPrompt) ? "(no message)" : aiPrompt;
                AppendLog($"[AI{wTag}] {sp}: !bbot {promptLog}", LogCategory.Command);
                await SendToImvuChat(AiMaintenanceReply, isWhisper, whisperRowRef, sp, firstCmd, ct: ct);
                return;
            }

            foreach (var kv in GetActiveCommandReplies())
            {
                if (!CommandMatchesFirstToken(msg, kv.Key)) continue;
                string r = isWhisper ? kv.Value : sp + " " + kv.Value;
                AppendLog($"[CMD{wTag}] {sp}: {firstCmd} → {r}", LogCategory.Command);
                await SendToImvuChat(r, isWhisper, whisperRowRef, sp, firstCmd, ct: ct);
                return;
            }
        }
        catch (Exception ex) { AppendLog("Proc err: " + ex.Message); }
    }

    private async Task HandleJoinGreetAsync(string joiner, string whisperRowRef, string joinUserId = "", CancellationToken ct = default)
    {
        if (!_greetedJoiners.Add(joiner))
        {
            AppendLog($"[JOIN skip] already greeted: {joiner}", LogCategory.Info);
            return;
        }
        var templates = GetCurrentTemplates(_joinEvent);
        string template = templates.Count > 0 ? templates[_random.Next(templates.Count)] : "Welcome {name}!";
        string greet = template.Replace("{name}", joiner);
        AppendLog($"[JOIN] {joiner} → {greet} (in {JoinGreetDelayMs}ms)", LogCategory.Join);
        await Task.Delay(JoinGreetDelayMs, ct);
        if (!IsBotActive) return;
        await SendToImvuChat(greet, ct: ct);

        if (!_welcomeExtra.SendExtra || !IsBotActive) return;
        string extraTemplate = GetWelcomeExtraMessage();
        if (string.IsNullOrWhiteSpace(extraTemplate)) return;

        string extra = extraTemplate.Replace("{name}", joiner);
        string mode = _welcomeExtra.AsWhisper ? "whisper" : "public";
        AppendLog($"[JOIN extra] {joiner} → {extra} ({mode}, in {JoinGreetDelayMs}ms)", LogCategory.Join);
        await Task.Delay(JoinGreetDelayMs, ct);
        if (!IsBotActive) return;
        if (_welcomeExtra.AsWhisper)
        {
            if (string.IsNullOrWhiteSpace(whisperRowRef) && string.IsNullOrWhiteSpace(joinUserId))
                AppendLog("[JOIN extra] whisper skipped — no join row ref or user id", LogCategory.Warning);
            else
                await SendToImvuChat(extra, whisperReply: true, whisperRowRef: whisperRowRef,
                    whisperSpeaker: joiner, proactiveWhisperToUser: true, joinUserId: joinUserId, ct: ct);
        }
        else
            await SendToImvuChat(extra, ct: ct);
    }

    private async Task SendToImvuChat(string t, bool whisperReply = false, string whisperRowRef = "",
        string? whisperSpeaker = null, string? whisperCmd = null, bool proactiveWhisperToUser = false,
        string? joinUserId = null, CancellationToken ct = default)
    {
        if (!IsWebViewReady)
        {
            AppendLog("IMVU browser not ready.", LogCategory.Warning);
            return;
        }
        if (!IsBotActive) return;
        try
        {
            await _chatSendLock.WaitAsync(ct);
        }
        catch (OperationCanceledException) { return; }
        try
        {
            if (!IsBotActive) return;
            string? result = await SendToImvuChatViaWebView(t, whisperReply, whisperRowRef, whisperSpeaker, whisperCmd, proactiveWhisperToUser, joinUserId, ct);
            if (whisperReply && result != "ok")
                AppendLog("Whisper send issue: " + (result ?? "unknown"), LogCategory.Warning);
            if (result == "ok")
                RegisterBotOutbound(t);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { AppendLog("Send err: " + ex.Message, LogCategory.Error); }
        finally { _chatSendLock.Release(); }
    }

    private async Task DiscoverElements()
    {
        if (!IsWebViewReady) return;
        try
        {
            AppendLog("Scanning chat page…", LogCategory.Info);
            var chatInfo = await RunJsStringAsync(FindChatRootJs + """
const r = __imvuFindChatRoot();
if (!r.hasStream) return 'chat-stream2 NOT FOUND (checked iframes)';
const items = r.cont.querySelectorAll('div,li,[class*="msg"],[class*="chat-line"],[class*="message"]');
let cnt = 0;
for (let it of items) { if ((it.innerText||'').trim().length > 2) cnt++; }
return 'chat-stream2 found, ~' + cnt + ' message rows';
""", logErrors: true);
            AppendLog("Chat: " + (chatInfo ?? "?"), LogCategory.Info);

            var inpInfo = await RunJsStringAsync(FindChatRootJs + """
const r = __imvuFindChatRoot();
if (!r.hasInput) return 'input-container NOT FOUND (checked iframes)';
const ic = r.doc.querySelector('div.input-container, [class*="input-container"]');
const child = ic?.querySelector('input, textarea, [contenteditable]');
return child ? 'input found: ' + child.tagName : 'input-container has no editable child';
""", logErrors: true);
            AppendLog("Input: " + (inpInfo ?? "?"), LogCategory.Info);
        }
        catch (Exception ex) { AppendLog("Discover err: " + ex.Message, LogCategory.Error); }
    }

#if false // Legacy window picker + OCR
    // ===================== SIMPLE NATIVE WINDOW + OCR + INPUT (the dropdown way user asked for) =====================

    private void RefreshWindows_Click(object sender, RoutedEventArgs e) => RefreshWindows();

    private void LaunchEdgeDebug_Click(object sender, RoutedEventArgs e)
    {
        _lastDebugBrowser = "edge";
        LaunchBrowserWithDebug("edge", 9222, killExisting: true);
    }

    private async void LaunchChromeDebug_Click(object sender, RoutedEventArgs e)
    {
        _lastDebugBrowser = "chrome";
        KillChromeBotProfileProcesses();
        if (!LaunchBrowserWithDebug("chrome", 9223, killExisting: false))
            return;
        if (await WaitForDebugPortAsync(9223, 25, "chrome"))
            AppendLog("Chrome debug port 9223 is ready. Log into IMVU in this Chrome window (first time only), open chat, Refresh, Start Bot.", LogCategory.Info);
    }

    private bool LaunchBrowserWithDebug(string browser, int port, bool killExisting)
    {
        string exe;
        string profile;
        if (browser == "chrome")
        {
            string[] candidates = {
                @"C:\Program Files\Google\Chrome\Application\chrome.exe",
                @"C:\Program Files (x86)\Google\Chrome\Application\chrome.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Google", "Chrome", "Application", "chrome.exe")
            };
            exe = candidates.FirstOrDefault(File.Exists) ?? "chrome.exe";
            // Chrome blocks remote debugging on the default profile — must use a separate dir.
            profile = ChromeDebugProfileDir();
            try { Directory.CreateDirectory(profile); } catch { }
        }
        else
        {
            string[] candidates = {
                @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe",
                @"C:\Program Files\Microsoft\Edge\Application\msedge.exe",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "Application", "msedge.exe")
            };
            exe = candidates.FirstOrDefault(File.Exists) ?? "msedge.exe";
            profile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "Edge", "User Data");
        }

        if (killExisting)
        {
            AppendLog($"Closing all {browser} windows so debug port {port} can open (login is preserved).", LogCategory.Warning);
            KillChromiumProcesses(browser);
        }
        else if (browser == "chrome")
        {
            AppendLog($"Opening Chrome bot profile at {profile} (regular Chrome can stay open). Log into IMVU here once.", LogCategory.Info);
        }

        string args = $"--user-data-dir=\"{profile}\" --remote-debugging-port={port} --remote-debugging-address=127.0.0.1 --new-window --no-first-run --no-default-browser-check https://www.imvu.com/next/";
        try
        {
            if (!File.Exists(exe))
            {
                AppendLog($"{browser} not found at {exe}. Install it or check path.", LogCategory.Error);
                return false;
            }
            Process.Start(new ProcessStartInfo(exe, args) { UseShellExecute = false });
            AppendLog($"Started {browser} with debug port {port}. Open your chat room, Refresh, select window, Start Bot.", LogCategory.Info);
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("Launch failed: " + ex.Message, LogCategory.Error);
            return false;
        }
    }

    private void TryReselectWindowByTitle(string priorTitle)
    {
        string norm = NormalizeTitle(priorTitle).ToLowerInvariant();
        if (string.IsNullOrEmpty(norm)) return;
        var match = _windows
            .OrderByDescending(w => ScoreTitleMatch(priorTitle, w.Title))
            .FirstOrDefault(w => ScoreTitleMatch(priorTitle, w.Title) >= 10
                || NormalizeTitle(w.Title).ToLowerInvariant().Contains("imvu")
                || NormalizeTitle(w.Title).ToLowerInvariant().Contains("chat"));
        if (match != null)
        {
            WindowCombo.SelectedItem = match;
            _selectedWindow = match;
            _targetHwnd = match.Hwnd;
        }
    }

    private async void WindowCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_switchingWindow) return;
        if (WindowCombo.SelectedItem is not WindowInfo wi || wi.Hwnd == IntPtr.Zero) return;

        _selectedWindow = wi;
        _targetHwnd = wi.Hwnd;
        UpdateStatusText(wi);

        if (!_botRunning)
        {
            AppendLog($"Selected: {wi.Display}", LogCategory.Info);
            return;
        }

        try
        {
            _switchingWindow = true;
            AppendLog($"Switching bot to: {wi.Display}", LogCategory.Info);
            await AttachToSelectedWindowAsync(rebindObserver: true);
            UpdateStatusText(wi);
        }
        catch (Exception ex) { AppendLog("Switch error: " + ex.Message, LogCategory.Error); }
        finally { _switchingWindow = false; }
    }

    private void RefreshWindows()
    {
        _windows.Clear();
        try
        {
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;

                var sb = new StringBuilder(512);
                GetWindowText(hWnd, sb, sb.Capacity);
                string title = sb.ToString().Trim();
                if (string.IsNullOrWhiteSpace(title) || title.Length < 4) return true;

                if (title.IndexOf("IMVU Companion", StringComparison.OrdinalIgnoreCase) >= 0) return true;

                uint pid = 0;
                GetWindowThreadProcessId(hWnd, out pid);
                string procName = "unknown";
                try
                {
                    var p = Process.GetProcessById((int)pid);
                    procName = p.ProcessName.ToLowerInvariant();
                }
                catch { }

                var classSb = new StringBuilder(256);
                GetClassName(hWnd, classSb, classSb.Capacity);
                string className = classSb.ToString();
                bool isChromeClass = className.StartsWith("Chrome_WidgetWin", StringComparison.OrdinalIgnoreCase);

                string tLower = title.ToLowerInvariant();
                bool looksGood = procName.Contains("msedge") || procName.Contains("edge") || procName.Contains("chrome") ||
                                 isChromeClass || procName.Contains("firefox") || tLower.Contains("imvu") || tLower.Contains("chat") ||
                                 tLower.Contains("room") || tLower.Contains("google chrome");
                if (isChromeClass && procName == "unknown") procName = "chrome";

                if (looksGood)
                {
                    RECT r;
                    GetWindowRect(hWnd, out r);
                    _windows.Add(new WindowInfo
                    {
                        Hwnd = hWnd,
                        ProcessId = pid,
                        Title = title,
                        ProcessName = procName,
                        Rect = r
                    });
                }
                return true;
            }, IntPtr.Zero);
        }
        catch (Exception ex) { AppendLog("Enum windows err: " + ex.Message); }

        WindowCombo.ItemsSource = null;
        WindowCombo.ItemsSource = _windows;
        WindowCombo.DisplayMemberPath = nameof(WindowInfo.Display);

        int chromeCount = _windows.Count(w => IsChromeProcess(w.ProcessName));
        int edgeCount = _windows.Count(w => IsEdgeProcess(w.ProcessName));
        AppendLog($"Refreshed: {_windows.Count} windows ({chromeCount} Chrome, {edgeCount} Edge). Select IMVU chat window.", LogCategory.Info);

        if (_selectedWindow != null)
        {
            var keep = _windows.FirstOrDefault(w => w.Hwnd == _selectedWindow.Hwnd);
            if (keep != null) { WindowCombo.SelectedItem = keep; _selectedWindow = keep; UpdateStatusText(keep); return; }
        }

        if (_windows.Count > 0 && WindowCombo.SelectedItem == null)
        {
            IEnumerable<WindowInfo> pool = _windows;
            if (_lastDebugBrowser == "chrome")
                pool = _windows.Where(w => IsChromeProcess(w.ProcessName)).DefaultIfEmpty(_windows.First());
            else if (_lastDebugBrowser == "edge")
                pool = _windows.Where(w => IsEdgeProcess(w.ProcessName)).DefaultIfEmpty(_windows.First());

            var first = pool.FirstOrDefault(w => w.Title.Contains("imvu", StringComparison.OrdinalIgnoreCase) && w.Title.Contains("chat", StringComparison.OrdinalIgnoreCase))
                     ?? pool.FirstOrDefault(w => w.Title.Contains("imvu", StringComparison.OrdinalIgnoreCase))
                     ?? pool.FirstOrDefault();
            if (first != null)
            {
                WindowCombo.SelectedItem = first;
                _selectedWindow = first;
                _targetHwnd = first.Hwnd;
                UpdateStatusText(first);
            }
        }
    }

    private Bitmap? CaptureWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return null;
        RECT r;
        if (!GetWindowRect(hwnd, out r)) return null;
        int w = r.Right - r.Left;
        int h = r.Bottom - r.Top;
        if (w < 50 || h < 50) return null;
        try
        {
            var bmp = new Bitmap(w, h, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(r.Left, r.Top, 0, 0, new System.Drawing.Size(w, h), CopyPixelOperation.SourceCopy);
            }
            return bmp;
        }
        catch { return null; }
    }

    private Bitmap? CropChatArea(Bitmap full)
    {
        if (full == null) return null;
        int w = full.Width;
        int h = full.Height;
        // Heuristic crop for typical browser IMVU chat pane (lower-mid section, most of width)
        int top = Math.Max(0, (int)(h * 0.28));
        int hh = Math.Min(h - top, (int)(h * 0.62));
        int left = Math.Max(0, (int)(w * 0.02));
        int ww = Math.Min(w - left, (int)(w * 0.96));
        try
        {
            return full.Clone(new Rectangle(left, top, ww, hh), full.PixelFormat);
        }
        catch { return full; }
    }

    private async Task<string> PerformOcrAsync(Bitmap bmp)
    {
        if (bmp == null) return string.Empty;
        try
        {
            var engine = OcrEngine.TryCreateFromUserProfileLanguages();
            if (engine == null) return "No OCR language pack installed (add English in Windows Settings > Time & language > Language).";

            using var ms = new MemoryStream();
            bmp.Save(ms, System.Drawing.Imaging.ImageFormat.Bmp);
            ms.Position = 0;
            var ras = ms.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(ras);
            var sbmp = await decoder.GetSoftwareBitmapAsync();
            var result = await engine.RecognizeAsync(sbmp);
            return result?.Text ?? string.Empty;
        }
        catch (Exception ex) { return "OCR error: " + ex.Message; }
    }

    private bool LooksLikeRadioOrNoise(string t)
    {
        if (string.IsNullOrWhiteSpace(t)) return true;
        var low = t.ToLowerInvariant();
        return low.Contains("on air") || low.Contains("radio") || low.Contains("now playing") ||
               low.Contains("http") || low.Contains("www.") || low.Contains("listen") ||
               low.Contains("powered by") || low.Contains("imvu.com") || low.Contains("click");
    }

    private async Task<string[]> GetOcrChatLinesAsync(int max = 10)
    {
        if (_targetHwnd == IntPtr.Zero) return Array.Empty<string>();
        Bitmap? full = null;
        Bitmap? chat = null;
        try
        {
            full = CaptureWindow(_targetHwnd);
            if (full == null) return Array.Empty<string>();
            chat = CropChatArea(full);
            string raw = await PerformOcrAsync(chat ?? full);
            var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                           .Select(l => l.Trim())
                           .Where(l => l.Length > 4 && l.Contains(":") && !LooksLikeRadioOrNoise(l))
                           .Take(max)
                           .ToArray();
            return lines;
        }
        finally
        {
            full?.Dispose();
            chat?.Dispose();
        }
    }

    private System.Drawing.Point ComputeInputClickPoint(IntPtr hwnd)
    {
        RECT r;
        if (!GetWindowRect(hwnd, out r) || (r.Left == 0 && r.Top == 0 && r.Right <= 0))
        {
            AppendLog("Bad or zero window rect for native input click - avoiding (0,0) top-left. Use Launch Edge+Debug + DOM path instead.");
            return new System.Drawing.Point(200, 200);
        }
        int width = r.Right - r.Left;
        int height = r.Bottom - r.Top;
        // Chat input bar is almost always very near the bottom of the browser window content
        int x = r.Left + width / 2;
        int y = r.Top + (int)(height * 0.91); // ~91% down
        return new System.Drawing.Point(x, y);
    }

    private void SendChatMessageNative(IntPtr hwnd, string text)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            SetForegroundWindow(hwnd);
            Thread.Sleep(90);

            var pt = ComputeInputClickPoint(hwnd);
            if (pt.X <= 50 && pt.Y <= 50)
            {
                AppendLog("Native send aborted - would click near (0,0). Use Launch Edge+Debug + DOM path instead.");
                return;
            }
            SetCursorPos(pt.X, pt.Y);
            mouse_event(MOUSEEVENTF_LEFTDOWN, 0, 0, 0, 0);
            Thread.Sleep(25);
            mouse_event(MOUSEEVENTF_LEFTUP, 0, 0, 0, 0);
            Thread.Sleep(140); // give the web input time to focus

            // Feed characters via WM_CHAR (works for most web text inputs)
            foreach (char c in text)
            {
                PostMessage(hwnd, WM_CHAR, new IntPtr(c), IntPtr.Zero);
                Thread.Sleep(4);
            }

            Thread.Sleep(25);
            // Enter
            PostMessage(hwnd, WM_KEYDOWN, new IntPtr(VK_RETURN), IntPtr.Zero);
            Thread.Sleep(8);
            PostMessage(hwnd, WM_KEYUP, new IntPtr(VK_RETURN), IntPtr.Zero);

            AppendLog("Sent (native): " + text);
        }
        catch (Exception ex) { AppendLog("Native send err: " + ex.Message); }
    }

    // ===================== END NATIVE SIMPLE PATH =====================
#endif

    private async Task<string[]> GetChatLinesAsync(int maxLines = 8)
    {
        if (!IsWebViewReady) return Array.Empty<string>();
        try
        {
            var candidates = await RunJsStringArrayAsync(FindChatRootJs + $$"""
const maxLines = {{maxLines}};
const bad = /radio|on air|now playing|http|www\.|listen|click here|powered by|imvu.com/i;
const cont = __imvuFindChatRoot().cont;
    function getSpeakerFromItem(item) {
        if (!item) return '';
        // Prioritize .cs2-name : the user-selected name displayed in chat (as per user research)
        // avatar-name is profile only, not for chat display
        let userCand = item.querySelector('.cs2-name');
        if (!userCand) {
            userCand = item.querySelector('[class*="cs2-name"], [class*="user"], [class*="name"], [class*="avatar"], [data-user], [data-username], [title], .from');
        }
        let sp = '';
        if (userCand) {
            sp = (userCand.textContent || userCand.innerText || '').trim();
            sp = sp.split(/[\n\r\t]/)[0].trim(); // first line, avoid extra
        }
        if (sp.length < 1 || sp.length > 60 || bad.test(sp)) sp = '';
        // If still no, look in previous sibling or parent for name (in case name is in header above message bubble)
        if (!sp) {
            let prev = item.previousElementSibling;
            if (prev) {
                let pUser = prev.querySelector('.cs2-name') || prev.querySelector('[class*="cs2-name"]');
                if (pUser) sp = (pUser.textContent || pUser.innerText || '').trim().split(/[\n\r\t]/)[0].trim();
            }
            if (!sp && item.parentElement) {
                let pUser = item.parentElement.querySelector('.cs2-name') || item.parentElement.querySelector('[class*="cs2-name"]');
                if (pUser) sp = (pUser.textContent || pUser.innerText || '').trim().split(/[\n\r\t]/)[0].trim();
            }
        }
        if (sp.length < 1 || sp.length > 60 || bad.test(sp)) sp = '';
        return sp;
    }
    function getMsgTextFromItem(item, speaker) {
        if (!item) return '';
        // Prefer text/body child, else the item's text minus speaker prefix
        const textCand = item.querySelector('[class*="text"], [class*="body"], [class*="content"], [class*="msg"], p, span:last-child, div:last-child');
        let txt = textCand ? (textCand.innerText || '').trim() : (item.innerText || '').trim();
        // For command messages, ensure we get the part starting with ! from full text (robust to child selection)
        let fullTxt = (item.innerText || '').trim();
        if (fullTxt.includes('!')) {
            let i = fullTxt.indexOf('!');
            txt = fullTxt.substring(i).trim();
        }
        if (speaker && txt.indexOf(speaker) === 0) {
            txt = txt.substring(speaker.length).replace(/^[\s\-:]+/, '').trim();
        }
        return txt;
    }
    function isInteresting(t) {
        t = (t || '').trim();
        if (!t || t.length < 2 || t.length > 300 || bad.test(t)) return false;
        const low = t.toLowerCase();
        if (/left\s+the\s+chat/i.test(low)) return false;
        if (/joined\s+the\s+chat|has\s+joined|joined\s+the\s+room|entered\s+the\s+room|has\s+entered/i.test(low) || t.includes('!')) return true;
        return false;
    }
    let results = [];
    // Find potential message rows (broad but scoped to container)
    const items = cont.querySelectorAll('div,li,[class*="msg"],[class*="chat-line"],[class*="message"],[class*="item"],[class*="bubble"]');
    for (let i = items.length - 1; i >= 0 && results.length < 40; i--) {
        let item = items[i];
        let txt = (item.innerText || '').trim();
        if (!isInteresting(txt)) continue;
        let speaker = getSpeakerFromItem(item);
        let msgText = getMsgTextFromItem(item, speaker);
        if (msgText.length < 1) msgText = txt;
        if (msgText.length < 1) continue;
        results.unshift(speaker + "\t" + msgText);
    }
    if (results.length === 0) {
        // broad fallback: any interesting text, speaker unknown
        const broad = Array.from(cont.querySelectorAll('*')).map(e => (e.innerText || '').trim()).filter(isInteresting);
        for (let t of broad.slice(-20)) {
            results.push("\t" + t);
        }
    }
return results.slice(-maxLines);
""");
            if (candidates == null) return Array.Empty<string>();
            return candidates.Take(maxLines).ToArray();
        }
        catch { return Array.Empty<string>(); }
    }

    private async void InspectChatDom_Click(object sender, RoutedEventArgs e)
    {
        if (!IsWebViewReady)
        {
            AppendLog("IMVU still loading…", LogCategory.Warning);
            return;
        }
        try
        {
            AppendLog("=== DOM inspect ===", LogCategory.Info);
            var chatInspect = await RunJsStringAsync(FindChatRootJs + """
const r = __imvuFindChatRoot();
if (!r.hasStream) return 'NO chat-stream2 (checked iframes)';
const c = r.cont;
let out = 'tag=' + c.tagName + ' rows~' + c.children.length;
const cs2 = Array.from(c.querySelectorAll('.cs2-name')).slice(0,3).map(e => (e.textContent||'').trim());
if (cs2.length) out += ' | names: ' + cs2.join(', ');
return out;
""", logErrors: true);
            AppendLog("Chat: " + (chatInspect ?? "?"), LogCategory.Info);

            var inputInspect = await RunJsStringAsync(FindChatRootJs + """
const r = __imvuFindChatRoot();
if (!r.hasInput) return 'NO input-container (checked iframes)';
const ic = r.doc.querySelector('div.input-container, [class*="input-container"]');
const e = ic?.querySelector('input, textarea, [contenteditable]');
return e ? 'input OK: ' + e.tagName : 'no editable child';
""", logErrors: true);
            AppendLog("Input: " + (inputInspect ?? "?"), LogCategory.Info);

            var whisperInspect = await RunJsStringAsync(FindChatRootJs + """
const r = __imvuFindChatRoot();
const rows = r.cont.querySelectorAll('[class*="msg"], [class*="message"], [class*="chat-line"], li');
const hints = [];
for (let i = rows.length - 1; i >= Math.max(0, rows.length - 8); i--) {
    const row = rows[i];
    const txt = (row.innerText || '').trim().split(/[\n\r]+/)[0].slice(0, 40);
    const cls = (row.className || '').toString().slice(0, 100);
    const isW = /\bwhisper\b/i.test(cls) && !/reply_from/i.test(cls);
    const isP = /\bis-presenter\b/i.test(cls);
    if (isW || isP || /!\S+/.test(row.innerText || '')) {
        hints.push(txt + ' | ' + (isW ? 'WHISPER' : isP ? 'presenter' : '?') + ' | cls=' + cls);
    }
}
return hints.length ? hints.join(' || ') : 'no recent command/whisper rows';
""", logErrors: true);
            AppendLog("Whisper hints: " + (whisperInspect ?? "?"), LogCategory.Info);
        }
        catch (Exception ex) { AppendLog("Inspect err: " + ex.Message, LogCategory.Error); }
    }

    private async void SendTestReply_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureChatPageAsync()) return;
        await SendToImvuChat("Test @ " + DateTime.Now.ToString("HH:mm:ss"));
    }

    private void ExitButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            _exiting = true;
            AppendLog("Proper exit requested.");
            Application.Current.Shutdown();
        }
        catch { Environment.Exit(0); }
    }

    protected override void OnClosed(EventArgs e)
    {
        SaveMessages(); // persist any unsaved template changes
        SaveCommands();
        StopChatQueue();
        _botGlowAnimator?.Stop();
        StopUpdateTimers();
        _aliveTimer?.Stop(); _robustHeartbeatTimer?.Stop(); _botCts?.Cancel();
        base.OnClosed(e);
    }
}
