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
    public string UserId { get; set; } = "";
    public int ReceiptIndex { get; set; }
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

    private readonly Dictionary<string, List<string>> _consoleByLang =
        new(StringComparer.OrdinalIgnoreCase);
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
    private bool _chipMessagePrefix = true;
    private string _recorderTrigger = "RMsg";
    private string? _selectedRoomUserKey;
    private readonly List<RoomUserVm> _roomUsers = new();
    private readonly Dictionary<string, int> _roomEnterCounts = new(StringComparer.Ordinal);
    private readonly ObservableCollection<RecorderUserVm> _recorderUsers = new();
    private readonly ObservableCollection<DmMessageEntry> _dmMessages = new();
    private readonly Dictionary<string, RecorderUserVm> _recorderByName =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RecorderUserVm> _recorderByUid =
        new(StringComparer.Ordinal);
    private bool _confirmReceipt = true;
    private readonly List<string> _receiptMessages = new();
    private readonly Dictionary<string, List<string>> _receiptByLang =
        new(StringComparer.OrdinalIgnoreCase);
    private int _rosterSeedGen;
    private string? _recorderReplyUser;
    private string? _recorderReplyMessageId;
    private string _selfDetectedName = "";
    private string _selfDetectedUid = "";
    private string? _recorderSelectedMessageId;
    private readonly List<RecorderRowChrome> _recorderRows = new();
    private RoomUserVm? _chipMessageUser;
    private readonly List<PendingRemove> _pendingRemoves = new();

    private sealed class PendingRemove
    {
        public string Name { get; init; } = "";
        public string Uid { get; init; } = "";
        public DateTime Utc { get; init; } = DateTime.UtcNow;
        public bool Logged { get; set; }
    }

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
        if (RecorderAnswerGearBtn != null)
            RecorderAnswerGearBtn.Visibility = vis;
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
        _recorderByUid.Clear();
        _confirmReceipt = true;
        _receiptMessages.Clear();
        _receiptByLang.Clear();
        try
        {
            var data = AppDatabase.LoadRecorder();
            string t = NormalizeRecorderTrigger(data.Trigger);
            if (!string.IsNullOrEmpty(t))
                _recorderTrigger = t;
            _confirmReceipt = data.ConfirmReceipt;
            foreach (var row in data.Users)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Name)) continue;
                var user = new RecorderUserVm
                {
                    Name = row.Name.Trim(),
                    UserId = (row.UserId ?? "").Trim(),
                    ReceiptIndex = Math.Max(0, row.ReceiptIndex)
                };
                foreach (var m in row.Messages)
                {
                    if (m == null || string.IsNullOrWhiteSpace(m.Text)) continue;
                    user.Messages.Add(new RecorderMessageVm
                    {
                        Id = string.IsNullOrWhiteSpace(m.Id) ? Guid.NewGuid().ToString("N") : m.Id,
                        Time = FormatRecorderClock(string.IsNullOrWhiteSpace(m.Time) ? null : m.Time),
                        Text = m.Text,
                        IsWhisper = m.IsWhisper
                    });
                }
                if (user.Messages.Count == 0) continue;
                _recorderUsers.Add(user);
                _recorderByName[user.Name] = user;
                if (!string.IsNullOrEmpty(user.UserId))
                    _recorderByUid[user.UserId] = user;
            }
            foreach (var kv in data.AnsweringByLang)
            {
                if (kv.Value != null && kv.Value.Count > 0)
                    PutAnsweringLang(kv.Key, kv.Value);
            }
        }
        catch (Exception ex)
        {
            AppendLog("Load recorder err: " + ex.Message, LogCategory.Warning);
        }
        foreach (string lang in AppLanguageCodes())
            SeedAnsweringLanguageIfEmpty(lang);
        ApplyReceiptLanguage(string.IsNullOrEmpty(_currentLanguage) ? "en" : _currentLanguage);
        _recorderReady = true;
        SaveRecorderSettings();
    }

    private void SaveRecorderSettings()
    {
        if (!_recorderReady) return;
        try
        {
            SyncReceiptViewToStore();
            var langs = new HashSet<string>(AppLanguageCodes(), StringComparer.OrdinalIgnoreCase);
            foreach (string k in _receiptByLang.Keys) langs.Add(k);
            foreach (string lang in langs)
                SeedAnsweringLanguageIfEmpty(lang);
            var answering = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in _receiptByLang)
                answering[kv.Key] = CopyTexts(kv.Value);
            AppDatabase.SaveRecorder(new AppDatabase.RecorderData
            {
                Trigger = _recorderTrigger,
                ConfirmReceipt = _confirmReceipt,
                Users = _recorderUsers.Select(u => new AppDatabase.RecorderUserData
                {
                    Name = u.Name,
                    UserId = u.UserId,
                    ReceiptIndex = u.ReceiptIndex,
                    Messages = u.Messages.Select(m => new AppDatabase.RecorderMessageData
                    {
                        Id = m.Id,
                        Time = m.Time,
                        Text = m.Text,
                        IsWhisper = m.IsWhisper
                    }).ToList()
                }).ToList(),
                AnsweringByLang = answering
            });
        }
        catch (Exception ex)
        {
            AppendLog("Save recorder err: " + ex.Message, LogCategory.Warning);
        }
    }

    private static List<string> CopyTexts(IEnumerable<string>? src)
    {
        var list = new List<string>();
        if (src == null) return list;
        foreach (string t in src)
        {
            if (!string.IsNullOrWhiteSpace(t))
                list.Add(t.Trim());
        }
        return list;
    }

    private static List<string> DefaultAnsweringTexts(string lang) =>
        string.Equals(lang, "ru", StringComparison.OrdinalIgnoreCase)
            ? new List<string>
            {
                "ваше сообщение получено.",
                "и это сообщение тоже получено.",
                "...ещё много их будет? все зафиксирую!"
            }
            : new List<string>
            {
                "your message was captured.",
                "and this message was captured",
                "...will there be more of them? I'll record everything!"
            };

    private static bool IsLegacyFactoryAnswering(List<string>? list)
    {
        if (list == null || list.Count == 0) return true;
        if (list.Count != 1) return false;
        string t = list[0].Trim();
        return t.Equals("Your message was captured.", StringComparison.Ordinal)
            || t.Equals("Ваше сообщение получено.", StringComparison.Ordinal);
    }

    private void PutAnsweringLang(string lang, IEnumerable<string>? texts)
    {
        string code = string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim().ToLowerInvariant();
        _receiptByLang[code] = CopyTexts(texts);
    }

    private void SeedAnsweringLanguageIfEmpty(string lang)
    {
        string code = string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim().ToLowerInvariant();
        if (!_receiptByLang.TryGetValue(code, out var list) || IsLegacyFactoryAnswering(list))
            PutAnsweringLang(code, DefaultAnsweringTexts(code));
    }

    private void SyncReceiptViewToStore()
    {
        string lang = string.IsNullOrEmpty(_currentLanguage) ? "en" : _currentLanguage;
        PutAnsweringLang(lang, _receiptMessages);
    }

    private void ApplyReceiptLanguage(string lang)
    {
        string code = string.IsNullOrWhiteSpace(lang) ? "en" : lang.Trim().ToLowerInvariant();
        SeedAnsweringLanguageIfEmpty(code);
        _receiptMessages.Clear();
        _receiptMessages.AddRange(CopyTexts(_receiptByLang[code]));
        if (RecorderReceiptEditBox != null)
            RecorderReceiptEditBox.Text = "";
        RefreshReceiptList();
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

    private void TryRecordChatMessage(string speaker, string msg, bool isWhisper, string? userId = null)
    {
        if (!_recorderEnabled || string.IsNullOrWhiteSpace(msg)) return;

        string name = NormalizeSpeaker(speaker);
        string uid = (userId ?? "").Trim();
        if (string.IsNullOrEmpty(uid))
            uid = LookupRoomUserIdByName(name);

        if (IsSelfName(speaker) || IsSelfName(name) || IsSelfUserId(uid) || IsBotOwnMessage(speaker, msg))
        {
            RememberSelfFromChat(speaker);
            if (!string.IsNullOrEmpty(uid))
                RememberSelfIdentity(name, uid);
            return;
        }

        bool triggerHit = MatchesRecorderTrigger(msg);
        if (!isWhisper && !triggerHit) return;

        if (string.IsNullOrWhiteSpace(name) || !IsValidSpeaker(name)) return;

        string body = triggerHit ? RecorderPayload(msg) : msg.Trim();
        if (IsRecorderChromeLabel(body)) return;
        string stamp = DateTime.Now.ToString("HH:mm");

        RecorderUserVm? user = null;
        if (!string.IsNullOrEmpty(uid) && _recorderByUid.TryGetValue(uid, out user) && user != null)
        {
            if (!string.IsNullOrWhiteSpace(name) &&
                !string.Equals(user.Name, name, StringComparison.Ordinal))
            {
                _recorderByName.Remove(user.Name);
                user.Name = name;
                _recorderByName[name] = user;
            }
        }
        else if (_recorderByName.TryGetValue(name, out user) && user != null)
        {
            if (!string.IsNullOrEmpty(uid) && string.IsNullOrEmpty(user.UserId))
            {
                user.UserId = uid;
                _recorderByUid[uid] = user;
            }
        }
        else
        {
            user = new RecorderUserVm { Name = name, UserId = uid };
            _recorderByName[name] = user;
            if (!string.IsNullOrEmpty(uid))
                _recorderByUid[uid] = user;
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
        _ = TrySendReceiptAsync(user, isWhisper);
    }

    private async Task TrySendReceiptAsync(RecorderUserVm user, bool isWhisper)
    {
        if (!_confirmReceipt || _receiptMessages.Count == 0) return;
        int i = user.ReceiptIndex;
        if (i < 0 || i >= _receiptMessages.Count) return;
        string text = _receiptMessages[i];
        user.ReceiptIndex = i + 1;
        if (string.IsNullOrWhiteSpace(user.UserId))
        {
            string found = LookupRoomUserIdByName(user.Name);
            if (!string.IsNullOrEmpty(found))
                user.UserId = found;
        }
        SaveRecorderSettings();
        if (!isWhisper)
            text = PrefixPublicDm(user.Name, text);
        try
        {
            if (isWhisper)
            {
                await SendToImvuChat(text, whisperReply: true, whisperSpeaker: user.Name,
                    proactiveWhisperToUser: true, joinUserId: user.UserId,
                    requireBotActive: false, logSend: false);
            }
            else
            {
                await SendToImvuChat(text, requireBotActive: false, logSend: false);
            }
        }
        catch { }
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

    private sealed class ReceiptLineVm
    {
        public int Index { get; init; }
        public string Number { get; init; } = "";
        public string Text { get; init; } = "";
        public bool CanMoveUp { get; init; }
        public bool CanMoveDown { get; init; }
    }

    private bool _receiptUiSyncing;

    private void RecorderAnswerGear_Click(object sender, RoutedEventArgs e)
    {
        _receiptUiSyncing = true;
        try
        {
            if (RecorderConfirmReceiptCheck != null)
                RecorderConfirmReceiptCheck.IsChecked = _confirmReceipt;
        }
        finally { _receiptUiSyncing = false; }
        UpdateReceiptEditorVisibility();
        RefreshReceiptList();
        if (RecorderReceiptEditBox != null)
            RecorderReceiptEditBox.Text = "";
        if (RecorderAnswerModal != null)
            RecorderAnswerModal.Visibility = Visibility.Visible;
    }

    private void RecorderAnswerDone_Click(object sender, RoutedEventArgs e)
    {
        if (RecorderAnswerModal != null)
            RecorderAnswerModal.Visibility = Visibility.Collapsed;
        SaveRecorderSettings();
    }

    private void RecorderConfirmReceipt_Changed(object sender, RoutedEventArgs e)
    {
        if (_receiptUiSyncing || !_recorderReady) return;
        _confirmReceipt = RecorderConfirmReceiptCheck?.IsChecked == true;
        UpdateReceiptEditorVisibility();
        SaveRecorderSettings();
    }

    private void UpdateReceiptEditorVisibility()
    {
        if (RecorderReceiptEditorPanel == null) return;
        RecorderReceiptEditorPanel.Visibility =
            _confirmReceipt ? Visibility.Visible : Visibility.Collapsed;
    }

    private void RefreshReceiptList()
    {
        if (RecorderReceiptList == null) return;
        var rows = new List<ReceiptLineVm>();
        int last = _receiptMessages.Count - 1;
        for (int i = 0; i < _receiptMessages.Count; i++)
        {
            rows.Add(new ReceiptLineVm
            {
                Index = i,
                Number = (i + 1) + ".",
                Text = _receiptMessages[i],
                CanMoveUp = i > 0,
                CanMoveDown = i < last
            });
        }
        RecorderReceiptList.ItemsSource = null;
        RecorderReceiptList.ItemsSource = rows;
    }

    private void RecorderReceiptMoveUp_Preview(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: ReceiptLineVm row }) return;
        MoveReceiptLine(row.Index, -1);
    }

    private void RecorderReceiptMoveDown_Preview(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is not FrameworkElement { Tag: ReceiptLineVm row }) return;
        MoveReceiptLine(row.Index, 1);
    }

    private void MoveReceiptLine(int index, int delta)
    {
        int dest = index + delta;
        if (index < 0 || dest < 0 || index >= _receiptMessages.Count || dest >= _receiptMessages.Count)
            return;
        (_receiptMessages[index], _receiptMessages[dest]) = (_receiptMessages[dest], _receiptMessages[index]);
        SaveRecorderSettings();
        RefreshReceiptList();
    }

    private void RecorderReceiptList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        int i = RecorderReceiptList?.SelectedIndex ?? -1;
        if (i < 0 || i >= _receiptMessages.Count) return;
        if (RecorderReceiptEditBox != null)
            RecorderReceiptEditBox.Text = _receiptMessages[i];
    }

    private void RecorderReceiptAdd_Click(object sender, RoutedEventArgs e)
    {
        string text = RecorderReceiptEditBox?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(text)) return;
        _receiptMessages.Add(text);
        RefreshReceiptList();
        SaveRecorderSettings();
        if (RecorderReceiptEditBox != null) RecorderReceiptEditBox.Text = "";
    }

    private void RecorderReceiptUpdate_Click(object sender, RoutedEventArgs e)
    {
        int i = RecorderReceiptList?.SelectedIndex ?? -1;
        string text = RecorderReceiptEditBox?.Text?.Trim() ?? "";
        if (i < 0 || i >= _receiptMessages.Count || string.IsNullOrEmpty(text)) return;
        _receiptMessages[i] = text;
        RefreshReceiptList();
        if (RecorderReceiptList != null) RecorderReceiptList.SelectedIndex = i;
        SaveRecorderSettings();
    }

    private void RecorderReceiptDelete_Click(object sender, RoutedEventArgs e)
    {
        int i = RecorderReceiptList?.SelectedIndex ?? -1;
        if (i < 0 || i >= _receiptMessages.Count) return;
        _receiptMessages.RemoveAt(i);
        SyncReceiptViewToStore();
        if (_receiptMessages.Count == 0)
        {
            SeedAnsweringLanguageIfEmpty(_currentLanguage);
            ApplyReceiptLanguage(_currentLanguage);
        }
        RefreshReceiptList();
        if (RecorderReceiptEditBox != null) RecorderReceiptEditBox.Text = "";
        SaveRecorderSettings();
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
            if (!string.IsNullOrEmpty(user.UserId))
                _recorderByUid.Remove(user.UserId);
        }
        SaveRecorderSettings();
        RefreshRecorderUsersUi();
    }

    private static List<string> DefaultConsoleTexts(string lang) =>
        string.Equals(lang, "ru", StringComparison.OrdinalIgnoreCase)
            ? new List<string> { "Это 1-е уведомление.", "Это последнее уведомление." }
            : new List<string> { "This is 1st Notice.", "This is final Notice." };

    private void LoadDmMessages()
    {
        _dmReady = false;
        _dmAsWhisper = false;
        _chipMessagePrefix = true;
        _consoleByLang.Clear();
        _dmMessages.Clear();
        try
        {
            var data = AppDatabase.LoadConsole();
            _dmAsWhisper = data.AsWhisper;
            _chipMessagePrefix = data.PrefixUserName;
            foreach (var kv in data.Messages)
            {
                if (kv.Value != null && kv.Value.Count > 0)
                    _consoleByLang[kv.Key] = kv.Value;
            }
        }
        catch (Exception ex)
        {
            AppendLog("Load console err: " + ex.Message, LogCategory.Warning);
        }

        foreach (string lang in AppLanguageCodes())
            SeedConsoleLanguageIfEmpty(lang);
        ApplyConsoleLanguage(_currentLanguage);
        _dmReady = true;
        SaveDmMessages();
    }

    private void SeedConsoleLanguageIfEmpty(string lang)
    {
        if (!_consoleByLang.TryGetValue(lang, out var list) || list == null || list.Count == 0)
            _consoleByLang[lang] = DefaultConsoleTexts(lang);
    }

    private void SyncConsoleViewToStore()
    {
        string lang = string.IsNullOrEmpty(_currentLanguage) ? "en" : _currentLanguage;
        _consoleByLang[lang] = _dmMessages.Select(m => m.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
    }

    private void ApplyConsoleLanguage(string lang)
    {
        SeedConsoleLanguageIfEmpty(lang);
        _dmMessages.Clear();
        foreach (string text in _consoleByLang[lang])
            _dmMessages.Add(new DmMessageEntry { Text = text });
        RefreshDmMessageCombo();
        RefreshDmEditList();
    }

    private void SaveDmMessages()
    {
        if (!_dmReady) return;
        try
        {
            SyncConsoleViewToStore();
            var langs = new HashSet<string>(AppLanguageCodes(), StringComparer.OrdinalIgnoreCase);
            foreach (string k in _consoleByLang.Keys) langs.Add(k);
            foreach (string lang in langs)
                SeedConsoleLanguageIfEmpty(lang);
            AppDatabase.SaveConsole(new AppDatabase.ConsoleData
            {
                AsWhisper = _dmAsWhisper,
                PrefixUserName = _chipMessagePrefix,
                Messages = _consoleByLang
            });
        }
        catch (Exception ex)
        {
            AppendLog("Save console err: " + ex.Message, LogCategory.Warning);
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
            foreach (string text in DefaultConsoleTexts(_currentLanguage))
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
            body = PrefixPublicDm(user.Name, body);
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

    private bool IsSelfName(string? name)
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

    private string LookupRoomUserIdByName(string? name)
    {
        string fold = FoldImvuName(name);
        if (string.IsNullOrEmpty(fold)) return "";
        var room = _roomUsers.FirstOrDefault(u =>
            !string.IsNullOrWhiteSpace(u.UserId) && FoldImvuName(u.Name) == fold);
        if (room != null)
            return room.UserId.Trim();
        foreach (var kv in _uidDisplayNames)
        {
            if (FoldImvuName(kv.Value) == fold && !string.IsNullOrWhiteSpace(kv.Key))
                return kv.Key.Trim();
        }
        if (_recorderByName.TryGetValue(name ?? "", out var rec) &&
            rec != null && !string.IsNullOrWhiteSpace(rec.UserId))
            return rec.UserId.Trim();
        return "";
    }

    private void RememberSelfFromChat(string speaker)
    {
        string name = NormalizeSpeaker(speaker);
        if (string.IsNullOrWhiteSpace(name) || !IsValidSpeaker(name)) return;
        if (string.Equals(FoldImvuName(_selfDetectedName), FoldImvuName(name), StringComparison.Ordinal))
            return;
        _selfDetectedName = name.Trim();
        PruneSelfFromRoster();
        PruneSelfFromRecorder();
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
            {
                PruneSelfFromRoster();
                PruneSelfFromRecorder();
            }
        }
        catch { }
    }

    private static string RoomUserKey(string name, string? userId)
    {
        if (!string.IsNullOrWhiteSpace(userId))
            return "uid:" + userId.Trim();
        return FoldImvuName(name);
    }

    private static bool IsInvisibleRoomName(string? name) => IsLayoutWhitespaceOnly(name);

    private bool AddOrUpdateRoomUser(string name, string? userId)
    {
        string uid = (userId ?? "").Trim();
        string display = SanitizeJoinerName(name);
        if (IsInvisibleRoomName(display))
        {
            if (string.IsNullOrEmpty(uid)) return false;
            display = uid;
        }
        if (IsSelfUserId(uid) || IsSelfName(display) || IsSelfName(name))
        {
            RememberSelfIdentity(name, uid);
            return false;
        }

        string key = RoomUserKey(display, uid);
        if (string.IsNullOrEmpty(key)) return false;

        string nameFold = FoldImvuName(display);
        var existing = _roomUsers.FirstOrDefault(u =>
            u.Key == key ||
            (!string.IsNullOrEmpty(uid) && u.UserId == uid) ||
            (!string.IsNullOrEmpty(nameFold) && FoldImvuName(u.Name) == nameFold));
        ForgetPendingRemove(display, uid);
        if (existing == null)
        {
            _roomUsers.Add(new RoomUserVm
            {
                Name = display,
                UserId = uid,
                Key = key
            });
            RefreshRoomUsersUi();
            return true;
        }

        bool changed = false;
        if (!string.IsNullOrEmpty(uid) && existing.UserId != uid)
        {
            existing.UserId = uid;
            existing.Key = "uid:" + uid;
            changed = true;
        }
        if (!IsInvisibleRoomName(display) &&
            !string.Equals(existing.Name, display, StringComparison.Ordinal))
        {
            existing.Name = display;
            changed = true;
        }
        if (changed)
            RefreshRoomUsersUi();
        return false;
    }

    private void RememberSelfIdentity(string? name, string? userId)
    {
        if (string.IsNullOrWhiteSpace(userId)) return;
        if (string.Equals(_selfDetectedUid, userId.Trim(), StringComparison.Ordinal)) return;
        _selfDetectedUid = userId.Trim();
        PruneSelfFromRoster();
        PruneSelfFromRecorder();
    }

    private void PruneSelfFromRecorder()
    {
        bool removed = false;
        for (int i = _recorderUsers.Count - 1; i >= 0; i--)
        {
            var u = _recorderUsers[i];
            if (!IsSelfName(u.Name) && !IsSelfUserId(u.UserId)) continue;
            _recorderUsers.RemoveAt(i);
            _recorderByName.Remove(u.Name);
            if (!string.IsNullOrEmpty(u.UserId))
                _recorderByUid.Remove(u.UserId);
            removed = true;
        }
        if (removed)
            RefreshRecorderUsersUi();
    }

    private bool RemoveRoomUser(string name, string? userId = null)
    {
        string uid = (userId ?? "").Trim();
        string nameKey = RoomUserKey(SanitizeJoinerName(name), null);
        int removed = _roomUsers.RemoveAll(u =>
            (!string.IsNullOrEmpty(uid) && (u.UserId == uid || u.Key == "uid:" + uid)) ||
            (!string.IsNullOrEmpty(nameKey) && u.Key == nameKey) ||
            (!string.IsNullOrWhiteSpace(name) &&
             string.Equals(u.Name, name.Trim(), StringComparison.OrdinalIgnoreCase)));
        if (removed == 0) return false;
        if (_selectedRoomUserKey != null &&
            _roomUsers.All(u => u.Key != _selectedRoomUserKey))
            _selectedRoomUserKey = null;
        RefreshRoomUsersUi();
        return true;
    }

    private void ClearRoomRoster()
    {
        _pendingRemoves.Clear();
        _roomEnterCounts.Clear();
        if (_roomUsers.Count == 0 && _selectedRoomUserKey == null) return;
        _roomUsers.Clear();
        _selectedRoomUserKey = null;
        RefreshRoomUsersUi();
    }

    private static string RoomEnterCountKey(string name, string? userId)
    {
        string uid = (userId ?? "").Trim();
        if (!string.IsNullOrEmpty(uid))
            return "uid:" + uid;
        string fold = FoldImvuName(SanitizeJoinerName(name));
        return string.IsNullOrEmpty(fold) ? "" : "name:" + fold;
    }

    private int NoteRoomEnter(string name, string? userId)
    {
        string key = RoomEnterCountKey(name, userId);
        if (string.IsNullOrEmpty(key)) return 1;
        _roomEnterCounts.TryGetValue(key, out int n);
        n++;
        _roomEnterCounts[key] = n;
        return n;
    }

    private void LogRoomJoin(string name, string? userId)
    {
        int n = NoteRoomEnter(name, userId);
        string uidLabel = string.IsNullOrWhiteSpace(userId) ? "?" : userId.Trim();
        string shown = !IsInvisibleRoomName(name) ? name : uidLabel;
        string line = $"[JOIN] uId={uidLabel}: {shown} | Joined the chat";
        if (n > 1)
            line += " | " + n;
        AppendActivityLog(line, LogCategory.Join);
    }

    private static string RoomUserLabel(RoomUserVm user)
    {
        if (!string.IsNullOrWhiteSpace(user.Name) && !IsInvisibleRoomName(user.Name))
            return user.Name;
        if (!string.IsNullOrWhiteSpace(user.UserId))
            return user.UserId;
        return "?";
    }

    private void RefreshRoomUsersUi()
    {
        if (DmUsersGrid == null) return;
        DmUsersGrid.Children.Clear();
        foreach (var user in _roomUsers)
        {
            bool selected = user.Key == _selectedRoomUserKey;
            var row = new Grid { Margin = new Thickness(3) };

            string chipText = RoomUserLabel(user);
            if (string.IsNullOrWhiteSpace(chipText) && !string.IsNullOrWhiteSpace(user.UserId))
                chipText = user.UserId;
            if (string.IsNullOrWhiteSpace(chipText))
                chipText = "?";

            var btn = new Button
            {
                Content = chipText,
                Style = TryFindResource("RoomUserChip") as Style,
                Margin = new Thickness(0),
                Tag = user.Key,
                Background = selected ? RoomUserSelectedBg : RoomUserIdleBg,
                BorderBrush = selected
                    ? new SolidColorBrush(Color.FromRgb(0x70, 0x80, 0xC0))
                    : new SolidColorBrush(Color.FromRgb(0x3A, 0x3A, 0x58))
            };
            btn.Click += RoomUserChip_Click;

            var showActions = selected ? Visibility.Visible : Visibility.Collapsed;
            var msgBtn = new Button
            {
                Style = TryFindResource("RoomUserMessageBtn") as Style,
                ToolTip = "Message User",
                Tag = user,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 28, 0),
                Visibility = showActions
            };
            Panel.SetZIndex(msgBtn, 2);
            msgBtn.PreviewMouseLeftButtonDown += RoomUserMessage_Preview;

            var removeBtn = new Button
            {
                Content = "R",
                Style = TryFindResource("RoomUserRemoveBtn") as Style,
                ToolTip = "Remove User",
                Tag = user,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 4, 0),
                Visibility = showActions
            };
            Panel.SetZIndex(removeBtn, 2);
            removeBtn.PreviewMouseLeftButtonDown += RoomUserRemove_Preview;

            void FitChipActionPad()
            {
                double h = btn.ActualHeight;
                if (h <= 0) return;
                double inset = Math.Max(1, Math.Round((h - 20) / 2.0));
                removeBtn.Margin = new Thickness(0, 0, inset, 0);
                msgBtn.Margin = new Thickness(0, 0, inset + 20 + inset, 0);
            }
            btn.Loaded += (_, _) => FitChipActionPad();
            btn.SizeChanged += (_, _) => FitChipActionPad();

            row.MouseEnter += (_, _) =>
            {
                msgBtn.Visibility = Visibility.Visible;
                removeBtn.Visibility = Visibility.Visible;
            };
            row.MouseLeave += (_, _) =>
            {
                if (user.Key != _selectedRoomUserKey)
                {
                    msgBtn.Visibility = Visibility.Collapsed;
                    removeBtn.Visibility = Visibility.Collapsed;
                }
            };

            row.Children.Add(btn);
            row.Children.Add(msgBtn);
            row.Children.Add(removeBtn);
            DmUsersGrid.Children.Add(row);
        }
    }

    private void RoomUserChip_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.Tag is not string key) return;
        _selectedRoomUserKey = _selectedRoomUserKey == key ? null : key;
        RefreshRoomUsersUi();
    }

    private void RoomUserMessage_Preview(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is Button { Tag: RoomUserVm user })
            OpenRoomUserMessageModal(user);
    }

    private void RoomUserRemove_Preview(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is Button btn)
            _ = RemoveRoomUserFromRoomAsync(btn);
    }

    private void OpenRoomUserMessageModal(RoomUserVm user)
    {
        _chipMessageUser = user;
        string label = RoomUserLabel(user);
        if (RoomUserMessageTitle != null)
            RoomUserMessageTitle.Text = "Message to " + label;
        if (RoomUserMessageBox != null)
            RoomUserMessageBox.Text = "";
        if (RoomUserMessagePrefixCheck != null)
        {
            _dmUiSyncing = true;
            RoomUserMessagePrefixCheck.IsChecked = _chipMessagePrefix;
            _dmUiSyncing = false;
        }
        if (RoomUserMessageModeCombo != null && RoomUserMessageModeCombo.Items.Count > 0)
            RoomUserMessageModeCombo.SelectedIndex = 0;
        UpdateRoomUserMessageModeUi();
        UpdateRoomUserMessageCount();
        if (RoomUserMessageModal != null)
            RoomUserMessageModal.Visibility = Visibility.Visible;
    }

    private void RoomUserMessageCancel_Click(object sender, RoutedEventArgs e) =>
        CloseRoomUserMessageModal();

    private void CloseRoomUserMessageModal()
    {
        if (RoomUserMessageModal != null)
            RoomUserMessageModal.Visibility = Visibility.Collapsed;
        if (RoomUserMessageBox != null)
            RoomUserMessageBox.Text = "";
        _chipMessageUser = null;
    }

    private void RoomUserMessagePrefix_Changed(object sender, RoutedEventArgs e)
    {
        if (_dmUiSyncing || !_dmReady) return;
        _chipMessagePrefix = RoomUserMessagePrefixCheck?.IsChecked == true;
        SaveDmMessages();
    }

    private void RoomUserMessageBox_TextChanged(object sender, TextChangedEventArgs e) =>
        UpdateRoomUserMessageCount();

    private void UpdateRoomUserMessageCount()
    {
        if (RoomUserMessageCount == null) return;
        int n = RoomUserMessageBox?.Text?.Length ?? 0;
        if (n > 1024)
            n = 1024;
        RoomUserMessageCount.Text = n.ToString() + "/1024";
    }

    private string SelectedRoomUserMessageMode()
    {
        if (RoomUserMessageModeCombo?.SelectedItem is ComboBoxItem item && item.Tag is string tag)
            return tag;
        return "public";
    }

    private void RoomUserMessageMode_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (!_dmReady && RoomUserMessageModal == null) return;
        UpdateRoomUserMessageModeUi();
    }

    private void UpdateRoomUserMessageModeUi()
    {
        string mode = SelectedRoomUserMessageMode();
        if (RoomUserMessageCount != null)
            RoomUserMessageCount.Visibility = mode == "dm" ? Visibility.Visible : Visibility.Hidden;
        if (RoomUserMessageSendBtn == null) return;
        RoomUserMessageSendBtn.Content = mode switch
        {
            "whisper" => "Send Whisper",
            "dm" => "Send DM",
            _ => "Send Public"
        };
        RoomUserMessageSendBtn.IsEnabled = mode != "dm";
        RoomUserMessageSendBtn.Opacity = mode == "dm" ? 0.45 : 1;
    }

    private async void RoomUserMessageSend_Click(object sender, RoutedEventArgs e)
    {
        if (_chipMessageUser == null) return;
        string mode = SelectedRoomUserMessageMode();
        if (mode == "dm") return;

        string body = RoomUserMessageBox?.Text ?? "";
        if (string.IsNullOrWhiteSpace(body))
        {
            AppendLog("Enter a message first.", LogCategory.Warning);
            return;
        }
        if (!await IsActiveRoomPresentAsync())
        {
            AppendLog("No active room — cannot send.", LogCategory.Warning);
            return;
        }

        var user = _chipMessageUser;
        string? result;
        if (mode == "whisper")
        {
            result = await SendToImvuChat(body, whisperReply: true, whisperSpeaker: user.Name,
                proactiveWhisperToUser: true, joinUserId: user.UserId,
                requireBotActive: false, logSend: false);
            if (result == "ok")
                AppendActivityLog($"[W.DM] {user.Name} {body}", LogCategory.WhisperDm);
            else
                AppendLog("Whisper failed: " + (result ?? "unknown"), LogCategory.Warning);
        }
        else
        {
            string sent = _chipMessagePrefix ? PrefixPublicDm(user.Name, body) : body;
            result = await SendToImvuChat(sent, requireBotActive: false, logSend: false);
            if (result == "ok")
                AppendActivityLog($"[P.DM] {user.Name} {sent}", LogCategory.PublicDm);
            else
                AppendLog("Public send failed: " + (result ?? "unknown"), LogCategory.Warning);
        }

        if (result == "ok")
            CloseRoomUserMessageModal();
    }

    private async Task RemoveRoomUserFromRoomAsync(Button btn)
    {
        if (btn.Tag is not RoomUserVm user)
        {
            AppendActivityLog("[REMOVE] ignored — no user on button", LogCategory.Warning);
            return;
        }

        string label = RoomUserLabel(user);
        string uid = (user.UserId ?? "").Trim();
        if (string.IsNullOrEmpty(uid) && Regex.IsMatch(label, @"^\d{5,}$"))
            uid = label;

        if (string.IsNullOrEmpty(uid))
        {
            AppendActivityLog("[REMOVE] failed — no uid for " + label, LogCategory.Warning);
            return;
        }
        if (IsSelfUserId(uid))
        {
            AppendActivityLog("[REMOVE] blocked — that is you", LogCategory.Warning);
            return;
        }
        if (!IsWebViewReady)
        {
            AppendActivityLog("[REMOVE] failed — IMVU not ready", LogCategory.Warning);
            return;
        }

        RememberPendingRemove(label, uid);

        string uidJson = System.Text.Json.JsonSerializer.Serialize(uid);
        string nameJson = System.Text.Json.JsonSerializer.Serialize(label);
        string js = ImvuScripts.KickUserFull;
        string? result;
        try
        {
            string? started = await RunJsStringAsync(
                js + $"return __imvuRemoveUserStart({uidJson}, {nameJson});",
                logErrors: true);
            result = started;
            if (started == "started")
            {
                for (int i = 0; i < 100; i++)
                {
                    await Task.Delay(80);
                    result = await RunJsStringAsync(js + "return __imvuRemoveUserPoll();", logErrors: false);
                    if (!string.IsNullOrEmpty(result) && result != "pending" && result != "{}")
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            AppendActivityLog("[REMOVE] error: " + ex.Message, LogCategory.Error);
            return;
        }

        bool ok = result == "ui:confirmed"
            || result == "bootFromChat"
            || (result != null && result.StartsWith("imvu-http:", StringComparison.Ordinal))
            || (result != null
                && result.StartsWith("api:", StringComparison.Ordinal)
                && !result.StartsWith("api-fail", StringComparison.Ordinal)
                && !result.StartsWith("api-error", StringComparison.Ordinal));
        if (!ok)
        {
            ForgetPendingRemove(label, uid);
            AppendActivityLog("[REMOVE] failed " + label + " (" + (result ?? "null") + ")", LogCategory.Warning);
            return;
        }

        LogRemovedOnce(label, uid, result);
    }

    private static string PrefixPublicDm(string? userName, string body)
    {
        string name = (userName ?? "").Trim();
        if (string.IsNullOrEmpty(name) || IsInvisibleRoomName(name))
            return body;
        string prefix = name + ", ";
        if (body.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            return body;
        return prefix + body;
    }

    private static string FormatRemoveHow(string? result)
    {
        if (string.IsNullOrWhiteSpace(result)) return "removed";
        if (string.Equals(result, "bootFromChat", StringComparison.OrdinalIgnoreCase))
            return "boot From Chat";
        if (string.Equals(result, "ui:confirmed", StringComparison.OrdinalIgnoreCase))
            return "Remove User";
        if (result.StartsWith("imvu-http:", StringComparison.OrdinalIgnoreCase))
            return "IMVU HTTP";
        if (result.StartsWith("api:", StringComparison.OrdinalIgnoreCase))
            return "API";
        return result;
    }

    private void PrunePendingRemoves()
    {
        DateTime cutoff = DateTime.UtcNow.AddSeconds(-60);
        _pendingRemoves.RemoveAll(p => p.Utc < cutoff);
    }

    private bool MatchesPendingRemove(PendingRemove p, string name, string uid)
    {
        if (!string.IsNullOrEmpty(uid) && p.Uid == uid) return true;
        return !string.IsNullOrWhiteSpace(name) &&
               string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase);
    }

    private void RememberPendingRemove(string name, string uid)
    {
        PrunePendingRemoves();
        if (_pendingRemoves.Any(p => MatchesPendingRemove(p, name, uid))) return;
        _pendingRemoves.Add(new PendingRemove { Name = name, Uid = uid });
    }

    private void ForgetPendingRemove(string name, string uid)
    {
        _pendingRemoves.RemoveAll(p => MatchesPendingRemove(p, name, uid));
    }

    private bool IsPendingRemove(string name, string? userId)
    {
        PrunePendingRemoves();
        string uid = (userId ?? "").Trim();
        return _pendingRemoves.Any(p => MatchesPendingRemove(p, name, uid));
    }

    private void LogRemovedOnce(string name, string uid, string? result)
    {
        PrunePendingRemoves();
        var hit = _pendingRemoves.FirstOrDefault(p => MatchesPendingRemove(p, name, uid));
        if (hit == null)
        {
            hit = new PendingRemove { Name = name, Uid = uid };
            _pendingRemoves.Add(hit);
        }
        if (hit.Logged) return;
        hit.Logged = true;
        string how = FormatRemoveHow(result);
        string who = string.IsNullOrWhiteSpace(name) ? uid : name;
        AppendActivityLog($"[REMOVED] {who} ({how}) uid={uid}", LogCategory.Remove);
    }

    private void HandleRoomChatEvent(string speaker, string text, string kind, string joinUserId, bool isWhisper = false)
    {
        if (string.Equals(kind, "leave", StringComparison.OrdinalIgnoreCase))
        {
            string name = SanitizeJoinerName(speaker);
            if (string.IsNullOrWhiteSpace(name) && TryParseLeaveName(text, out string parsed))
                name = parsed;
            if (!string.IsNullOrWhiteSpace(name) || !string.IsNullOrWhiteSpace(joinUserId))
            {
                string label = !IsInvisibleRoomName(name) ? name : (joinUserId ?? "").Trim();
                bool pending = IsPendingRemove(name, joinUserId) || IsPendingRemove(label, joinUserId);
                bool wasInRoom = RemoveRoomUser(name, joinUserId);
                if (pending)
                    return;
                if (wasInRoom && !string.IsNullOrWhiteSpace(label))
                    AppendActivityLog($"[LEFT] {label} left the chat", LogCategory.Left);
            }
            return;
        }

        if (string.Equals(kind, "present", StringComparison.OrdinalIgnoreCase))
        {
            string name = SanitizeJoinerName(speaker);
            if (string.IsNullOrWhiteSpace(name) && TryParsePresentName(text, out string parsed))
                name = parsed;
            AddOrUpdateRoomUser(name, joinUserId);
            return;
        }

        if (ContainsJoinLine(text))
        {
            TryResolveJoiner(speaker, text, out string joiner);
            if (string.IsNullOrWhiteSpace(joiner))
                joiner = SanitizeJoinerName(speaker);
            if (IsSelfName(joiner) || IsSelfUserId(joinUserId))
            {
                RememberSelfIdentity(joiner, joinUserId);
            }
            else if (!string.IsNullOrWhiteSpace(joiner) || !string.IsNullOrWhiteSpace(joinUserId))
            {
                AddOrUpdateRoomUser(joiner, joinUserId);
                LogRoomJoin(joiner, joinUserId);
            }
        }

        if (string.Equals(kind, "chat", StringComparison.OrdinalIgnoreCase) ||
            kind == "0" || kind == "1")
        {
            if (IsBotOwnMessage(speaker, text))
                RememberSelfFromChat(speaker);
            TryRecordChatMessage(speaker, text, isWhisper || kind == "1", joinUserId);
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
