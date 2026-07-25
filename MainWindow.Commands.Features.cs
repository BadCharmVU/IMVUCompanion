using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Microsoft.Win32;

namespace IMVUCompanion;

public partial class MainWindow
{
    public const int CommandsSchemaVersion = 2;

    public sealed class CategorySettings
    {
        public bool AllowRepeatTriggers { get; set; }
        public int CooldownSeconds { get; set; } = 30;
        /// <summary>When true, public replies get "Name, " unless the template already has {name}.</summary>
        public bool UseNamePrefix { get; set; }
        public string ColorHex { get; set; } = "#7DD3FC";
    }

    private sealed class ActiveTrigger
    {
        public string Category { get; init; } = "";
        public string Trigger { get; init; } = "";
        public List<string> Responses { get; init; } = new();
    }

    private Dictionary<string, CategorySettings> _categorySettings =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>category -> set of user uids who already got a reply (when allow-repeat is off).</summary>
    private readonly Dictionary<string, HashSet<string>> _categoryRepliedOnceByUser =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>category -> uid -> last reply UTC (when allow-repeat is on).</summary>
    private readonly Dictionary<string, Dictionary<string, DateTime>> _categoryLastReplyUtcByUser =
        new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Shuffled remaining response indexes for (category, trigger) this session.</summary>
    private readonly Dictionary<(string category, string trigger), List<int>> _unusedResponseIndexes = new();

    private int _nextCategoryColorIndex;

    private static readonly string[] CategoryColorPalette =
    {
        "#7DD3FC", "#A78BFA", "#F472B6", "#4ADE80", "#FACC15",
        "#FB923C", "#38BDF8", "#C084FC", "#F87171", "#2DD4BF",
        "#E879F9", "#94A3B8"
    };

    private void ClearCommandSessionState()
    {
        _categoryRepliedOnceByUser.Clear();
        _categoryLastReplyUtcByUser.Clear();
        _unusedResponseIndexes.Clear();
    }

    private CategorySettings GetOrCreateCategorySettings(string category)
    {
        if (string.IsNullOrWhiteSpace(category)) category = "General";
        if (!_categorySettings.TryGetValue(category, out var s) || s == null)
        {
            s = new CategorySettings { ColorHex = NextCategoryColor() };
            _categorySettings[category] = s;
        }
        if (string.IsNullOrWhiteSpace(s.ColorHex))
            s.ColorHex = NextCategoryColor();
        if (s.CooldownSeconds < 1) s.CooldownSeconds = 1;
        if (s.CooldownSeconds > 3600) s.CooldownSeconds = 3600;
        return s;
    }

    private string NextCategoryColor()
    {
        string hex = CategoryColorPalette[_nextCategoryColorIndex % CategoryColorPalette.Length];
        _nextCategoryColorIndex++;
        return hex;
    }

    private static SolidColorBrush BrushFromHex(string hex)
    {
        try
        {
            var c = (System.Windows.Media.Color)ColorConverter.ConvertFromString(hex);
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }
        catch
        {
            return CreateFrozenBrush(0x7D, 0xD3, 0xFC);
        }
    }

