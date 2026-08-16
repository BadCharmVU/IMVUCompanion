using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace IMVUCompanion;

public sealed class RecorderMessageVm
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Time { get; set; } = "";
    public string Text { get; set; } = "";
    public bool IsWhisper { get; set; }
    public string ChannelTag => IsWhisper ? "[W]" : "[P]";
    public string Display => $"{ChannelTag} [{Time}] {Text}";
}

public sealed class RecorderUserVm
{
    public string Name { get; set; } = "";
    public ObservableCollection<RecorderMessageVm> Messages { get; } = new();
}

public sealed class DmMessageEntry
{
    public string Text { get; set; } = "";
    public string Display => Text;
}

public sealed class RoomUserVm
{
    public string Name { get; set; } = "";
    public string UserId { get; set; } = "";
    public string Key { get; set; } = "";
}

public partial class MainWindow
{
    private static readonly string RecorderFile = UserDataPaths.GetConfigFile("recorder.json");
    private static readonly string DmMessagesFile = UserDataPaths.GetConfigFile("dm_messages.json");
    private static readonly SolidColorBrush HeaderActiveGreen = CreateFrozenBrush(0x4A, 0xDE, 0x80);
    private static readonly SolidColorBrush HeaderInactiveRed = CreateFrozenBrush(0xFF, 0x55, 0x55);
    private static readonly SolidColorBrush RoomUserIdleBg = CreateFrozenBrush(0x25, 0x25, 0x40);
    private static readonly SolidColorBrush RoomUserSelectedBg = CreateFrozenBrush(0x3A, 0x4A, 0x7A);
    private static readonly SolidColorBrush RecorderChipPublicBg = CreateFrozenBrush(0x7D, 0xD3, 0xFC);
    private static readonly SolidColorBrush RecorderChipWhisperBg = CreateFrozenBrush(0xD5, 0xA5, 0x48);
    private static readonly SolidColorBrush RecorderChipFg = CreateFrozenBrush(0x0F, 0x0F, 0x1C);
    private static readonly SolidColorBrush RecorderRowHoverBg = CreateFrozenBrush(0x1E, 0x1E, 0x38);
    private static readonly SolidColorBrush RecorderRowSelectedBg = CreateFrozenBrush(0x24, 0x24, 0x44);
    private static readonly SolidColorBrush RecorderSepBrush = CreateFrozenBrush(0x2A, 0x2A, 0x40);
    private static readonly SolidColorBrush RecorderTextFg = CreateFrozenBrush(0xC0, 0xC0, 0xE0);

    private bool _inActiveRoom;
    private bool _recorderEnabled;
    private bool _recorderReady;
    private bool _dmReady;
    private bool _dmUiSyncing;
    private bool _dmAsWhisper;
    private string _recorderTrigger = "RMsg";
    private string? _selectedRoomUserKey;
    private readonly List<RoomUserVm> _roomUsers = new();
    private readonly ObservableCollection<RecorderUserVm> _recorderUsers = new();
    private readonly ObservableCollection<DmMessageEntry> _dmMessages = new();
    private readonly Dictionary<string, RecorderUserVm> _recorderByName =
        new(StringComparer.OrdinalIgnoreCase);
    private int _rosterSeedGen;
    private string? _recorderReplyUser;
    private string? _recorderReplyMessageId;
    private string _selfDetectedName = "";
    private string _selfDetectedUid = "";
    private string? _recorderSelectedMessageId;
    private readonly List<RecorderRowChrome> _recorderRows = new();

    private sealed class RecorderRowChrome
    {
        public string Id { get; init; } = "";
        public Border Inner { get; init; } = null!;
        public TextBlock Body { get; init; } = null!;
        public bool Hover { get; set; }
    }

