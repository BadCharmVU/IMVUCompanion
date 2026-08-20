using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace IMVUCompanion;

/// <summary>
/// App data in %LOCALAPPDATA%\IMVUCompanion\companion.db.
/// First launch imports leftover JSON files; after that SQLite is the only store.
/// API keys are stored DPAPI-wrapped (same Windows user only).
/// </summary>
internal static class AppDatabase
{
    private static readonly object Gate = new();
    private static SqliteConnection? _conn;

    public sealed class WelcomeData
    {
        public bool Msg1Enabled { get; set; } = true;
        public bool Msg1AsWhisper { get; set; }
        public bool Msg2Enabled { get; set; }
        public bool Msg2AsWhisper { get; set; } = true;
        public Dictionary<string, List<string>> Msg1 { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, List<string>> Msg2 { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class CategorySettingsData
    {
        public bool AllowRepeatTriggers { get; set; }
        public int CooldownSeconds { get; set; } = 30;
        public bool UseNamePrefix { get; set; }
        public string ColorHex { get; set; } = "#7DD3FC";
    }

    public sealed class TriggerEntryData
    {
        public string Command { get; set; } = "";
        public string Response { get; set; } = "";
    }

    public sealed class TriggerData
    {
        public bool ListenToChat { get; set; } = true;
        public string ActiveCategory { get; set; } = "General";
        public Dictionary<string, string> ActiveCategoryByLang { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, List<TriggerEntryData>>> Categories { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, Dictionary<string, CategorySettingsData>> SettingsByLang { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class ConsoleData
    {
        public bool AsWhisper { get; set; }
        public bool PrefixUserName { get; set; } = true;
        public Dictionary<string, List<string>> Messages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class RecorderMessageData
    {
        public string Id { get; set; } = "";
        public string Time { get; set; } = "";
        public string Text { get; set; } = "";
        public bool IsWhisper { get; set; }
    }

    public sealed class RecorderUserData
    {
        public string Name { get; set; } = "";
        public string UserId { get; set; } = "";
        public int ReceiptIndex { get; set; }
        public List<RecorderMessageData> Messages { get; set; } = new();
    }

    public sealed class RecorderData
    {
        public string Trigger { get; set; } = "RMsg";
        public bool ConfirmReceipt { get; set; } = true;
        public List<RecorderUserData> Users { get; set; } = new();
        public Dictionary<string, List<string>> AnsweringByLang { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public sealed class UiLayoutData
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double CenterX { get; set; }
        public double CenterY { get; set; }
        public string WindowState { get; set; } = "Normal";
        public double LeftColRatio { get; set; }
        public double LeftColWidth { get; set; }
        public double RightColWidth { get; set; }
        public double ActivityLogHeight { get; set; }
        public bool WelcomeExpanded { get; set; }
        public bool BotSettingsExpanded { get; set; }
        public bool RecorderExpanded { get; set; }
        public bool DmSettingsExpanded { get; set; }
        public string Language { get; set; } = "en";
        public bool HasRow { get; set; }
    }

    public sealed class AiProviderData
    {
        public string ApiKeyProtected { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public string Model { get; set; } = "";
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 1024;
        public bool Enabled { get; set; }
    }

    public sealed class AiSettingsData
    {
        public bool HasRow { get; set; }
        public string SelectedProvider { get; set; } = "Grok";
        public string BotDisplayName { get; set; } = "";
        public string CompanionAiTrigger { get; set; } = "";
        public Dictionary<string, AiProviderData> Providers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static void Initialize()
    {
        lock (Gate)
        {
            if (_conn != null) return;
            Directory.CreateDirectory(UserDataPaths.Root);
            var cs = new SqliteConnectionStringBuilder
            {
                DataSource = UserDataPaths.DatabaseFile,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Cache = SqliteCacheMode.Shared
            }.ToString();
            _conn = new SqliteConnection(cs);
            _conn.Open();
            Exec("PRAGMA foreign_keys = ON;");
            Exec("PRAGMA busy_timeout = 5000;");
            using (var cmd = _conn.CreateCommand())
            {
                cmd.CommandText = "PRAGMA journal_mode = WAL;";
                cmd.ExecuteScalar();
            }
            CreateSchema();
            if (!IsFlag("initialized"))
            {
                if (HasLegacyJson())
                    MigrateFromJson();
                SetFlag("initialized", true);
                SetMeta("schema_version", "2");
            }
            // Existing DBs were initialized before AI lived in SQLite.
            MigrateAiJsonIfNeeded();
            DeleteLegacyJsonFiles();
        }
    }

    public static void Close()
    {
        lock (Gate)
        {
            try { _conn?.Close(); } catch { }
            try { _conn?.Dispose(); } catch { }
            _conn = null;
        }
    }

    // ── Welcome ──────────────────────────────────────────────────────────────

    public static WelcomeData LoadWelcome()
    {
        lock (Gate)
        {
            var data = new WelcomeData();
            using (var cmd = Cmd("SELECT msg1_enabled, msg1_as_whisper, msg2_enabled, msg2_as_whisper FROM welcome_settings WHERE id = 1"))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    data.Msg1Enabled = r.GetInt32(0) != 0;
                    data.Msg1AsWhisper = r.GetInt32(1) != 0;
                    data.Msg2Enabled = r.GetInt32(2) != 0;
                    data.Msg2AsWhisper = r.GetInt32(3) != 0;
                }
            }
            using (var cmd = Cmd("SELECT lang, kind, text FROM welcome_messages ORDER BY lang, kind, sort_order, id"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    string lang = r.GetString(0);
                    string kind = r.GetString(1);
                    string text = r.GetString(2);
                    var bag = string.Equals(kind, "msg2", StringComparison.OrdinalIgnoreCase) ? data.Msg2 : data.Msg1;
                    if (!bag.TryGetValue(lang, out var list))
                    {
                        list = new List<string>();
                        bag[lang] = list;
                    }
                    list.Add(text);
                }
            }
            return data;
        }
    }

    public static void SaveWelcome(WelcomeData data)
    {
        lock (Gate)
        {
            using var tx = Conn.BeginTransaction();
            ExecTx(tx, "INSERT INTO welcome_settings (id, msg1_enabled, msg1_as_whisper, msg2_enabled, msg2_as_whisper) VALUES (1, @a, @b, @c, @d) ON CONFLICT(id) DO UPDATE SET msg1_enabled=@a, msg1_as_whisper=@b, msg2_enabled=@c, msg2_as_whisper=@d",
                ("@a", data.Msg1Enabled ? 1 : 0),
                ("@b", data.Msg1AsWhisper ? 1 : 0),
                ("@c", data.Msg2Enabled ? 1 : 0),
                ("@d", data.Msg2AsWhisper ? 1 : 0));
            ExecTx(tx, "DELETE FROM welcome_messages");
            foreach (var kv in data.Msg1)
                InsertMessageList(tx, "welcome_messages", kv.Key, "msg1", kv.Value);
            foreach (var kv in data.Msg2)
                InsertMessageList(tx, "welcome_messages", kv.Key, "msg2", kv.Value);
            tx.Commit();
        }
    }

    // ── Triggers ─────────────────────────────────────────────────────────────

    public static TriggerData LoadTriggers()
    {
        lock (Gate)
        {
            var data = new TriggerData();
            using (var cmd = Cmd("SELECT listen_to_chat, active_category FROM trigger_settings WHERE id = 1"))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    data.ListenToChat = r.GetInt32(0) != 0;
                    data.ActiveCategory = r.GetString(1);
                }
            }
            using (var cmd = Cmd("SELECT lang, name FROM trigger_active_category"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                    data.ActiveCategoryByLang[r.GetString(0)] = r.GetString(1);
            }

            var idToKey = new Dictionary<long, (string lang, string name)>();
            using (var cmd = Cmd("SELECT id, lang, name, color_hex, cooldown_seconds, allow_repeat, use_name_prefix FROM trigger_categories ORDER BY id"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    long id = r.GetInt64(0);
                    string lang = r.GetString(1);
                    string name = r.GetString(2);
                    idToKey[id] = (lang, name);
                    if (!data.Categories.ContainsKey(name))
                        data.Categories[name] = new Dictionary<string, List<TriggerEntryData>>(StringComparer.OrdinalIgnoreCase);
                    data.Categories[name][lang] = new List<TriggerEntryData>();
                    if (!data.SettingsByLang.TryGetValue(lang, out var bag))
                    {
                        bag = new Dictionary<string, CategorySettingsData>(StringComparer.OrdinalIgnoreCase);
                        data.SettingsByLang[lang] = bag;
                    }
                    bag[name] = new CategorySettingsData
                    {
                        ColorHex = r.IsDBNull(3) ? "#7DD3FC" : r.GetString(3),
                        CooldownSeconds = r.IsDBNull(4) ? 30 : Math.Clamp(r.GetInt32(4), 1, 3600),
                        AllowRepeatTriggers = !r.IsDBNull(5) && r.GetInt32(5) != 0,
                        UseNamePrefix = !r.IsDBNull(6) && r.GetInt32(6) != 0
                    };
                }
            }
            using (var cmd = Cmd("SELECT category_id, command, response FROM trigger_entries ORDER BY category_id, sort_order, id"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    long cid = r.GetInt64(0);
                    if (!idToKey.TryGetValue(cid, out var key)) continue;
                    if (!data.Categories.TryGetValue(key.name, out var langs)) continue;
                    if (!langs.TryGetValue(key.lang, out var list)) continue;
                    list.Add(new TriggerEntryData
                    {
                        Command = r.GetString(1),
                        Response = r.IsDBNull(2) ? "" : r.GetString(2)
                    });
                }
            }
            return data;
        }
    }

    public static void SaveTriggers(TriggerData data)
    {
        lock (Gate)
        {
            using var tx = Conn.BeginTransaction();
            ExecTx(tx, "INSERT INTO trigger_settings (id, listen_to_chat, active_category) VALUES (1, @a, @b) ON CONFLICT(id) DO UPDATE SET listen_to_chat=@a, active_category=@b",
                ("@a", data.ListenToChat ? 1 : 0),
                ("@b", data.ActiveCategory ?? "General"));
            ExecTx(tx, "DELETE FROM trigger_entries");
            ExecTx(tx, "DELETE FROM trigger_categories");
            ExecTx(tx, "DELETE FROM trigger_active_category");
            foreach (var kv in data.ActiveCategoryByLang)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || string.IsNullOrWhiteSpace(kv.Value)) continue;
                ExecTx(tx, "INSERT INTO trigger_active_category (lang, name) VALUES (@l, @n)",
                    ("@l", kv.Key.Trim().ToLowerInvariant()), ("@n", kv.Value.Trim()));
            }

            foreach (var catKv in data.Categories)
            {
                string name = catKv.Key.Trim();
                if (string.IsNullOrEmpty(name) || catKv.Value == null) continue;
                foreach (var langKv in catKv.Value)
                {
                    string lang = (langKv.Key ?? "en").Trim().ToLowerInvariant();
                    if (string.IsNullOrEmpty(lang) || langKv.Value == null) continue;
                    var s = GetSettings(data, lang, name);
                    long id;
                    using (var cmd = CmdTx(tx,
                        "INSERT INTO trigger_categories (lang, name, color_hex, cooldown_seconds, allow_repeat, use_name_prefix) VALUES (@l, @n, @c, @cd, @ar, @up)"))
                    {
                        cmd.Parameters.AddWithValue("@l", lang);
                        cmd.Parameters.AddWithValue("@n", name);
                        cmd.Parameters.AddWithValue("@c", string.IsNullOrWhiteSpace(s.ColorHex) ? "#7DD3FC" : s.ColorHex);
                        cmd.Parameters.AddWithValue("@cd", Math.Clamp(s.CooldownSeconds, 1, 3600));
                        cmd.Parameters.AddWithValue("@ar", s.AllowRepeatTriggers ? 1 : 0);
                        cmd.Parameters.AddWithValue("@up", s.UseNamePrefix ? 1 : 0);
                        cmd.ExecuteNonQuery();
                        id = LastInsertId(tx);
                    }
                    int order = 0;
                    foreach (var entry in langKv.Value)
                    {
                        if (entry == null || string.IsNullOrWhiteSpace(entry.Command)) continue;
                        ExecTx(tx, "INSERT INTO trigger_entries (category_id, command, response, sort_order) VALUES (@c, @cmd, @r, @o)",
                            ("@c", id), ("@cmd", entry.Command), ("@r", entry.Response ?? ""), ("@o", order++));
                    }
                }
            }
            tx.Commit();
        }
    }

    private static CategorySettingsData GetSettings(TriggerData data, string lang, string name)
    {
        if (data.SettingsByLang.TryGetValue(lang, out var bag) &&
            bag != null &&
            bag.TryGetValue(name, out var s) && s != null)
            return s;
        return new CategorySettingsData();
    }

    // ── Console ──────────────────────────────────────────────────────────────

    public static ConsoleData LoadConsole()
    {
        lock (Gate)
        {
            var data = new ConsoleData();
            using (var cmd = Cmd("SELECT as_whisper, prefix_user_name FROM console_settings WHERE id = 1"))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    data.AsWhisper = r.GetInt32(0) != 0;
                    data.PrefixUserName = r.GetInt32(1) != 0;
                }
            }
            LoadLangTexts("SELECT lang, text FROM console_messages ORDER BY lang, sort_order, id", data.Messages);
            return data;
        }
    }

    public static void SaveConsole(ConsoleData data)
    {
        lock (Gate)
        {
            using var tx = Conn.BeginTransaction();
            ExecTx(tx, "INSERT INTO console_settings (id, as_whisper, prefix_user_name) VALUES (1, @a, @p) ON CONFLICT(id) DO UPDATE SET as_whisper=@a, prefix_user_name=@p",
                ("@a", data.AsWhisper ? 1 : 0),
                ("@p", data.PrefixUserName ? 1 : 0));
            ExecTx(tx, "DELETE FROM console_messages");
            foreach (var kv in data.Messages)
                InsertLangTexts(tx, "console_messages", kv.Key, kv.Value);
            tx.Commit();
        }
    }

    // ── Recorder + answering ─────────────────────────────────────────────────

    public static RecorderData LoadRecorder()
    {
        lock (Gate)
        {
            var data = new RecorderData();
            using (var cmd = Cmd("SELECT trigger, confirm_receipt FROM recorder_settings WHERE id = 1"))
            using (var r = cmd.ExecuteReader())
            {
                if (r.Read())
                {
                    string t = r.IsDBNull(0) ? "" : r.GetString(0);
                    if (!string.IsNullOrWhiteSpace(t)) data.Trigger = t;
                    data.ConfirmReceipt = r.GetInt32(1) != 0;
                }
            }

            var usersById = new Dictionary<long, RecorderUserData>();
            using (var cmd = Cmd("SELECT id, name, user_id, receipt_index FROM recorder_users ORDER BY sort_order, id"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    var user = new RecorderUserData
                    {
                        Name = r.GetString(1),
                        UserId = r.IsDBNull(2) ? "" : r.GetString(2),
                        ReceiptIndex = r.IsDBNull(3) ? 0 : r.GetInt32(3)
                    };
                    usersById[r.GetInt64(0)] = user;
                    data.Users.Add(user);
                }
            }
            using (var cmd = Cmd("SELECT user_row_id, msg_id, time, text, is_whisper FROM recorder_messages ORDER BY user_row_id, sort_order, id"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    long uid = r.GetInt64(0);
                    if (!usersById.TryGetValue(uid, out var user)) continue;
                    user.Messages.Add(new RecorderMessageData
                    {
                        Id = r.IsDBNull(1) ? "" : r.GetString(1),
                        Time = r.IsDBNull(2) ? "" : r.GetString(2),
                        Text = r.IsDBNull(3) ? "" : r.GetString(3),
                        IsWhisper = !r.IsDBNull(4) && r.GetInt32(4) != 0
                    });
                }
            }
            LoadLangTexts("SELECT lang, text FROM answering_messages ORDER BY lang, sort_order, id", data.AnsweringByLang);
            return data;
        }
    }

    public static void SaveRecorder(RecorderData data)
    {
        lock (Gate)
        {
            using var tx = Conn.BeginTransaction();
            ExecTx(tx, "INSERT INTO recorder_settings (id, trigger, confirm_receipt) VALUES (1, @t, @c) ON CONFLICT(id) DO UPDATE SET trigger=@t, confirm_receipt=@c",
                ("@t", string.IsNullOrWhiteSpace(data.Trigger) ? "RMsg" : data.Trigger),
                ("@c", data.ConfirmReceipt ? 1 : 0));
            ExecTx(tx, "DELETE FROM recorder_messages");
            ExecTx(tx, "DELETE FROM recorder_users");
            int uOrder = 0;
            foreach (var user in data.Users)
            {
                if (user == null || string.IsNullOrWhiteSpace(user.Name) || user.Messages == null || user.Messages.Count == 0)
                    continue;
                long id;
                using (var cmd = CmdTx(tx,
                    "INSERT INTO recorder_users (name, user_id, receipt_index, sort_order) VALUES (@n, @u, @r, @o)"))
                {
                    cmd.Parameters.AddWithValue("@n", user.Name.Trim());
                    cmd.Parameters.AddWithValue("@u", user.UserId ?? "");
                    cmd.Parameters.AddWithValue("@r", Math.Max(0, user.ReceiptIndex));
                    cmd.Parameters.AddWithValue("@o", uOrder++);
                    cmd.ExecuteNonQuery();
                    id = LastInsertId(tx);
                }
                int mOrder = 0;
                foreach (var msg in user.Messages)
                {
                    if (msg == null || string.IsNullOrWhiteSpace(msg.Text)) continue;
                    ExecTx(tx, "INSERT INTO recorder_messages (user_row_id, msg_id, time, text, is_whisper, sort_order) VALUES (@u, @i, @t, @x, @w, @o)",
                        ("@u", id),
                        ("@i", string.IsNullOrWhiteSpace(msg.Id) ? Guid.NewGuid().ToString("N") : msg.Id),
                        ("@t", msg.Time ?? ""),
                        ("@x", msg.Text),
                        ("@w", msg.IsWhisper ? 1 : 0),
                        ("@o", mOrder++));
                }
            }
            ExecTx(tx, "DELETE FROM answering_messages");
            foreach (var kv in data.AnsweringByLang)
                InsertLangTexts(tx, "answering_messages", kv.Key, kv.Value);
            tx.Commit();
        }
    }

    // ── UI layout ────────────────────────────────────────────────────────────

    public static UiLayoutData LoadUiLayout()
    {
        lock (Gate)
        {
            var data = new UiLayoutData();
            using var cmd = Cmd(@"SELECT width, height, center_x, center_y, window_state, left_col_ratio, left_col_width, right_col_width,
                activity_log_height, welcome_expanded, bot_settings_expanded, recorder_expanded, dm_settings_expanded, language
                FROM ui_layout WHERE id = 1");
            using var r = cmd.ExecuteReader();
            if (!r.Read()) return data;
            data.HasRow = true;
            data.Width = r.IsDBNull(0) ? 0 : r.GetDouble(0);
            data.Height = r.IsDBNull(1) ? 0 : r.GetDouble(1);
            data.CenterX = r.IsDBNull(2) ? 0 : r.GetDouble(2);
            data.CenterY = r.IsDBNull(3) ? 0 : r.GetDouble(3);
            data.WindowState = r.IsDBNull(4) ? "Normal" : r.GetString(4);
            data.LeftColRatio = r.IsDBNull(5) ? 0 : r.GetDouble(5);
            data.LeftColWidth = r.IsDBNull(6) ? 0 : r.GetDouble(6);
            data.RightColWidth = r.IsDBNull(7) ? 0 : r.GetDouble(7);
            data.ActivityLogHeight = r.IsDBNull(8) ? 0 : r.GetDouble(8);
            data.WelcomeExpanded = !r.IsDBNull(9) && r.GetInt32(9) != 0;
            data.BotSettingsExpanded = !r.IsDBNull(10) && r.GetInt32(10) != 0;
            data.RecorderExpanded = !r.IsDBNull(11) && r.GetInt32(11) != 0;
            data.DmSettingsExpanded = !r.IsDBNull(12) && r.GetInt32(12) != 0;
            data.Language = r.IsDBNull(13) || string.IsNullOrWhiteSpace(r.GetString(13)) ? "en" : r.GetString(13);
            return data;
        }
    }

    public static void SaveUiLayout(UiLayoutData data)
    {
        lock (Gate)
        {
            Exec(@"INSERT INTO ui_layout (id, width, height, center_x, center_y, window_state, left_col_ratio, left_col_width, right_col_width,
                    activity_log_height, welcome_expanded, bot_settings_expanded, recorder_expanded, dm_settings_expanded, language)
                VALUES (1, @w, @h, @cx, @cy, @ws, @lr, @lw, @rw, @ah, @we, @be, @re, @de, @lang)
                ON CONFLICT(id) DO UPDATE SET
                    width=@w, height=@h, center_x=@cx, center_y=@cy, window_state=@ws, left_col_ratio=@lr,
                    left_col_width=@lw, right_col_width=@rw, activity_log_height=@ah, welcome_expanded=@we,
                    bot_settings_expanded=@be, recorder_expanded=@re, dm_settings_expanded=@de, language=@lang",
                ("@w", data.Width), ("@h", data.Height), ("@cx", data.CenterX), ("@cy", data.CenterY),
                ("@ws", data.WindowState ?? "Normal"), ("@lr", data.LeftColRatio), ("@lw", data.LeftColWidth),
                ("@rw", data.RightColWidth), ("@ah", data.ActivityLogHeight),
                ("@we", data.WelcomeExpanded ? 1 : 0), ("@be", data.BotSettingsExpanded ? 1 : 0),
                ("@re", data.RecorderExpanded ? 1 : 0), ("@de", data.DmSettingsExpanded ? 1 : 0),
                ("@lang", string.IsNullOrWhiteSpace(data.Language) ? "en" : data.Language));
        }
    }

    // ── AI settings (keys stored DPAPI-wrapped) ──────────────────────────────

    public static AiSettingsData LoadAiSettings()
    {
        lock (Gate)
        {
            var data = new AiSettingsData();
            using (var cmd = Cmd("SELECT selected_provider, bot_display_name, companion_ai_trigger FROM ai_settings WHERE id = 1"))
            using (var r = cmd.ExecuteReader())
            {
                if (!r.Read()) return data;
                data.HasRow = true;
                data.SelectedProvider = r.IsDBNull(0) || string.IsNullOrWhiteSpace(r.GetString(0)) ? "Grok" : r.GetString(0);
                data.BotDisplayName = r.IsDBNull(1) ? "" : r.GetString(1);
                data.CompanionAiTrigger = r.IsDBNull(2) ? "" : r.GetString(2);
            }
            using (var cmd = Cmd("SELECT name, api_key_protected, endpoint, model, temperature, max_tokens, enabled FROM ai_providers"))
            using (var r = cmd.ExecuteReader())
            {
                while (r.Read())
                {
                    string name = r.GetString(0);
                    if (string.IsNullOrWhiteSpace(name)) continue;
                    data.Providers[name] = new AiProviderData
                    {
                        ApiKeyProtected = r.IsDBNull(1) ? "" : r.GetString(1),
                        Endpoint = r.IsDBNull(2) ? "" : r.GetString(2),
                        Model = r.IsDBNull(3) ? "" : r.GetString(3),
                        Temperature = r.IsDBNull(4) ? 0.7 : r.GetDouble(4),
                        MaxTokens = r.IsDBNull(5) ? 1024 : r.GetInt32(5),
                        Enabled = !r.IsDBNull(6) && r.GetInt32(6) != 0
                    };
                }
            }
            return data;
        }
    }

    public static void SaveAiSettings(AiSettingsData data)
    {
        lock (Gate)
        {
            using var tx = Conn.BeginTransaction();
            ExecTx(tx, @"INSERT INTO ai_settings (id, selected_provider, bot_display_name, companion_ai_trigger)
                VALUES (1, @p, @n, @t)
                ON CONFLICT(id) DO UPDATE SET selected_provider=@p, bot_display_name=@n, companion_ai_trigger=@t",
                ("@p", string.IsNullOrWhiteSpace(data.SelectedProvider) ? "Grok" : data.SelectedProvider),
                ("@n", data.BotDisplayName ?? ""),
                ("@t", data.CompanionAiTrigger ?? ""));
            ExecTx(tx, "DELETE FROM ai_providers");
            foreach (var kv in data.Providers)
            {
                if (string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null) continue;
                ExecTx(tx, @"INSERT INTO ai_providers (name, api_key_protected, endpoint, model, temperature, max_tokens, enabled)
                    VALUES (@name, @key, @ep, @m, @temp, @tok, @en)",
                    ("@name", kv.Key.Trim()),
                    ("@key", kv.Value.ApiKeyProtected ?? ""),
                    ("@ep", kv.Value.Endpoint ?? ""),
                    ("@m", kv.Value.Model ?? ""),
                    ("@temp", kv.Value.Temperature),
                    ("@tok", Math.Max(1, kv.Value.MaxTokens)),
                    ("@en", kv.Value.Enabled ? 1 : 0));
            }
            tx.Commit();
            TryDeleteLegacyFile("ai_settings.json");
        }
    }

    private static bool HasAiSettingsRow()
    {
        using var cmd = Cmd("SELECT 1 FROM ai_settings WHERE id = 1 LIMIT 1");
        return cmd.ExecuteScalar() != null;
    }

    private static void MigrateAiJsonIfNeeded()
    {
        if (HasAiSettingsRow()) return;
        try { MigrateAiJson(); } catch { }
    }

    private static void MigrateAiJson()
    {
        using var doc = TryParse(Path.Combine(UserDataPaths.Root, "ai_settings.json"));
        if (doc == null) return;
        var root = doc.RootElement;
        var data = new AiSettingsData
        {
            HasRow = true,
            SelectedProvider = FirstString(root, "Grok", "SelectedProvider", "selectedProvider"),
            BotDisplayName = FirstString(root, "", "BotDisplayName", "botDisplayName"),
            CompanionAiTrigger = FirstString(root, "", "CompanionAiTrigger", "companionAiTrigger")
        };
        JsonElement providers = default;
        bool hasProviders = root.TryGetProperty("Providers", out providers) ||
                            root.TryGetProperty("providers", out providers);
        if (hasProviders && providers.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in providers.EnumerateObject())
            {
                if (prop.Value.ValueKind != JsonValueKind.Object) continue;
                var el = prop.Value;
                data.Providers[prop.Name] = new AiProviderData
                {
                    ApiKeyProtected = FirstString(el, "", "ApiKey", "apiKey"),
                    Endpoint = FirstString(el, "", "Endpoint", "endpoint"),
                    Model = FirstString(el, "", "Model", "model"),
                    Temperature = FirstDouble(el, 0.7, "Temperature", "temperature"),
                    MaxTokens = FirstInt(el, 1024, "MaxTokens", "maxTokens"),
                    Enabled = FirstBool(el, false, "Enabled", "enabled")
                };
            }
        }
        SaveAiSettings(data);
    }

    private static void TryDeleteLegacyFile(string fileName)
    {
        TryDeletePath(Path.Combine(UserDataPaths.Root, fileName));
        TryDeletePath(Path.Combine(UserDataPaths.Root, fileName + ".tmp"));
    }

    private static void TryDeletePath(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch { }
    }

    /// <summary>
    /// SQLite is the store now. Remove leftover config JSON from the data folder
    /// (same as ai_settings.json after import). Does not touch companion.db,
    /// WebView2, or ChromeDebug.
    /// </summary>
    private static void DeleteLegacyJsonFiles()
    {
        bool keepAiJson = !HasAiSettingsRow();
        foreach (string name in LegacyFiles)
        {
            if (keepAiJson && name.Equals("ai_settings.json", StringComparison.OrdinalIgnoreCase))
                continue;
            TryDeleteLegacyFile(name);
        }
        try
        {
            foreach (string stem in LegacyStems)
            {
                foreach (string path in Directory.GetFiles(UserDataPaths.Root, stem + "-*.json"))
                    TryDeletePath(path);
            }
        }
        catch { }
    }

    private static string FirstString(JsonElement el, string fallback, params string[] names)
    {
        foreach (string name in names)
        {
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
                return p.GetString() ?? fallback;
        }
        return fallback;
    }

    private static double FirstDouble(JsonElement el, double fallback, params string[] names)
    {
        foreach (string name in names)
        {
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out double d))
                return d;
        }
        return fallback;
    }