    private List<ActiveTrigger> GetActiveTriggers()
    {
        var list = new List<ActiveTrigger>();
        if (!_listenToChat) return list;

        foreach (var catKv in _commandCategories.OrderBy(k => k.Key, StringComparer.OrdinalIgnoreCase))
        {
            var byTrigger = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var entry in GetCommandLangListOrEmpty(catKv.Value, _commandLanguage))
            {
                string cmd = NormalizeCommand(entry.Command);
                if (string.IsNullOrWhiteSpace(cmd) || string.IsNullOrWhiteSpace(entry.Response))
                    continue;
                if (!byTrigger.TryGetValue(cmd, out var responses))
                {
                    responses = new List<string>();
                    byTrigger[cmd] = responses;
                }
                responses.Add(entry.Response);
            }
            foreach (var t in byTrigger)
            {
                list.Add(new ActiveTrigger
                {
                    Category = catKv.Key,
                    Trigger = t.Key,
                    Responses = t.Value
                });
            }
        }
        return list;
    }

    /// <summary>
    /// Same normalized trigger must not exist in another category for the same language.
    /// Multiple responses for the same trigger in the same category are allowed.
    /// </summary>
    private bool TryFindCrossCategoryTriggerConflict(string trigger, string targetCategory, string lang, out string existingCategory)
    {
        existingCategory = "";
        string cmd = NormalizeCommand(trigger);
        if (string.IsNullOrEmpty(cmd)) return false;

        foreach (var catKv in _commandCategories)
        {
            if (string.Equals(catKv.Key, targetCategory, StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var entry in GetCommandLangListOrEmpty(catKv.Value, lang))
            {
                if (string.Equals(NormalizeCommand(entry.Command), cmd, StringComparison.OrdinalIgnoreCase))
                {
                    existingCategory = catKv.Key;
                    return true;
                }
            }
        }
        return false;
    }

    private void ShowBotSettingsError(string title, string message)
    {
        if (BotSettingsErrorTitle != null) BotSettingsErrorTitle.Text = title;
        if (BotSettingsErrorMessage != null) BotSettingsErrorMessage.Text = message;
        if (BotSettingsErrorOverlay != null) BotSettingsErrorOverlay.Visibility = Visibility.Visible;
    }

    private void BotSettingsErrorOk_Click(object sender, RoutedEventArgs e)
    {
        if (BotSettingsErrorOverlay != null)
            BotSettingsErrorOverlay.Visibility = Visibility.Collapsed;
    }

    private string PickResponseFromBag(string category, string trigger, List<string> responses)
    {
        if (responses == null || responses.Count == 0) return "";
        if (responses.Count == 1) return responses[0];

        var key = (category, trigger);
        if (!_unusedResponseIndexes.TryGetValue(key, out var bag) || bag == null || bag.Count == 0)
        {
            bag = Enumerable.Range(0, responses.Count).ToList();
            // Fisher–Yates
            for (int i = bag.Count - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (bag[i], bag[j]) = (bag[j], bag[i]);
            }
            _unusedResponseIndexes[key] = bag;
        }

        int idx = bag[bag.Count - 1];
        bag.RemoveAt(bag.Count - 1);
        if (idx < 0 || idx >= responses.Count) idx = 0;
        return responses[idx];
    }

    private bool AllowCommandReplyForUser(string category, string userId, CategorySettings settings)
    {
        if (string.IsNullOrWhiteSpace(userId))
            return true; // should not happen on IMVU; fail open

        if (!settings.AllowRepeatTriggers)
        {
            if (!_categoryRepliedOnceByUser.TryGetValue(category, out var set))
            {
                set = new HashSet<string>(StringComparer.Ordinal);
                _categoryRepliedOnceByUser[category] = set;
            }
            if (set.Contains(userId))
                return false;
            set.Add(userId);
            return true;
        }

        int cd = Math.Clamp(settings.CooldownSeconds, 1, 3600);
        if (!_categoryLastReplyUtcByUser.TryGetValue(category, out var map))
        {
            map = new Dictionary<string, DateTime>(StringComparer.Ordinal);
            _categoryLastReplyUtcByUser[category] = map;
        }
        if (map.TryGetValue(userId, out var last) &&
            (DateTime.UtcNow - last).TotalSeconds < cd)
            return false;
        map[userId] = DateTime.UtcNow;
        return true;
    }

    private void MigrateCategorySettingsKeys(string oldName, string newName)
    {
        if (string.Equals(oldName, newName, StringComparison.OrdinalIgnoreCase)) return;
        if (_categorySettings.TryGetValue(oldName, out var s))
        {
            _categorySettings.Remove(oldName);
            _categorySettings[newName] = s;
        }
        if (_categoryRepliedOnceByUser.TryGetValue(oldName, out var once))
        {
            _categoryRepliedOnceByUser.Remove(oldName);
            _categoryRepliedOnceByUser[newName] = once;
        }
        if (_categoryLastReplyUtcByUser.TryGetValue(oldName, out var last))
        {
            _categoryLastReplyUtcByUser.Remove(oldName);
            _categoryLastReplyUtcByUser[newName] = last;
        }
        var bagKeys = _unusedResponseIndexes.Keys
            .Where(k => string.Equals(k.category, oldName, StringComparison.OrdinalIgnoreCase))
            .ToList();
        foreach (var k in bagKeys)
        {
            var bag = _unusedResponseIndexes[k];
            _unusedResponseIndexes.Remove(k);
            _unusedResponseIndexes[(newName, k.trigger)] = bag;
        }
    }

    private void EnsureSettingsForAllCategories()
    {
        foreach (var key in _commandCategories.Keys)
            GetOrCreateCategorySettings(key);
        // Drop orphan settings for removed categories
        foreach (var orphan in _categorySettings.Keys
                     .Where(k => !_commandCategories.ContainsKey(k)).ToList())
            _categorySettings.Remove(orphan);
    }

    private void LoadCategorySettingsFromUi()
    {
        if (string.IsNullOrEmpty(_currentCommandCategory)) return;
        var s = GetOrCreateCategorySettings(_currentCommandCategory);
        if (CategoryAllowRepeatCheck != null)
            s.AllowRepeatTriggers = CategoryAllowRepeatCheck.IsChecked == true;
        if (CategoryCooldownBox != null && int.TryParse(CategoryCooldownBox.Text?.Trim(), out int sec))
            s.CooldownSeconds = Math.Clamp(sec, 1, 3600);
        if (CategoryUseNamePrefixCheck != null)
            s.UseNamePrefix = CategoryUseNamePrefixCheck.IsChecked == true;
    }

    private void ApplyCategorySettingsToUi(string category)
    {
        var s = GetOrCreateCategorySettings(category);
        _categorySettingsUiSyncing = true;
        try
        {
            if (CategoryAllowRepeatCheck != null)
                CategoryAllowRepeatCheck.IsChecked = s.AllowRepeatTriggers;
            if (CategoryCooldownBox != null)
            {
                CategoryCooldownBox.Text = s.AllowRepeatTriggers ? s.CooldownSeconds.ToString() : "";
                CategoryCooldownBox.IsEnabled = s.AllowRepeatTriggers;
            }
            if (CategoryCooldownPlaceholder != null)
            {
                CategoryCooldownPlaceholder.Visibility =
                    string.IsNullOrEmpty(CategoryCooldownBox?.Text) ? Visibility.Visible : Visibility.Collapsed;
            }
            if (CategoryUseNamePrefixCheck != null)
                CategoryUseNamePrefixCheck.IsChecked = s.UseNamePrefix;
            if (CategoryColorSwatch != null)
            {
                CategoryColorSwatch.Background = BrushFromHex(s.ColorHex);
                CategoryColorSwatch.ToolTip = s.ColorHex;
            }
        }
        finally { _categorySettingsUiSyncing = false; }
    }

    private bool _categorySettingsUiSyncing;

    private void CategorySettings_Changed(object sender, RoutedEventArgs e)
    {
        if (_categorySettingsUiSyncing || !_commandsReady) return;
        if (CategoryCooldownBox != null && CategoryAllowRepeatCheck != null)
            CategoryCooldownBox.IsEnabled = CategoryAllowRepeatCheck.IsChecked == true;
        // New category not saved yet — keep UI only until Save
        if (_isAddingNewCategory) return;
        LoadCategorySettingsFromUi();
        SaveCommands();
        RefreshCommandsPagedView();
    }

    private void CategoryColorSwatch_Click(object sender, RoutedEventArgs e)
    {
        if (!_commandsReady) return;
        e.Handled = true;

        if (_isAddingNewCategory)
        {
            int idx = Array.FindIndex(CategoryColorPalette,
                c => string.Equals(c, _pendingNewCategoryColor, StringComparison.OrdinalIgnoreCase));
            _pendingNewCategoryColor =
                CategoryColorPalette[(idx + 1 + CategoryColorPalette.Length) % CategoryColorPalette.Length];
            if (CategoryColorSwatch != null)
            {
                CategoryColorSwatch.Background = BrushFromHex(_pendingNewCategoryColor);
                CategoryColorSwatch.ToolTip = _pendingNewCategoryColor;
            }
            return;
        }

        if (string.IsNullOrEmpty(_currentCommandCategory)) return;
        var s = GetOrCreateCategorySettings(_currentCommandCategory);
        int i = Array.FindIndex(CategoryColorPalette,
            c => string.Equals(c, s.ColorHex, StringComparison.OrdinalIgnoreCase));
        s.ColorHex = CategoryColorPalette[(i + 1 + CategoryColorPalette.Length) % CategoryColorPalette.Length];
        if (CategoryColorSwatch != null)
        {
            CategoryColorSwatch.Background = BrushFromHex(s.ColorHex);
            CategoryColorSwatch.ToolTip = s.ColorHex;
        }
        SaveCommands();
        RefreshCommandsPagedView();
    }

    // —— Import / Export ——

    private sealed class CommandsExportDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; } = CommandsSchemaVersion;
        [JsonPropertyName("exportedAt")]
        public string ExportedAt { get; set; } = "";
        [JsonPropertyName("categories")]
        public Dictionary<string, CategoryExportDto> Categories { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CategoryExportDto
    {
        [JsonPropertyName("settings")]
        public CategorySettingsExportDto Settings { get; set; } = new();
        [JsonPropertyName("languages")]
        public Dictionary<string, List<CommandEntryExportDto>> Languages { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class CategorySettingsExportDto
    {
        [JsonPropertyName("allowRepeatTriggers")]
        public bool AllowRepeatTriggers { get; set; }
        [JsonPropertyName("cooldownSeconds")]
        public int CooldownSeconds { get; set; } = 30;
        [JsonPropertyName("useNamePrefix")]
        public bool UseNamePrefix { get; set; }
        /// <summary>Legacy export field; inverted into UseNamePrefix when loading old files.</summary>
        [JsonPropertyName("suppressNamePrefix")]
        public bool? SuppressNamePrefix { get; set; }
        [JsonPropertyName("colorHex")]
        public string ColorHex { get; set; } = "#7DD3FC";
    }

    private sealed class CommandEntryExportDto
    {
        [JsonPropertyName("command")]
        public string Command { get; set; } = "";
        [JsonPropertyName("response")]
        public string Response { get; set; } = "";
    }

    private void ExportCommandsOpen_Click(object sender, RoutedEventArgs e)
    {
        PopulateExportCategoryChecks();
        if (ExportCommandsModal != null)
            ExportCommandsModal.Visibility = Visibility.Visible;
    }

    private void ExportCommandsClose_Click(object sender, RoutedEventArgs e)
    {
        if (ExportCommandsModal != null)
            ExportCommandsModal.Visibility = Visibility.Collapsed;
    }

    private void PopulateExportCategoryChecks()
    {
        if (ExportCategoryList == null) return;
        ExportCategoryList.Items.Clear();
        foreach (var key in _commandCategories.Keys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            ExportCategoryList.Items.Add(new CheckBox
            {
                Content = key,
                IsChecked = true,
                Foreground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE0, 0xE0, 0xFF)),
                Margin = new Thickness(0, 2, 0, 2),
                Style = TryFindResource("CompanionCheckBox") as Style
            });
        }
    }

    private void ExportSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (ExportCategoryList == null) return;
        foreach (var item in ExportCategoryList.Items)
            if (item is CheckBox cb) cb.IsChecked = true;
    }

    private void ExportSelectNone_Click(object sender, RoutedEventArgs e)
    {
        if (ExportCategoryList == null) return;
        foreach (var item in ExportCategoryList.Items)
            if (item is CheckBox cb) cb.IsChecked = false;
    }

    private void ExportCommandsDo_Click(object sender, RoutedEventArgs e)
    {
        var selected = new List<string>();
        if (ExportCategoryList != null)
        {
            foreach (var item in ExportCategoryList.Items)
            {
                if (item is CheckBox { IsChecked: true, Content: string name } && !string.IsNullOrWhiteSpace(name))
                    selected.Add(name);
            }
        }
        if (selected.Count == 0)
        {
            ShowBotSettingsError("Export", "Select at least one category to export.");
            return;
        }

        string label = selected.Count == _commandCategories.Count ? "all" :
            string.Join("-", selected.Take(3)).Replace(" ", "");
        var dlg = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"imvucompanion-commands-{label}-{DateTime.Now:yyyyMMdd}.json",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            var dto = BuildExportDto(selected);
            string json = JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(dlg.FileName, json);
            if (ExportCommandsModal != null)
                ExportCommandsModal.Visibility = Visibility.Collapsed;
            AppendLog($"Exported {selected.Count} categor(ies) to {dlg.FileName}", LogCategory.Info);
        }
        catch (Exception ex)
        {
            ShowBotSettingsError("Export failed", ex.Message);
        }
    }

    private CommandsExportDto BuildExportDto(IEnumerable<string> categoryNames)
    {
        var dto = new CommandsExportDto
        {
            SchemaVersion = CommandsSchemaVersion,
            ExportedAt = DateTime.UtcNow.ToString("o")
        };
        foreach (var name in categoryNames)
        {
            if (!_commandCategories.TryGetValue(name, out var langs)) continue;
            var s = GetOrCreateCategorySettings(name);
            var cat = new CategoryExportDto
            {
                Settings = new CategorySettingsExportDto
                {
                    AllowRepeatTriggers = s.AllowRepeatTriggers,
                    CooldownSeconds = s.CooldownSeconds,
                    UseNamePrefix = s.UseNamePrefix,
                    ColorHex = s.ColorHex
                }
            };
            foreach (var langKv in langs)
            {
                if (langKv.Value == null || langKv.Value.Count == 0) continue;
                cat.Languages[langKv.Key] = langKv.Value.Select(e => new CommandEntryExportDto
                {
                    Command = e.Command,
                    Response = e.Response
                }).ToList();
            }
            dto.Categories[name] = cat;
        }
        return dto;
    }

    private void ImportCommandsOpen_Click(object sender, RoutedEventArgs e)
    {
        if (ImportFilePathText != null) ImportFilePathText.Text = "";
        if (ImportMergeReplaceCheck != null) ImportMergeReplaceCheck.IsChecked = true; // true = merge
        if (ImportCommandsModal != null)
            ImportCommandsModal.Visibility = Visibility.Visible;
    }

    private void ImportCommandsClose_Click(object sender, RoutedEventArgs e)
    {
        if (ImportCommandsModal != null)
            ImportCommandsModal.Visibility = Visibility.Collapsed;
    }

    private void ImportBrowse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true && ImportFilePathText != null)
            ImportFilePathText.Text = dlg.FileName;
    }

    private void ImportCommandsDo_Click(object sender, RoutedEventArgs e)
    {
        string path = ImportFilePathText?.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            ShowBotSettingsError("Import", "Choose a valid JSON file to import.");
            return;
        }

        CommandsExportDto? dto;
        try
        {
            dto = JsonSerializer.Deserialize<CommandsExportDto>(File.ReadAllText(path));
        }
        catch (Exception ex)
        {
            ShowBotSettingsError("Import failed", "Could not read file: " + ex.Message);
            return;
        }

        if (dto == null || dto.Categories == null || dto.Categories.Count == 0)
        {
            ShowBotSettingsError("Import failed", "File has no categories.");
            return;
        }
        if (dto.SchemaVersion <= 0 || dto.SchemaVersion > CommandsSchemaVersion)
        {
            ShowBotSettingsError(
                "Import blocked",
                $"Unsupported schemaVersion {dto.SchemaVersion}. This app supports up to {CommandsSchemaVersion}.");
            return;
        }

        bool merge = ImportMergeReplaceCheck?.IsChecked != false;

        // Cross-category trigger check against final state
        string? conflict = FindImportTriggerConflict(dto, merge);
        if (conflict != null)
        {
            ShowBotSettingsError("Import blocked", conflict);
            return;
        }

        try
        {
            if (!merge)
            {
                _commandCategories.Clear();
                _categorySettings.Clear();
            }

            foreach (var catKv in dto.Categories)
            {
                ApplyImportedCategory(catKv.Key, catKv.Value);
            }

            EnsureSettingsForAllCategories();
            if (string.IsNullOrEmpty(_currentCommandCategory) || !_commandCategories.ContainsKey(_currentCommandCategory))
                _currentCommandCategory = _commandCategories.Keys.FirstOrDefault() ?? "General";
            _activeCommandCategory = _currentCommandCategory;
            PopulateCategoryCombo();
            PopulateCategoryFilterCombo();
            EnsureCategoryComboSelected();
            RefreshCommandsList();
            SaveCommands();
            if (ImportCommandsModal != null)
                ImportCommandsModal.Visibility = Visibility.Collapsed;
            AppendLog($"Imported {dto.Categories.Count} categor(ies) ({(merge ? "merge" : "replace")}).", LogCategory.Info);
        }
        catch (Exception ex)
        {
            ShowBotSettingsError("Import failed", ex.Message);
        }
    }

    private string? FindImportTriggerConflict(CommandsExportDto dto, bool merge)
    {
        // Build map: trigger -> category for current language after import intent
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        void add(string cat, string lang, string cmd)
        {
            string n = NormalizeCommand(cmd);
            if (string.IsNullOrEmpty(n)) return;
            // Only check current language for runtime conflict; still scan all langs in file vs each other
            if (map.TryGetValue(lang + "\0" + n, out var existing) &&
                !string.Equals(existing, cat, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Import has trigger '{n}' in both '{existing}' and '{cat}' (language '{lang}'). " +
                    "The same trigger cannot exist in more than one category for a language.");
            map[lang + "\0" + n] = cat;
        }

        try
        {
            foreach (var catKv in dto.Categories)
            {
                if (catKv.Value?.Languages == null) continue;
                foreach (var langKv in catKv.Value.Languages)
                {
                    if (langKv.Value == null) continue;
                    foreach (var e in langKv.Value)
                        add(catKv.Key, langKv.Key, e.Command);
                }
            }

            if (merge)
            {
                foreach (var catKv in _commandCategories)
                {
                    // Skip categories that will be fully replaced by import of same name
                    if (dto.Categories.ContainsKey(catKv.Key)) continue;
                    foreach (var langKv in catKv.Value)
                    {
                        if (langKv.Value == null) continue;
                        foreach (var e in langKv.Value)
                        {
                            string n = NormalizeCommand(e.Command);
                            if (string.IsNullOrEmpty(n)) continue;
                            string key = langKv.Key + "\0" + n;
                            if (map.TryGetValue(key, out var importCat) &&
                                !string.Equals(importCat, catKv.Key, StringComparison.OrdinalIgnoreCase))
                            {
                                return
                                    $"Import would place trigger '{n}' in category '{importCat}', " +
                                    $"but it already exists in category '{catKv.Key}' (language '{langKv.Key}'). " +
                                    "Remove or rename one side, or use Replace All.";
                            }
                        }
                    }
                }
            }
        }
        catch (InvalidOperationException ex)
        {
            return ex.Message;
        }

        return null;
    }

    private void ApplyImportedCategory(string name, CategoryExportDto cat)
    {
        var langs = new Dictionary<string, List<CommandEntry>>(StringComparer.OrdinalIgnoreCase);
        if (cat.Languages != null)
        {
            foreach (var langKv in cat.Languages)
            {
                if (langKv.Value == null) continue;
                langs[langKv.Key] = langKv.Value.Select(e => new CommandEntry
                {
                    Command = e.Command ?? "",
                    Response = e.Response ?? ""
                }).ToList();
            }
        }
        _commandCategories[name] = langs;
        var st = cat.Settings ?? new CategorySettingsExportDto();
        _categorySettings[name] = new CategorySettings
        {
            AllowRepeatTriggers = st.AllowRepeatTriggers,
            CooldownSeconds = Math.Clamp(st.CooldownSeconds <= 0 ? 30 : st.CooldownSeconds, 1, 3600),
            UseNamePrefix = st.SuppressNamePrefix.HasValue
                ? !st.SuppressNamePrefix.Value
                : st.UseNamePrefix,
            ColorHex = string.IsNullOrWhiteSpace(st.ColorHex) ? NextCategoryColor() : st.ColorHex
        };
    }

    private void CategoryCooldownBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (CategoryCooldownPlaceholder == null || CategoryCooldownBox == null) return;
        CategoryCooldownPlaceholder.Visibility = string.IsNullOrEmpty(CategoryCooldownBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void ImportModal_DragOver(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            e.Effects = DragDropEffects.Copy;
            e.Handled = true;
        }
    }

    private void ImportModal_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] files || files.Length == 0) return;
        string path = files[0];
        if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            ShowBotSettingsError("Import", "Drop a .json Bot Settings export file.");
            return;
        }
        if (ImportFilePathText != null)
            ImportFilePathText.Text = path;
    }
}