    private static readonly Regex FirstWordRegex =
        new(@"^(!?\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private void InitRoomTools()
    {
        LoadRecorderSettings();
        LoadDmMessages();
        if (RecorderTriggerBox != null)
            RecorderTriggerBox.Text = _recorderTrigger;
        if (RecorderEnabledCheck != null)
            RecorderEnabledCheck.IsChecked = false;
        _recorderEnabled = false;
        UpdateRecorderHint();
        RefreshRecorderUsersUi();
        RefreshDmMessageCombo();
        UpdateDmSendButton();
        RefreshRoomUsersUi();
        if (RecorderExpander != null)
            RecorderExpander.IsExpanded = false;
        UpdateWelcomeNotGreetingHint();
        UpdateBotNotListeningHint();
    }

    private void DisabledAiSection_Expanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander ex)
            ex.IsExpanded = false;
    }

    private void RecorderExpander_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutRecorderHeader();
    private void RecorderExpander_Layout(object sender, RoutedEventArgs e) => LayoutRecorderHeader();

    private void LayoutRecorderHeader()
    {
        if (RecorderExpander == null || RecorderHeaderRoot == null) return;
        double w = RecorderExpander.ActualWidth - 42;
        if (w > 80)
            RecorderHeaderRoot.Width = w;
    }

    private void DmSettingsExpander_SizeChanged(object sender, SizeChangedEventArgs e) => LayoutDmSettingsHeader();
    private void DmSettingsExpander_Layout(object sender, RoutedEventArgs e) => LayoutDmSettingsHeader();

    private void LayoutDmSettingsHeader()
    {
        if (DmSettingsExpander == null || DmSettingsHeaderRoot == null) return;
        double w = DmSettingsExpander.ActualWidth - 42;
        if (w > 80)
            DmSettingsHeaderRoot.Width = w;
    }

    private void UpdateRecorderHint()
    {
        if (RecorderHint == null) return;
        if (_recorderEnabled)
        {
            RecorderHint.Text = "...Recording";
            RecorderHint.Foreground = HeaderActiveGreen;
        }
        else
        {
            RecorderHint.Text = "...is Not Recording";
            RecorderHint.Foreground = HeaderInactiveRed;
        }
        LayoutRecorderHeader();
    }

    private void RecorderEnabledCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (!_recorderReady) return;
        _recorderEnabled = RecorderEnabledCheck?.IsChecked == true;
        var vis = _recorderEnabled ? Visibility.Visible : Visibility.Collapsed;
        if (RecorderTriggerBox != null)
            RecorderTriggerBox.Visibility = vis;
        if (RecorderTriggerLabel != null)
            RecorderTriggerLabel.Visibility = vis;
        UpdateRecorderHint();
    }

    private void RecorderTriggerBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (!_recorderReady || RecorderTriggerBox == null) return;
        string next = NormalizeRecorderTrigger(RecorderTriggerBox.Text);
        if (string.IsNullOrEmpty(next))
            next = "RMsg";
        _recorderTrigger = next;
        RecorderTriggerBox.Text = next;
        SaveRecorderSettings();
    }

    private static string NormalizeRecorderTrigger(string? raw)
    {
        string t = (raw ?? "").Trim();
        if (t.StartsWith('!'))
            t = t[1..].Trim();
        t = Regex.Replace(t, @"\s+", "");
        return t;
    }

    private void LoadRecorderSettings()
    {
        _recorderReady = false;
        _recorderTrigger = "RMsg";
        _recorderUsers.Clear();
        _recorderByName.Clear();
        try
        {
            if (File.Exists(RecorderFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(RecorderFile));
                var root = doc.RootElement;
                if (root.TryGetProperty("trigger", out var tr))
                {
                    string t = NormalizeRecorderTrigger(tr.GetString());
                    if (!string.IsNullOrEmpty(t))
                        _recorderTrigger = t;
                }
                if (root.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array)
                {
                    foreach (var uel in users.EnumerateArray())
                    {
                        string name = uel.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var user = new RecorderUserVm { Name = name.Trim() };
                        if (uel.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var mel in msgs.EnumerateArray())
                            {
                                string text = mel.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
                                if (string.IsNullOrWhiteSpace(text)) continue;
                                string time = mel.TryGetProperty("time", out var tm) ? tm.GetString() ?? "" : "";
                                bool whisper = mel.TryGetProperty("whisper", out var w) &&
                                               w.ValueKind == JsonValueKind.True;
                                string id = mel.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "";
                                user.Messages.Add(new RecorderMessageVm
                                {
                                    Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id,
                                    Time = FormatRecorderClock(string.IsNullOrWhiteSpace(time) ? null : time),
                                    Text = text,
                                    IsWhisper = whisper
                                });
                            }
                        }
                        if (user.Messages.Count == 0) continue;
                        _recorderUsers.Add(user);
                        _recorderByName[user.Name] = user;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog("Load recorder err: " + ex.Message, LogCategory.Warning);
        }
        _recorderReady = true;
    }

    private void SaveRecorderSettings()
    {
        if (!_recorderReady) return;
        try
        {
            var payload = new
            {
                trigger = _recorderTrigger,
                users = _recorderUsers.Select(u => new
                {
                    name = u.Name,
                    messages = u.Messages.Select(m => new
                    {
                        id = m.Id,
                        time = m.Time,
                        text = m.Text,
                        whisper = m.IsWhisper
                    }).ToList()
                }).ToList()
            };
            File.WriteAllText(RecorderFile, JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppendLog("Save recorder err: " + ex.Message, LogCategory.Warning);
        }
    }

    private static bool TryGetFirstWord(string msg, out string word)
    {
        word = "";
        if (string.IsNullOrWhiteSpace(msg)) return false;
        var m = FirstWordRegex.Match(msg.Trim());
        if (!m.Success) return false;
        word = m.Groups[1].Value;
        return true;
    }

    private bool MatchesRecorderTrigger(string msg)
    {
        string trigger = NormalizeRecorderTrigger(_recorderTrigger);
        if (string.IsNullOrEmpty(trigger) || !TryGetFirstWord(msg, out string word))
            return false;
        word = NormalizeRecorderTrigger(word);
        return string.Equals(word, trigger, StringComparison.OrdinalIgnoreCase);
    }

    private string RecorderPayload(string msg)
    {
        if (!TryGetFirstWord(msg, out string word))
            return (msg ?? "").Trim();
        string rest = msg.Trim();
        if (rest.StartsWith(word, StringComparison.OrdinalIgnoreCase))
            rest = rest[word.Length..].Trim();
        return string.IsNullOrEmpty(rest) ? "(empty)" : rest;
    }

    private static bool IsRecorderChromeLabel(string? text)
    {
        string t = (text ?? "").Trim();
        return t.Equals("whisper", StringComparison.OrdinalIgnoreCase)
            || t.Equals("whispers", StringComparison.OrdinalIgnoreCase)
            || t.Equals("private", StringComparison.OrdinalIgnoreCase)
            || t.Equals("to me", StringComparison.OrdinalIgnoreCase);
    }

    private static string FormatRecorderClock(string? raw)
    {
        string s = (raw ?? "").Trim();
        if (s.Length >= 5 && s[2] == ':')
            return s[..5];
        if (DateTime.TryParse(s, out var dt))
            return dt.ToString("HH:mm");
        return DateTime.Now.ToString("HH:mm");
    }

    private void TryRecordChatMessage(string speaker, string msg, bool isWhisper)
    {
        if (!_recorderEnabled || string.IsNullOrWhiteSpace(msg)) return;
        if (IsBotOwnMessage(speaker, msg))
        {
            RememberSelfFromChat(speaker);
            return;
        }

        bool triggerHit = MatchesRecorderTrigger(msg);
        if (!isWhisper && !triggerHit) return;

        string name = NormalizeSpeaker(speaker);
        if (string.IsNullOrWhiteSpace(name) || !IsValidSpeaker(name)) return;

        string body = triggerHit ? RecorderPayload(msg) : msg.Trim();
        if (IsRecorderChromeLabel(body)) return;
        string stamp = DateTime.Now.ToString("HH:mm");

        if (!_recorderByName.TryGetValue(name, out var user))
        {
            user = new RecorderUserVm { Name = name };
            _recorderByName[name] = user;
            _recorderUsers.Add(user);
        }
        user.Messages.Add(new RecorderMessageVm
        {
            Time = stamp,
            Text = body,
            IsWhisper = isWhisper
        });
        SaveRecorderSettings();
        RefreshRecorderUsersUi();
    }

    private void RefreshRecorderUsersUi()
    {
        if (RecorderUsersPanel == null) return;
        var open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var child in RecorderUsersPanel.Children)
        {
            if (child is Expander { IsExpanded: true, Header: string hdr })
                open.Add(hdr);
        }

        _recorderRows.Clear();
        RecorderUsersPanel.Children.Clear();
        foreach (var user in _recorderUsers)
        {
            var expander = new Expander
            {
                Style = TryFindResource("RecorderUserExpander") as Style,
                Header = user.Name,
                IsExpanded = open.Contains(user.Name),
                Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xE0, 0xFF)),
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x14, 0x28)),
                Margin = new Thickness(0, 0, 0, 4)
            };
            var list = new StackPanel();
            foreach (var line in user.Messages)
            {
                var (outer, chrome) = BuildRecorderMessageRow(user, line);
                _recorderRows.Add(chrome);
                ApplyRecorderRowState(chrome);
                list.Children.Add(outer);
            }
            expander.Content = list;
            RecorderUsersPanel.Children.Add(expander);
        }
    }

    private (Border Outer, RecorderRowChrome Chrome) BuildRecorderMessageRow(
        RecorderUserVm user, RecorderMessageVm line)
    {
        var outer = new Border
        {
            Background = Brushes.Transparent,
            BorderBrush = RecorderSepBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            MinHeight = 26,
            Padding = new Thickness(0),
            SnapsToDevicePixels = true
        };
        var inner = new Border
        {
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        var grid = new Grid { MinHeight = 25 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(52) });

        var chip = new Border
        {
            Width = 20,
            Height = 20,
            CornerRadius = new CornerRadius(4),
            Background = line.IsWhisper ? RecorderChipWhisperBg : RecorderChipPublicBg,
            Margin = new Thickness(6, 3, 4, 3),
            IsHitTestVisible = false,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        chip.Child = new TextBlock
        {
            Text = line.IsWhisper ? "W" : "P",
            Foreground = RecorderChipFg,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        double timeW = MeasureRecorderStampWidth();
        var time = new TextBlock
        {
            Text = $"[{FormatRecorderClock(line.Time)}]",
            Foreground = RecorderTextFg,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            HorizontalAlignment = HorizontalAlignment.Right,
            Width = timeW,
            Margin = new Thickness(4, 5, 0, 0),
            IsHitTestVisible = false
        };

        var body = new TextBlock
        {
            Text = line.Text,
            Foreground = RecorderTextFg,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(8, 5, 6, 4),
            TextWrapping = TextWrapping.NoWrap,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 4, 0)
        };
        var replyBtn = new Button
        {
            Content = "↩",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 12,
            ToolTip = "Reply",
            Tag = (user, line),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xA0, 0xB0, 0xFF)),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false
        };
        replyBtn.Click += RecorderReply_Click;
        var delBtn = new Button
        {
            Content = "🗑",
            Width = 22,
            Height = 22,
            Padding = new Thickness(0),
            FontSize = 11,
            ToolTip = "Delete",
            Tag = (user, line),
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = new SolidColorBrush(Color.FromRgb(0xF8, 0x71, 0x71)),
            Cursor = System.Windows.Input.Cursors.Hand,
            Focusable = false
        };
        delBtn.Click += RecorderDelete_Click;
        actions.Children.Add(replyBtn);
        actions.Children.Add(delBtn);

        Grid.SetColumn(chip, 0);
        Grid.SetColumn(time, 1);
        Grid.SetColumn(body, 2);
        Grid.SetColumn(actions, 3);
        grid.Children.Add(chip);
        grid.Children.Add(time);
        grid.Children.Add(body);
        grid.Children.Add(actions);
        inner.Child = grid;
        outer.Child = inner;

        var chrome = new RecorderRowChrome
        {
            Id = line.Id,
            Inner = inner,
            Body = body
        };
        inner.MouseEnter += (_, _) =>
        {
            chrome.Hover = true;
            ApplyRecorderRowState(chrome);
        };
        inner.MouseLeave += (_, _) =>
        {
            chrome.Hover = false;
            ApplyRecorderRowState(chrome);
        };
        inner.MouseLeftButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject src &&
                (src is Button || FindParentButton(src) != null))
                return;
            _recorderSelectedMessageId = line.Id;
            foreach (var row in _recorderRows)
                ApplyRecorderRowState(row);
            e.Handled = true;
        };

        return (outer, chrome);
    }

    private static Button? FindParentButton(DependencyObject? start)
    {
        for (var d = start; d != null; d = VisualTreeHelper.GetParent(d))
        {
            if (d is Button b) return b;
        }
        return null;
    }

    private void ApplyRecorderRowState(RecorderRowChrome row)
    {
        bool selected = string.Equals(row.Id, _recorderSelectedMessageId, StringComparison.Ordinal);
        bool expand = selected || row.Hover;
        if (selected)
            row.Inner.Background = RecorderRowSelectedBg;
        else if (row.Hover)
            row.Inner.Background = RecorderRowHoverBg;
        else
            row.Inner.Background = Brushes.Transparent;

        if (expand)
        {
            row.Body.TextWrapping = TextWrapping.Wrap;
            row.Body.TextTrimming = TextTrimming.None;
            row.Body.Height = double.NaN;
        }
        else
        {
            row.Body.TextWrapping = TextWrapping.NoWrap;
            row.Body.TextTrimming = TextTrimming.CharacterEllipsis;
            row.Body.Height = 16;
        }
    }

    private void RecorderReply_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: (RecorderUserVm user, RecorderMessageVm msg) }) return;
        _recorderReplyUser = user.Name;
        _recorderReplyMessageId = msg.Id;
        if (RecorderReplyTitle != null)
            RecorderReplyTitle.Text = "Reply to " + user.Name;
        if (RecorderReplyBox != null)
            RecorderReplyBox.Text = "";
        UpdateRecorderReplyCount();
        if (RecorderReplyModal != null)
            RecorderReplyModal.Visibility = Visibility.Visible;
    }

    private void RecorderReplyBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateRecorderReplyCount();

    private void UpdateRecorderReplyCount()
    {
        if (RecorderReplyCount == null) return;
        int n = RecorderReplyBox?.Text?.Length ?? 0;
        if (n > 1024)
            n = 1024;
        RecorderReplyCount.Text = n.ToString() + "/1024";
    }

    private double MeasureRecorderStampWidth()
    {
        double dip = 1.0;
        try { dip = VisualTreeHelper.GetDpi(this).PixelsPerDip; } catch { }
        var ft = new FormattedText(
            "[44:44]",
            CultureInfo.CurrentCulture,
            FlowDirection.LeftToRight,
            new Typeface(SystemFonts.MessageFontFamily, FontStyles.Normal, FontWeights.Normal, FontStretches.Normal),
            12,
            RecorderTextFg,
            dip);
        return Math.Ceiling(ft.Width);
    }

    private void RecorderReplyDev_Click(object sender, RoutedEventArgs e)
    {
        if (RecorderReplyModal != null)
            RecorderReplyModal.Visibility = Visibility.Collapsed;
        if (RecorderReplyBox != null)
            RecorderReplyBox.Text = "";
        UpdateRecorderReplyCount();
    }

    private void RecorderDelete_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: (RecorderUserVm user, RecorderMessageVm msg) }) return;
        if (_recorderSelectedMessageId == msg.Id)
            _recorderSelectedMessageId = null;
        user.Messages.Remove(msg);
        if (user.Messages.Count == 0)
        {
            _recorderUsers.Remove(user);
            _recorderByName.Remove(user.Name);
        }
        SaveRecorderSettings();
        RefreshRecorderUsersUi();
    }

    private static readonly string[] DefaultDmTexts =
    {
        "This is 1st Notice.",
        "This is final Notice."
    };

    private void LoadDmMessages()
    {
        _dmReady = false;
        _dmAsWhisper = false;
        _dmMessages.Clear();
        try
        {
            if (File.Exists(DmMessagesFile))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(DmMessagesFile));
                var root = doc.RootElement;
                if (root.TryGetProperty("asWhisper", out var aw) &&
                    (aw.ValueKind == JsonValueKind.True || aw.ValueKind == JsonValueKind.False))
                    _dmAsWhisper = aw.GetBoolean();
                else if (root.TryGetProperty("delivery", out var del) &&
                         string.Equals(del.GetString(), "whisper", StringComparison.OrdinalIgnoreCase))
                    _dmAsWhisper = true;

                if (root.TryGetProperty("messages", out var arr) &&
                    arr.ValueKind == JsonValueKind.Array)
                {
                    foreach (var el in arr.EnumerateArray())
                    {
                        string text = el.ValueKind == JsonValueKind.String
                            ? el.GetString() ?? ""
                            : el.TryGetProperty("text", out var t) ? t.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(text)) continue;
                        _dmMessages.Add(new DmMessageEntry { Text = text.Trim() });
                    }
                }
            }
        }
        catch (Exception ex)
        {
            AppendLog("Load DM messages err: " + ex.Message, LogCategory.Warning);
        }

        if (_dmMessages.Count == 0)
        {
            foreach (string text in DefaultDmTexts)
                _dmMessages.Add(new DmMessageEntry { Text = text });
            _dmReady = true;
            SaveDmMessages();
        }
        else
        {
            _dmReady = true;
        }
    }

    private void SaveDmMessages()
    {
        if (!_dmReady) return;
        try
        {
            var payload = new
            {
                asWhisper = _dmAsWhisper,
                delivery = _dmAsWhisper ? "whisper" : "public",
                messages = _dmMessages.Select(m => new { text = m.Text }).ToList()
            };
            File.WriteAllText(DmMessagesFile, JsonSerializer.Serialize(payload,
                new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            AppendLog("Save DM messages err: " + ex.Message, LogCategory.Warning);
        }
    }

    private void RefreshDmMessageCombo()
    {
        if (DmMessageCombo == null) return;
        _dmUiSyncing = true;
        try
        {
            int keep = DmMessageCombo.SelectedIndex;
            DmMessageCombo.ItemsSource = null;
            DmMessageCombo.ItemsSource = _dmMessages;
            DmMessageCombo.DisplayMemberPath = nameof(DmMessageEntry.Display);
            if (_dmMessages.Count == 0)
                DmMessageCombo.SelectedIndex = -1;
            else if (keep >= 0 && keep < _dmMessages.Count)
                DmMessageCombo.SelectedIndex = keep;
            else
                DmMessageCombo.SelectedIndex = 0;
            UpdateDmSendButton();
        }
        finally { _dmUiSyncing = false; }
    }

    private void UpdateDmSendButton()
    {
        if (DmSendBtn == null) return;
        DmSendBtn.Content = _dmAsWhisper ? "Send Whisper" : "Send Public";
    }

    private void DmMessageCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_dmUiSyncing) return;
        UpdateDmSendButton();
    }

    private void DmEditMessages_Click(object sender, RoutedEventArgs e)
    {
        if (DmEditModal == null) return;
        RefreshDmEditList();
        if (DmEditBox != null) DmEditBox.Text = "";
        SelectDeliveryCombo(DmEditDeliveryCombo, _dmAsWhisper);
        DmEditModal.Visibility = Visibility.Visible;
    }

    private void DmEditDone_Click(object sender, RoutedEventArgs e)
    {
        if (DmEditDeliveryCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            _dmAsWhisper = tag == "whisper";
        SaveDmMessages();
        UpdateDmSendButton();
        if (DmEditModal != null)
            DmEditModal.Visibility = Visibility.Collapsed;
        RefreshDmMessageCombo();
    }

    private void RefreshDmEditList()
    {
        if (DmEditList == null) return;
        DmEditList.ItemsSource = null;
        DmEditList.ItemsSource = _dmMessages.Select(m => m.Text).ToList();
    }

    private void DmEditList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int i = DmEditList?.SelectedIndex ?? -1;
        if (i < 0 || i >= _dmMessages.Count) return;
        if (DmEditBox != null) DmEditBox.Text = _dmMessages[i].Text;
    }

    private bool TryReadDmEditor(out string text)
    {
        text = DmEditBox?.Text?.Trim() ?? "";
        return !string.IsNullOrWhiteSpace(text);
    }

    private void DmEditAdd_Click(object sender, RoutedEventArgs e)
    {
        if (!TryReadDmEditor(out string text)) return;
        _dmMessages.Add(new DmMessageEntry { Text = text });
        SaveDmMessages();
        RefreshDmEditList();
        RefreshDmMessageCombo();
        if (DmEditBox != null) DmEditBox.Text = "";
    }

    private void DmEditUpdate_Click(object sender, RoutedEventArgs e)
    {
        int i = DmEditList?.SelectedIndex ?? -1;
        if (i < 0 || i >= _dmMessages.Count) return;
        if (!TryReadDmEditor(out string text)) return;
        _dmMessages[i].Text = text;
        SaveDmMessages();
        RefreshDmEditList();
        RefreshDmMessageCombo();
        if (DmEditList != null) DmEditList.SelectedIndex = i;
    }

    private void DmEditDelete_Click(object sender, RoutedEventArgs e)
    {
        int i = DmEditList?.SelectedIndex ?? -1;
        if (i < 0 || i >= _dmMessages.Count) return;
        _dmMessages.RemoveAt(i);
        if (_dmMessages.Count == 0)
        {
            foreach (string text in DefaultDmTexts)
                _dmMessages.Add(new DmMessageEntry { Text = text });
        }
        SaveDmMessages();
        RefreshDmEditList();
        RefreshDmMessageCombo();
        if (DmEditBox != null) DmEditBox.Text = "";
    }

    private async void DmSend_Click(object sender, RoutedEventArgs e)
    {
        if (DmMessageCombo?.SelectedItem is not DmMessageEntry msg)
            return;
        var user = SelectedRoomUser();
        if (user == null)
        {
            AppendLog("Select a user in the room first.", LogCategory.Warning);
            return;
        }
        if (!await IsActiveRoomPresentAsync())
        {
            AppendLog("No active room — cannot send.", LogCategory.Warning);
            return;
        }

        string body = ApplyMessageTemplate(msg.Text, user.Name);
        string? result;
        if (_dmAsWhisper)
        {
            result = await SendToImvuChat(body, whisperReply: true, whisperSpeaker: user.Name,
                proactiveWhisperToUser: true, joinUserId: user.UserId,
                requireBotActive: false, logSend: false);
            if (result == "ok")
                AppendActivityLog($"[W.DM] {user.Name} {body}", LogCategory.WhisperDm);
            else
                AppendLog("Whisper DM failed: " + (result ?? "unknown"), LogCategory.Warning);
        }
        else
        {
            result = await SendToImvuChat(body, requireBotActive: false, logSend: false);
            if (result == "ok")
                AppendActivityLog($"[P.DM] {user.Name} {body}", LogCategory.PublicDm);
            else
                AppendLog("Public DM failed: " + (result ?? "unknown"), LogCategory.Warning);
        }
    }

    private RoomUserVm? SelectedRoomUser()
    {
        if (string.IsNullOrEmpty(_selectedRoomUserKey)) return null;
        return _roomUsers.FirstOrDefault(u => u.Key == _selectedRoomUserKey);
    }

    private bool IsSelfName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;
        string fold = FoldImvuName(name);
        if (string.IsNullOrEmpty(fold)) return false;
        if (!string.IsNullOrWhiteSpace(_botDisplayName) &&
            fold == FoldImvuName(_botDisplayName))
            return true;
        if (!string.IsNullOrWhiteSpace(_selfDetectedName) &&
            fold == FoldImvuName(_selfDetectedName))
            return true;
        return false;
    }

    private bool IsSelfUserId(string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(_selfDetectedUid))
            return false;
        return string.Equals(userId.Trim(), _selfDetectedUid.Trim(), StringComparison.Ordinal);
    }

    private void RememberSelfFromChat(string speaker)
    {
        string name = NormalizeSpeaker(speaker);
        if (string.IsNullOrWhiteSpace(name) || !IsValidSpeaker(name)) return;
        if (string.Equals(FoldImvuName(_selfDetectedName), FoldImvuName(name), StringComparison.Ordinal))
            return;
        _selfDetectedName = name.Trim();
        PruneSelfFromRoster();
    }

    private void PruneSelfFromRoster()
    {
        int removed = _roomUsers.RemoveAll(u => IsSelfName(u.Name) || IsSelfUserId(u.UserId));
        if (_selectedRoomUserKey != null &&
            _roomUsers.All(u => u.Key != _selectedRoomUserKey))
            _selectedRoomUserKey = null;
        if (removed > 0)
            RefreshRoomUsersUi();
    }

    private async Task RefreshSelfIdentityAsync()
    {
        if (!IsWebViewReady) return;
        try
        {
            string? raw = await RunJsStringAsync(
                "return (typeof window.__imvuSelfIdentity === 'function') ? window.__imvuSelfIdentity() : '';",
                logErrors: false);
            if (string.IsNullOrWhiteSpace(raw)) return;
            var parts = raw.Split('\t');
            string name = parts.Length > 0 ? parts[0].Trim() : "";
            string uid = parts.Length > 1 ? parts[1].Trim() : "";
            bool changed = false;
            if (!string.IsNullOrEmpty(name) &&
                !string.Equals(FoldImvuName(_selfDetectedName), FoldImvuName(name), StringComparison.Ordinal))
            {
                _selfDetectedName = name;
                changed = true;
            }
            if (!string.IsNullOrEmpty(uid) &&
                !string.Equals(_selfDetectedUid, uid, StringComparison.Ordinal))
            {
                _selfDetectedUid = uid;
                changed = true;
            }
            if (changed)
                PruneSelfFromRoster();
        }
        catch { }
    }

    private static string RoomUserKey(string name) => FoldImvuName(name);

    private void AddOrUpdateRoomUser(string name, string? userId)
    {
        name = SanitizeJoinerName(name);
        if (string.IsNullOrWhiteSpace(name) || IsSelfName(name) || IsSelfUserId(userId)) return;
        string key = RoomUserKey(name);
        if (string.IsNullOrEmpty(key)) return;

        var existing = _roomUsers.FirstOrDefault(u => u.Key == key);
        if (existing == null)
        {
            _roomUsers.Add(new RoomUserVm
            {
                Name = name,
                UserId = userId?.Trim() ?? "",
                Key = key
            });
            _roomUsers.Sort(static (a, b) =>
                string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            RefreshRoomUsersUi();
            return;
        }

        if (!string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(existing.UserId))
            existing.UserId = userId.Trim();
        if (!string.Equals(existing.Name, name, StringComparison.Ordinal))
        {
            existing.Name = name;
            RefreshRoomUsersUi();
        }
    }

    private void RemoveRoomUser(string name)
    {
        string key = RoomUserKey(name);
        if (string.IsNullOrEmpty(key)) return;
        int removed = _roomUsers.RemoveAll(u => u.Key == key);
        if (removed == 0) return;
        if (_selectedRoomUserKey == key)
            _selectedRoomUserKey = null;
        RefreshRoomUsersUi();
    }

    private void ClearRoomRoster()
    {
        if (_roomUsers.Count == 0 && _selectedRoomUserKey == null) return;
        _roomUsers.Clear();
        _selectedRoomUserKey = null;
        RefreshRoomUsersUi();
    }

    private void RefreshRoomUsersUi()
    {
        if (DmUsersGrid == null) return;
        DmUsersGrid.Children.Clear();
        foreach (var user in _roomUsers)
        {
            bool selected = user.Key == _selectedRoomUserKey;
            var btn = new Button
            {
                Content = user.Name,
                Style = TryFindResource("RoomUserChip") as Style,
                Tag = user.Key,
                Background = selected ? RoomUserSelectedBg : RoomUserIdleBg,
                BorderBrush = selected
                    ? new SolidColorBrush(Color.FromRgb(0x70, 0x80, 0xC0))
                    : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x58))
            };
            btn.Click += RoomUserChip_Click;
            DmUsersGrid.Children.Add(btn);
        }
    }

    private void RoomUserChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string key) return;
        _selectedRoomUserKey = _selectedRoomUserKey == key ? null : key;
        RefreshRoomUsersUi();
    }

    private void HandleRoomChatEvent(string speaker, string text, string kind, string joinUserId, bool isWhisper = false)
    {
        if (string.Equals(kind, "leave", StringComparison.OrdinalIgnoreCase))
        {
            string name = SanitizeJoinerName(speaker);
            if (string.IsNullOrWhiteSpace(name) && TryParseLeaveName(text, out string parsed))
                name = parsed;
            if (!string.IsNullOrWhiteSpace(name))
            {
                RemoveRoomUser(name);
                AppendActivityLog($"[LEFT] {name} left the chat", LogCategory.Left);
            }
            return;
        }

        if (string.Equals(kind, "present", StringComparison.OrdinalIgnoreCase))
        {
            string name = SanitizeJoinerName(speaker);
            if (string.IsNullOrWhiteSpace(name) && TryParsePresentName(text, out string parsed))
                name = parsed;
            if (!string.IsNullOrWhiteSpace(name))
                AddOrUpdateRoomUser(name, joinUserId);
            return;
        }

        if (ContainsJoinLine(text))
        {
            if (TryResolveJoiner(speaker, text, out string joiner) && !string.IsNullOrWhiteSpace(joiner)
                && !IsSelfName(joiner) && !IsSelfUserId(joinUserId))
            {
                AddOrUpdateRoomUser(joiner, joinUserId);
                string uidLabel = string.IsNullOrWhiteSpace(joinUserId) ? "?" : joinUserId;
                AppendActivityLog($"[JOIN] uId={uidLabel}: {joiner} | Joined the chat", LogCategory.Join);
            }
        }

        if (string.Equals(kind, "chat", StringComparison.OrdinalIgnoreCase) ||
            kind == "0" || kind == "1")
        {
            if (IsBotOwnMessage(speaker, text))
                RememberSelfFromChat(speaker);
            TryRecordChatMessage(speaker, text, isWhisper || kind == "1");
        }
    }

    private static bool TryParseLeaveName(string msg, out string name)
    {
        name = "";
        if (string.IsNullOrWhiteSpace(msg)) return false;
        var m = Regex.Match(msg.Trim(), @"^!?\s*(.+?)\s+left\s+the\s+chat\s*\.?\s*$",
            RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        name = SanitizeJoinerName(m.Groups[1].Value);
        return !string.IsNullOrWhiteSpace(name);
    }

    private static bool TryParsePresentName(string msg, out string name)
    {
        name = "";
        if (string.IsNullOrWhiteSpace(msg)) return false;
        if (Regex.IsMatch(msg, @"is\s+now\s+in\s+the\s+chat", RegexOptions.IgnoreCase))
            return false;
        var m = Regex.Match(msg.Trim(), @"^!?\s*(.+?)\s+is\s+in\s+the\s+chat\s*\.?\s*$",
            RegexOptions.IgnoreCase);
        if (!m.Success) return false;
        name = SanitizeJoinerName(m.Groups[1].Value);
        return !string.IsNullOrWhiteSpace(name);
    }

    private async Task CheckRoomPresenceAsync()
    {
        if (!IsWebViewReady || _isShuttingDown) return;
        if ((DateTime.UtcNow - _lastRoomCheckUtc).TotalSeconds < 2) return;
        _lastRoomCheckUtc = DateTime.UtcNow;

        bool room = await IsActiveRoomPresentAsync();
        if (room && !_inActiveRoom)
        {
            _inActiveRoom = true;
            await EnsureRoomObserverAsync();
            await RefreshSelfIdentityAsync();
            _ = ReseedRosterAfterEnterAsync();
        }
        else if (!room && _inActiveRoom)
        {
            _inActiveRoom = false;
            _selfDetectedName = "";
            _selfDetectedUid = "";
            ClearRoomRoster();
            if (!_botRunning)
            {
                try { await TeardownChatObserverWebView(); } catch { }
                _observerBoundUrl = null;
            }
        }

        if (_botRunning)
        {
            if (!room && !_botPausedNoRoom)
                await PauseBotForMissingRoomAsync();
            else if (room && _botPausedNoRoom)
                await ResumeBotAfterRoomAsync();
        }
    }

    private async Task EnsureRoomObserverAsync()
    {
        if (!IsWebViewReady) return;
        try
        {
            await SetupChatObserver();
        }
        catch (Exception ex)
        {
            AppendLog("Room observer: " + ex.Message, LogCategory.Warning);
        }
    }

    private async Task ReseedRosterAfterEnterAsync()
    {
        int gen = ++_rosterSeedGen;
        int[] delays = { 400, 1000, 2000, 3500, 6000 };
        foreach (int delay in delays)
        {
            try { await Task.Delay(delay); }
            catch { return; }
            if (_isShuttingDown || !_inActiveRoom || gen != _rosterSeedGen) return;
            try
            {
                await RefreshSelfIdentityAsync();
                await RunJsVoidAsync(
                    "if (typeof window.__imvuReseedPresence === 'function') window.__imvuReseedPresence();",
                    logErrors: false);
            }
            catch { }
        }
    }
}
