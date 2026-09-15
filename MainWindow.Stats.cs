using System;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;

namespace IMVUCompanion;

public partial class MainWindow
{
    private readonly DateTime _sessionStartedAt = DateTime.Now;
    private DateTime? _botSessionStartedAt;
    private int _sessionGreetedCount;
    private bool _botSessionTimerRunning;
    private TimeSpan _botSessionPausedElapsed;
    private int _lifetimeGreeted;
    private TimeSpan _lifetimeTime = TimeSpan.Zero;
    private int _lifetimeSessions;
    private bool _lifetimeFlushed = true;
    private bool _showLifetimeStats;

    private static readonly SolidColorBrush StatusSessionGreetedBrush = CreateFrozenBrush(0xF8, 0x9B, 0x1C);
    private static readonly SolidColorBrush StatusSessionTimerBrush = CreateFrozenBrush(0x63, 0xB3, 0x45);
    private static readonly SolidColorBrush StatusLifetimeGreetedBrush = CreateFrozenBrush(0xEE, 0x2F, 0x24);
    private static readonly SolidColorBrush StatusLifetimeTimerBrush = CreateFrozenBrush(0x15, 0x94, 0xD0);
    private static readonly SolidColorBrush StatusClockBrush = CreateFrozenBrush(0xC0, 0xC0, 0xE0);
    private static readonly SolidColorBrush StatusSeparatorBrush = CreateFrozenBrush(0xC0, 0xC0, 0xE0);

    private static SolidColorBrush CreateFrozenBrush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    private void ResetBotSessionStats()
    {
        _botSessionStartedAt = DateTime.Now;
        _sessionGreetedCount = 0;
        _botSessionTimerRunning = true;
        _botSessionPausedElapsed = TimeSpan.Zero;
        UpdateStatusBar();
    }

    private void PauseBotSessionTimer()
    {
        if (_botSessionStartedAt.HasValue && _botSessionTimerRunning)
            _botSessionPausedElapsed = DateTime.Now - _botSessionStartedAt.Value;
        _botSessionTimerRunning = false;
        UpdateStatusBar();
    }

    private void ResumeBotSessionTimer()
    {
        if (!_botSessionStartedAt.HasValue)
            return;
        _botSessionStartedAt = DateTime.Now - _botSessionPausedElapsed;
        _botSessionTimerRunning = true;
        UpdateStatusBar();
    }

    private TimeSpan GetBotSessionElapsed()
    {
        if (!_botSessionStartedAt.HasValue) return TimeSpan.Zero;
        if (_botSessionTimerRunning)
            return DateTime.Now - _botSessionStartedAt.Value;
        return _botSessionPausedElapsed;
    }

    private static string FormatSessionElapsed(TimeSpan elapsed) =>
        $"{(int)elapsed.TotalHours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}";

    private string GetSessionTimerText() => FormatSessionElapsed(GetBotSessionElapsed());

    private string BuildSessionStatsMessage() =>
        $"• IMVU Companion • Online for: {GetSessionTimerText()} • Greeted: {_sessionGreetedCount} Users •";

    private int DisplayLifetimeGreeted() =>
        _lifetimeFlushed ? _lifetimeGreeted : _lifetimeGreeted + _sessionGreetedCount;

    private TimeSpan DisplayLifetimeTime() =>
        _lifetimeFlushed ? _lifetimeTime : _lifetimeTime + GetBotSessionElapsed();

    private string BuildLifetimeStatsMessage() =>
        "• IMVU Companion LifeTime • Online " + _lifetimeSessions + " times for: " +
        FormatSessionElapsed(DisplayLifetimeTime()) + " • Greeted: " + DisplayLifetimeGreeted() + " •";

    private void LoadLifetimeStats()
    {
        try
        {
            int.TryParse(AppDatabase.ReadMeta("lifetime_greeted"), out _lifetimeGreeted);
            int.TryParse(AppDatabase.ReadMeta("lifetime_sessions"), out _lifetimeSessions);
            if (long.TryParse(AppDatabase.ReadMeta("lifetime_time"), out long ticks) && ticks > 0)
                _lifetimeTime = TimeSpan.FromTicks(ticks);
            else
                _lifetimeTime = TimeSpan.Zero;
        }
        catch
        {
            _lifetimeGreeted = 0;
            _lifetimeSessions = 0;
            _lifetimeTime = TimeSpan.Zero;
        }
        _lifetimeFlushed = true;
    }

    private void SaveLifetimeStats()
    {
        try
        {
            AppDatabase.WriteMeta("lifetime_greeted", _lifetimeGreeted.ToString());
            AppDatabase.WriteMeta("lifetime_sessions", _lifetimeSessions.ToString());
            AppDatabase.WriteMeta("lifetime_time", _lifetimeTime.Ticks.ToString());
        }
        catch { }
    }

    private void FlushLifetimeFromSession()
    {
        if (_lifetimeFlushed) return;
        _lifetimeGreeted += _sessionGreetedCount;
        _lifetimeTime += GetBotSessionElapsed();
        _lifetimeFlushed = true;
        SaveLifetimeStats();
    }

    private void StatsModeLabel_Click(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        _showLifetimeStats = !_showLifetimeStats;
        AppendActivityLog(
            _showLifetimeStats
                ? "[Event] Displaying Lifetime Statistics"
                : "[Event] Displaying Current Session Statistics",
            LogCategory.Info);
        UpdateStatusBar();
    }

    private void UpdateStatusBar()
    {
        try
        {
            void writeUi()
            {
                if (StatsModeLabel != null)
                {
                    StatsModeLabel.Text = _showLifetimeStats ? "LIFETIME:" : "SESSION:";
                    StatsModeLabel.Foreground = StatusClockBrush;
                }
                if (AliveText == null) return;
                AliveText.Inlines.Clear();
                string greeted = _showLifetimeStats
                    ? DisplayLifetimeGreeted().ToString()
                    : _sessionGreetedCount.ToString();
                string timer = _showLifetimeStats
                    ? FormatSessionElapsed(DisplayLifetimeTime())
                    : GetSessionTimerText();
                AliveText.Inlines.Add(new Run(greeted)
                {
                    Foreground = _showLifetimeStats ? StatusLifetimeGreetedBrush : StatusSessionGreetedBrush
                });
                AliveText.Inlines.Add(new Run(" - ") { Foreground = StatusSeparatorBrush });
                AliveText.Inlines.Add(new Run(timer)
                {
                    Foreground = _showLifetimeStats ? StatusLifetimeTimerBrush : StatusSessionTimerBrush
                });
                AliveText.Inlines.Add(new Run("   |   ") { Foreground = StatusSeparatorBrush });
                AliveText.Inlines.Add(new Run(DateTime.Now.ToString("MM.dd.yyyy - HH:mm:ss")) { Foreground = StatusClockBrush });
            }
            if (Dispatcher.CheckAccess()) writeUi();
            else Dispatcher.BeginInvoke(writeUi);
        }
        catch { }
    }

    private async void SessionStats_Click(object sender, RoutedEventArgs e)
    {
        if (!await EnsureChatPageAsync()) return;
        await SendToImvuChat(BuildSessionStatsMessage(), requireBotActive: false);
    }
}