    private static int FirstInt(JsonElement el, int fallback, params string[] names)
    {
        foreach (string name in names)
        {
            if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetInt32(out int n))
                return n;
        }
        return fallback;
    }

    private static bool FirstBool(JsonElement el, bool fallback, params string[] names)
    {
        foreach (string name in names)
        {
            if (!el.TryGetProperty(name, out var p)) continue;
            if (p.ValueKind == JsonValueKind.True) return true;
            if (p.ValueKind == JsonValueKind.False) return false;
        }
        return fallback;
    }

    // ── Schema ───────────────────────────────────────────────────────────────

    private static void CreateSchema()
    {
        foreach (string sql in new[]
        {
            "CREATE TABLE IF NOT EXISTS meta (key TEXT PRIMARY KEY, value TEXT NOT NULL)",
            @"CREATE TABLE IF NOT EXISTS welcome_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                msg1_enabled INTEGER NOT NULL DEFAULT 1,
                msg1_as_whisper INTEGER NOT NULL DEFAULT 0,
                msg2_enabled INTEGER NOT NULL DEFAULT 0,
                msg2_as_whisper INTEGER NOT NULL DEFAULT 1)",
            @"CREATE TABLE IF NOT EXISTS welcome_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                lang TEXT NOT NULL,
                kind TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                text TEXT NOT NULL)",
            @"CREATE TABLE IF NOT EXISTS trigger_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                listen_to_chat INTEGER NOT NULL DEFAULT 1,
                active_category TEXT NOT NULL DEFAULT 'General')",
            @"CREATE TABLE IF NOT EXISTS trigger_categories (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                lang TEXT NOT NULL,
                name TEXT NOT NULL,
                color_hex TEXT NOT NULL DEFAULT '#7DD3FC',
                cooldown_seconds INTEGER NOT NULL DEFAULT 30,
                allow_repeat INTEGER NOT NULL DEFAULT 0,
                use_name_prefix INTEGER NOT NULL DEFAULT 0,
                UNIQUE(lang, name))",
            @"CREATE TABLE IF NOT EXISTS trigger_entries (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                category_id INTEGER NOT NULL REFERENCES trigger_categories(id) ON DELETE CASCADE,
                command TEXT NOT NULL,
                response TEXT NOT NULL,
                sort_order INTEGER NOT NULL)",
            @"CREATE TABLE IF NOT EXISTS trigger_active_category (
                lang TEXT PRIMARY KEY,
                name TEXT NOT NULL)",
            @"CREATE TABLE IF NOT EXISTS console_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                as_whisper INTEGER NOT NULL DEFAULT 0,
                prefix_user_name INTEGER NOT NULL DEFAULT 1)",
            @"CREATE TABLE IF NOT EXISTS console_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                lang TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                text TEXT NOT NULL)",
            @"CREATE TABLE IF NOT EXISTS recorder_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                trigger TEXT NOT NULL DEFAULT 'RMsg',
                confirm_receipt INTEGER NOT NULL DEFAULT 1)",
            @"CREATE TABLE IF NOT EXISTS recorder_users (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                name TEXT NOT NULL,
                user_id TEXT NOT NULL DEFAULT '',
                receipt_index INTEGER NOT NULL DEFAULT 0,
                sort_order INTEGER NOT NULL)",
            @"CREATE TABLE IF NOT EXISTS recorder_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                user_row_id INTEGER NOT NULL REFERENCES recorder_users(id) ON DELETE CASCADE,
                msg_id TEXT NOT NULL,
                time TEXT NOT NULL DEFAULT '',
                text TEXT NOT NULL,
                is_whisper INTEGER NOT NULL DEFAULT 0,
                sort_order INTEGER NOT NULL)",
            @"CREATE TABLE IF NOT EXISTS answering_messages (
                id INTEGER PRIMARY KEY AUTOINCREMENT,
                lang TEXT NOT NULL,
                sort_order INTEGER NOT NULL,
                text TEXT NOT NULL)",
            @"CREATE TABLE IF NOT EXISTS ui_layout (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                width REAL, height REAL, center_x REAL, center_y REAL,
                window_state TEXT, left_col_ratio REAL, left_col_width REAL, right_col_width REAL,
                activity_log_height REAL, welcome_expanded INTEGER, bot_settings_expanded INTEGER,
                recorder_expanded INTEGER, dm_settings_expanded INTEGER,
                language TEXT NOT NULL DEFAULT 'en')",
            @"CREATE TABLE IF NOT EXISTS ai_settings (
                id INTEGER PRIMARY KEY CHECK (id = 1),
                selected_provider TEXT NOT NULL DEFAULT 'Grok',
                bot_display_name TEXT NOT NULL DEFAULT '',
                companion_ai_trigger TEXT NOT NULL DEFAULT '')",
            @"CREATE TABLE IF NOT EXISTS ai_providers (
                name TEXT PRIMARY KEY,
                api_key_protected TEXT NOT NULL DEFAULT '',
                endpoint TEXT NOT NULL DEFAULT '',
                model TEXT NOT NULL DEFAULT '',
                temperature REAL NOT NULL DEFAULT 0.7,
                max_tokens INTEGER NOT NULL DEFAULT 1024,
                enabled INTEGER NOT NULL DEFAULT 0)"
        })
            Exec(sql);
    }

    // ── JSON import (one-shot) ───────────────────────────────────────────────

    private static readonly string[] LegacyFiles =
    {
        "messages.json", "triggers.json", "commands.json",
        "console.json", "dm_messages.json", "recorder.json", "ui_layout.json",
        "ai_settings.json"
    };

    private static readonly string[] LegacyStems = { "messages", "triggers", "console", "answering" };

    private static bool HasLegacyJson()
    {
        foreach (string name in LegacyFiles)
        {
            string path = Path.Combine(UserDataPaths.Root, name);
            if (File.Exists(path) && new FileInfo(path).Length > 2) return true;
        }
        foreach (string stem in LegacyStems)
        {
            if (UserDataPaths.ListLangCodes(stem).Count > 0) return true;
        }
        return false;
    }

    private static void MigrateFromJson()
    {
        try { MigrateWelcomeJson(); } catch { }
        try { MigrateTriggersJson(); } catch { }
        try { MigrateConsoleJson(); } catch { }
        try { MigrateRecorderJson(); } catch { }
        try { MigrateUiLayoutJson(); } catch { }
        try { MigrateAiJson(); } catch { }
    }

    private static void MigrateWelcomeJson()
    {
        var data = new WelcomeData();
        using (var doc = TryParse(Path.Combine(UserDataPaths.Root, "messages.json")))
        {
            if (doc != null)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("msg1", out var m1) && m1.ValueKind == JsonValueKind.Object)
                {
                    data.Msg1Enabled = ReadBool(m1, "enabled", true);
                    data.Msg1AsWhisper = ReadAsWhisper(m1, false);
                    MergeWelcomeDict(data.Msg1, m1);
                    if (root.TryGetProperty("msg2", out var m2) && m2.ValueKind == JsonValueKind.Object)
                    {
                        data.Msg2Enabled = ReadBool(m2, "enabled", false);
                        data.Msg2AsWhisper = ReadAsWhisper(m2, true);
                        MergeWelcomeDict(data.Msg2, m2);
                    }
                }
                else
                {
                    if (root.TryGetProperty("events", out var eventsEl) && eventsEl.ValueKind == JsonValueKind.Object)
                    {
                        JsonElement ev = default;
                        bool hasEv = false;
                        if (root.TryGetProperty("joinEvent", out var je) && je.ValueKind == JsonValueKind.String)
                            hasEv = eventsEl.TryGetProperty(je.GetString() ?? "Welcoming", out ev);
                        if (!hasEv)
                        {
                            foreach (var prop in eventsEl.EnumerateObject())
                            {
                                ev = prop.Value;
                                hasEv = true;
                                break;
                            }
                        }
                        if (hasEv && ev.ValueKind == JsonValueKind.Object)
                        {
                            var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(ev.GetRawText());
                            if (dict != null)
                            {
                                foreach (var kv in dict)
                                    if (kv.Value != null && kv.Value.Count > 0)
                                        data.Msg1[kv.Key] = kv.Value;
                            }
                        }
                    }
                    if (root.TryGetProperty("welcomeExtra", out var we) && we.ValueKind == JsonValueKind.Object)
                    {
                        data.Msg2Enabled = ReadBool(we, "sendExtra", false);
                        data.Msg2AsWhisper = ReadBool(we, "asWhisper", true);
                        if (we.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in msgs.EnumerateObject())
                            {
                                string text = prop.Value.GetString() ?? "";
                                if (string.IsNullOrWhiteSpace(text)) continue;
                                if (!data.Msg2.ContainsKey(prop.Name))
                                    data.Msg2[prop.Name] = new List<string>();
                                data.Msg2[prop.Name].Add(text);
                            }
                        }
                    }
                }
            }
        }
        foreach (string lang in UserDataPaths.ListLangCodes("messages"))
        {
            using var doc = TryParse(UserDataPaths.LangFile("messages", lang));
            if (doc == null) continue;
            var root = doc.RootElement;
            if (root.TryGetProperty("msg1", out var m1))
                MergeWelcomeLangList(data.Msg1, lang, m1);
            if (root.TryGetProperty("msg2", out var m2))
                MergeWelcomeLangList(data.Msg2, lang, m2);
        }
        SaveWelcome(data);
    }

    private static void MergeWelcomeDict(Dictionary<string, List<string>> bag, JsonElement el)
    {
        if (!el.TryGetProperty("messages", out var msgs) || msgs.ValueKind != JsonValueKind.Object) return;
        var dict = JsonSerializer.Deserialize<Dictionary<string, List<string>>>(msgs.GetRawText());
        if (dict == null) return;
        foreach (var kv in dict)
        {
            if (kv.Value == null || kv.Value.Count == 0) continue;
            bag[kv.Key] = kv.Value;
        }
    }

    private static void MergeWelcomeLangList(Dictionary<string, List<string>> bag, string lang, JsonElement el)
    {
        var list = ReadStringArray(el);
        if (list.Count > 0) bag[lang] = list;
    }

    private static void MigrateTriggersJson()
    {
        var data = new TriggerData();
        string settingsPath = Path.Combine(UserDataPaths.Root, "triggers.json");
        if (!File.Exists(settingsPath))
            settingsPath = Path.Combine(UserDataPaths.Root, "commands.json");
        using (var doc = TryParse(settingsPath))
        {
            if (doc != null)
            {
                var root = doc.RootElement;
                int schema = 1;
                if (root.TryGetProperty("schemaVersion", out var sv) && sv.ValueKind == JsonValueKind.Number)
                    schema = sv.GetInt32();
                if (root.TryGetProperty("activeCategory", out var ac) && ac.ValueKind == JsonValueKind.String)
                    data.ActiveCategory = ac.GetString() ?? "General";
                if (root.TryGetProperty("listenToChat", out var lc))
                    data.ListenToChat = lc.ValueKind != JsonValueKind.False;

                if (root.TryGetProperty("languages", out var langsEl) && langsEl.ValueKind == JsonValueKind.Object)
                    ReadTriggerSettingsByLanguage(data, langsEl);
                else if (root.TryGetProperty("categories", out var catsEl) && catsEl.ValueKind == JsonValueKind.Object)
                {
                    if (schema >= 2)
                        ReadCommandsSchemaV2(data, catsEl);
                    else
                    {
                        var loaded = JsonSerializer.Deserialize<Dictionary<string, Dictionary<string, List<TriggerEntryData>>>>(catsEl.GetRawText());
                        if (loaded != null)
                        {
                            foreach (var catKv in loaded)
                            {
                                if (string.IsNullOrWhiteSpace(catKv.Key) || catKv.Value == null) continue;
                                if (!data.Categories.ContainsKey(catKv.Key))
                                    data.Categories[catKv.Key] = new Dictionary<string, List<TriggerEntryData>>(StringComparer.OrdinalIgnoreCase);
                                foreach (var langKv in catKv.Value)
                                {
                                    if (langKv.Value == null || langKv.Value.Count == 0) continue;
                                    data.Categories[catKv.Key][langKv.Key] = langKv.Value;
                                }
                            }
                        }
                    }
                }
            }
        }
        foreach (string lang in UserDataPaths.ListLangCodes("triggers"))
        {
            using var doc = TryParse(UserDataPaths.LangFile("triggers", lang));
            if (doc == null) continue;
            if (!doc.RootElement.TryGetProperty("categories", out var cats) || cats.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var prop in cats.EnumerateObject())
            {
                string cat = prop.Name.Trim();
                if (string.IsNullOrEmpty(cat) || prop.Value.ValueKind != JsonValueKind.Array) continue;
                var list = JsonSerializer.Deserialize<List<TriggerEntryData>>(prop.Value.GetRawText()) ?? new();
                list.RemoveAll(e => e == null || string.IsNullOrWhiteSpace(e.Command));
                if (list.Count == 0) continue;
                if (!data.Categories.ContainsKey(cat))
                    data.Categories[cat] = new Dictionary<string, List<TriggerEntryData>>(StringComparer.OrdinalIgnoreCase);
                data.Categories[cat][lang] = list;
            }
        }
        SaveTriggers(data);
    }

    private static void ReadTriggerSettingsByLanguage(TriggerData data, JsonElement langsEl)
    {
        foreach (var langProp in langsEl.EnumerateObject())
        {
            string lang = langProp.Name.Trim().ToLowerInvariant();
            if (string.IsNullOrEmpty(lang) || langProp.Value.ValueKind != JsonValueKind.Object) continue;
            if (langProp.Value.TryGetProperty("activeCategory", out var ac) && ac.ValueKind == JsonValueKind.String)
            {
                string name = (ac.GetString() ?? "").Trim();
                if (!string.IsNullOrEmpty(name))
                    data.ActiveCategoryByLang[lang] = name;
            }
            var bag = new Dictionary<string, CategorySettingsData>(StringComparer.OrdinalIgnoreCase);
            if (langProp.Value.TryGetProperty("categories", out var cats) && cats.ValueKind == JsonValueKind.Object)
            {
                foreach (var catProp in cats.EnumerateObject())
                {
                    string cat = catProp.Name.Trim();
                    if (string.IsNullOrEmpty(cat)) continue;
                    JsonElement setEl = catProp.Value;
                    if (catProp.Value.TryGetProperty("settings", out var inner) && inner.ValueKind == JsonValueKind.Object)
                        setEl = inner;
                    bag[cat] = ReadCategorySettings(setEl);
                }
            }
            data.SettingsByLang[lang] = bag;
        }
    }

    private static void ReadCommandsSchemaV2(TriggerData data, JsonElement catsEl)
    {
        var settings = new Dictionary<string, CategorySettingsData>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in catsEl.EnumerateObject())
        {
            string name = prop.Name.Trim();
            if (string.IsNullOrEmpty(name)) continue;
            var langs = new Dictionary<string, List<TriggerEntryData>>(StringComparer.OrdinalIgnoreCase);
            if (prop.Value.TryGetProperty("languages", out var langEl) && langEl.ValueKind == JsonValueKind.Object)
            {
                var loaded = JsonSerializer.Deserialize<Dictionary<string, List<TriggerEntryData>>>(langEl.GetRawText());
                if (loaded != null)
                {
                    foreach (var lk in loaded)
                    {
                        if (lk.Value == null || lk.Value.Count == 0) continue;
                        langs[lk.Key] = lk.Value;
                    }
                }
            }
            data.Categories[name] = langs;
            var cs = new CategorySettingsData();
            if (prop.Value.TryGetProperty("settings", out var setEl) && setEl.ValueKind == JsonValueKind.Object)
                cs = ReadCategorySettings(setEl);
            if (string.IsNullOrWhiteSpace(cs.ColorHex))
                cs.ColorHex = "#7DD3FC";
            settings[name] = cs;
        }
        data.SettingsByLang["en"] = settings;
    }

    private static CategorySettingsData ReadCategorySettings(JsonElement setEl)
    {
        var cs = new CategorySettingsData();
        if (setEl.ValueKind != JsonValueKind.Object) return cs;
        cs.AllowRepeatTriggers = ReadBool(setEl, "allowRepeatTriggers", false);
        if (setEl.TryGetProperty("cooldownSeconds", out var cd) && cd.ValueKind == JsonValueKind.Number)
            cs.CooldownSeconds = Math.Clamp(cd.GetInt32(), 1, 3600);
        if (setEl.TryGetProperty("useNamePrefix", out var un) &&
            (un.ValueKind == JsonValueKind.True || un.ValueKind == JsonValueKind.False))
            cs.UseNamePrefix = un.GetBoolean();
        else if (setEl.TryGetProperty("suppressNamePrefix", out var sn) &&
                 (sn.ValueKind == JsonValueKind.True || sn.ValueKind == JsonValueKind.False))
            cs.UseNamePrefix = !sn.GetBoolean();
        if (setEl.TryGetProperty("colorHex", out var ch) && ch.ValueKind == JsonValueKind.String)
            cs.ColorHex = ch.GetString() ?? cs.ColorHex;
        return cs;
    }

    private static void MigrateConsoleJson()
    {
        var data = new ConsoleData();
        string path = Path.Combine(UserDataPaths.Root, "console.json");
        if (!File.Exists(path))
            path = Path.Combine(UserDataPaths.Root, "dm_messages.json");
        using (var doc = TryParse(path))
        {
            if (doc != null)
            {
                var root = doc.RootElement;
                data.AsWhisper = ReadAsWhisper(root, false);
                if (root.TryGetProperty("prefixUserName", out var px) &&
                    (px.ValueKind == JsonValueKind.True || px.ValueKind == JsonValueKind.False))
                    data.PrefixUserName = px.GetBoolean();
                if (root.TryGetProperty("messages", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    var list = ReadStringArray(arr);
                    if (list.Count > 0) data.Messages["en"] = list;
                }
            }
        }
        foreach (string lang in UserDataPaths.ListLangCodes("console"))
        {
            using var doc = TryParse(UserDataPaths.LangFile("console", lang));
            if (doc == null) continue;
            if (doc.RootElement.TryGetProperty("messages", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var list = ReadStringArray(arr);
                if (list.Count > 0) data.Messages[lang] = list;
            }
        }
        SaveConsole(data);
    }

    private static void MigrateRecorderJson()
    {
        var data = new RecorderData();
        using (var doc = TryParse(Path.Combine(UserDataPaths.Root, "recorder.json")))
        {
            if (doc != null)
            {
                var root = doc.RootElement;
                if (root.TryGetProperty("trigger", out var tr) && tr.ValueKind == JsonValueKind.String)
                {
                    string t = (tr.GetString() ?? "").Trim();
                    if (!string.IsNullOrEmpty(t)) data.Trigger = t.TrimStart('!');
                }
                if (root.TryGetProperty("confirmReceipt", out var cr) &&
                    (cr.ValueKind == JsonValueKind.True || cr.ValueKind == JsonValueKind.False))
                    data.ConfirmReceipt = cr.GetBoolean();
                if (root.TryGetProperty("receiptMessages", out var rm) && rm.ValueKind == JsonValueKind.Array)
                {
                    var legacy = ReadStringArray(rm);
                    if (legacy.Count > 0) data.AnsweringByLang["en"] = legacy;
                }
                if (root.TryGetProperty("users", out var users) && users.ValueKind == JsonValueKind.Array)
                {
                    foreach (var uel in users.EnumerateArray())
                    {
                        string name = uel.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                        if (string.IsNullOrWhiteSpace(name)) continue;
                        var user = new RecorderUserData
                        {
                            Name = name.Trim(),
                            UserId = uel.TryGetProperty("userId", out var uidEl) ? uidEl.GetString() ?? "" : "",
                            ReceiptIndex = uel.TryGetProperty("receiptIndex", out var ri) && ri.ValueKind == JsonValueKind.Number
                                ? Math.Max(0, ri.GetInt32()) : 0
                        };
                        if (uel.TryGetProperty("messages", out var msgs) && msgs.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var mel in msgs.EnumerateArray())
                            {
                                string text = mel.TryGetProperty("text", out var tx) ? tx.GetString() ?? "" : "";
                                if (string.IsNullOrWhiteSpace(text)) continue;
                                user.Messages.Add(new RecorderMessageData
                                {
                                    Id = mel.TryGetProperty("id", out var idEl) ? idEl.GetString() ?? "" : "",
                                    Time = mel.TryGetProperty("time", out var tm) ? tm.GetString() ?? "" : "",
                                    Text = text,
                                    IsWhisper = mel.TryGetProperty("whisper", out var w) && w.ValueKind == JsonValueKind.True
                                });
                            }
                        }
                        if (user.Messages.Count > 0)
                            data.Users.Add(user);
                    }
                }
            }
        }
        foreach (string lang in UserDataPaths.ListLangCodes("answering"))
        {
            using var doc = TryParse(UserDataPaths.LangFile("answering", lang));
            if (doc == null) continue;
            if (doc.RootElement.TryGetProperty("messages", out var arr) && arr.ValueKind == JsonValueKind.Array)
            {
                var list = ReadStringArray(arr);
                if (list.Count > 0) data.AnsweringByLang[lang] = list;
            }
        }
        SaveRecorder(data);
    }

    private static void MigrateUiLayoutJson()
    {
        using var doc = TryParse(Path.Combine(UserDataPaths.Root, "ui_layout.json"));
        if (doc == null) return;
        var root = doc.RootElement;
        var data = new UiLayoutData
        {
            HasRow = true,
            Width = ReadDouble(root, "Width"),
            Height = ReadDouble(root, "Height"),
            CenterX = ReadDouble(root, "CenterX"),
            CenterY = ReadDouble(root, "CenterY"),
            WindowState = ReadString(root, "WindowState", "Normal"),
            LeftColRatio = ReadDouble(root, "LeftColRatio"),
            LeftColWidth = ReadDouble(root, "LeftColWidth"),
            RightColWidth = ReadDouble(root, "RightColWidth"),
            ActivityLogHeight = ReadDouble(root, "ActivityLogHeight"),
            WelcomeExpanded = ReadBool(root, "WelcomeExpanded", false),
            BotSettingsExpanded = ReadBool(root, "BotSettingsExpanded", false),
            RecorderExpanded = ReadBool(root, "RecorderExpanded", false),
            DmSettingsExpanded = ReadBool(root, "DmSettingsExpanded", false),
            Language = ReadString(root, "Language", "en")
        };
        SaveUiLayout(data);
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private static SqliteConnection Conn =>
        _conn ?? throw new InvalidOperationException("AppDatabase.Initialize() was not called.");

    private static SqliteCommand Cmd(string sql)
    {
        var cmd = Conn.CreateCommand();
        cmd.CommandText = sql;
        return cmd;
    }

    private static SqliteCommand CmdTx(SqliteTransaction tx, string sql)
    {
        var cmd = Conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = sql;
        return cmd;
    }

    private static void Exec(string sql, params (string name, object value)[] args)
    {
        using var cmd = Cmd(sql);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static void ExecTx(SqliteTransaction tx, string sql, params (string name, object value)[] args)
    {
        using var cmd = CmdTx(tx, sql);
        foreach (var (name, value) in args)
            cmd.Parameters.AddWithValue(name, value ?? DBNull.Value);
        cmd.ExecuteNonQuery();
    }

    private static long LastInsertId(SqliteTransaction tx)
    {
        using var cmd = CmdTx(tx, "SELECT last_insert_rowid()");
        object? v = cmd.ExecuteScalar();
        return v is long l ? l : Convert.ToInt64(v);
    }

    private static bool IsFlag(string key)
    {
        using var cmd = Cmd("SELECT value FROM meta WHERE key = @k");
        cmd.Parameters.AddWithValue("@k", key);
        var v = cmd.ExecuteScalar() as string;
        return v == "1" || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase);
    }

    private static void SetFlag(string key, bool value) => SetMeta(key, value ? "1" : "0");

    private static void SetMeta(string key, string value)
    {
        Exec("INSERT INTO meta (key, value) VALUES (@k, @v) ON CONFLICT(key) DO UPDATE SET value=@v",
            ("@k", key), ("@v", value));
    }

    private static void InsertMessageList(SqliteTransaction tx, string table, string lang, string kind, List<string>? list)
    {
        if (list == null) return;
        int order = 0;
        foreach (string text in list)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            ExecTx(tx, $"INSERT INTO {table} (lang, kind, sort_order, text) VALUES (@l, @k, @o, @t)",
                ("@l", lang), ("@k", kind), ("@o", order++), ("@t", text.Trim()));
        }
    }

    private static void InsertLangTexts(SqliteTransaction tx, string table, string lang, List<string>? list)
    {
        if (list == null) return;
        int order = 0;
        foreach (string text in list)
        {
            if (string.IsNullOrWhiteSpace(text)) continue;
            ExecTx(tx, $"INSERT INTO {table} (lang, sort_order, text) VALUES (@l, @o, @t)",
                ("@l", lang), ("@o", order++), ("@t", text.Trim()));
        }
    }

    private static void LoadLangTexts(string sql, Dictionary<string, List<string>> bag)
    {
        using var cmd = Cmd(sql);
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            string lang = r.GetString(0);
            string text = r.GetString(1);
            if (!bag.TryGetValue(lang, out var list))
            {
                list = new List<string>();
                bag[lang] = list;
            }
            list.Add(text);
        }
    }

    private static JsonDocument? TryParse(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            return JsonDocument.Parse(File.ReadAllText(path));
        }
        catch
        {
            return null;
        }
    }

    private static bool ReadBool(JsonElement el, string name, bool fallback)
    {
        if (!el.TryGetProperty(name, out var p)) return fallback;
        if (p.ValueKind == JsonValueKind.True) return true;
        if (p.ValueKind == JsonValueKind.False) return false;
        return fallback;
    }

    private static bool ReadAsWhisper(JsonElement el, bool fallback)
    {
        if (el.TryGetProperty("asWhisper", out var aw) &&
            (aw.ValueKind == JsonValueKind.True || aw.ValueKind == JsonValueKind.False))
            return aw.GetBoolean();
        if (el.TryGetProperty("delivery", out var del) && del.ValueKind == JsonValueKind.String)
            return string.Equals(del.GetString(), "whisper", StringComparison.OrdinalIgnoreCase);
        return fallback;
    }

    private static string ReadString(JsonElement el, string name, string fallback)
    {
        if (el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String)
            return p.GetString() ?? fallback;
        return fallback;
    }

    private static double ReadDouble(JsonElement el, string name)
    {
        if (!el.TryGetProperty(name, out var p)) return 0;
        if (p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out double d)) return d;
        return 0;
    }

    private static List<string> ReadStringArray(JsonElement el)
    {
        var list = new List<string>();
        if (el.ValueKind != JsonValueKind.Array) return list;
        foreach (var item in el.EnumerateArray())
        {
            string t = item.ValueKind == JsonValueKind.String
                ? item.GetString() ?? ""
                : item.ValueKind == JsonValueKind.Object && item.TryGetProperty("text", out var te)
                    ? te.GetString() ?? ""
                    : "";
            if (!string.IsNullOrWhiteSpace(t)) list.Add(t.Trim());
        }
        return list;
    }
}
