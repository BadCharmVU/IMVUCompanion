using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace IMVUCompanion;

public partial class MainWindow
{
    public const string ManagePresetsTag = "__manage__";

    private readonly List<AppDatabase.PresetInfo> _presets = new();
    private bool _presetsReady;
    private bool _presetComboQuiet;
    private List<PresetBundleDto>? _pendingPresetImport;
    private readonly List<PresetLineVm> _presetLines = new();

    private sealed class PresetLineVm : INotifyPropertyChanged
    {
        private bool _isChecked;
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Number { get; set; } = "";
        public int Index { get; set; }
        public bool CanMoveUp { get; set; }
        public bool CanMoveDown { get; set; }
        public bool IsChecked
        {
            get => _isChecked;
            set
            {
                if (_isChecked == value) return;
                _isChecked = value;
                OnPropertyChanged();
            }
        }
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    private sealed class PresetFileDto
    {
        public string Kind { get; set; } = "IMVUCompanion.Presets";
        public int Version { get; set; } = 1;
        public List<PresetBundleDto> Presets { get; set; } = new();
    }

    private sealed class PresetBundleDto
    {
        public string Name { get; set; } = "";
        public bool Welcome1Enabled { get; set; } = true;
        public bool Welcome1Whisper { get; set; }
        public bool Welcome2Enabled { get; set; }
        public bool Welcome2Whisper { get; set; } = true;
        public List<string> Welcome1 { get; set; } = new();
        public List<string> Welcome2 { get; set; } = new();
        public bool ConsoleWhisper { get; set; }
        public bool ConsolePrefix { get; set; } = true;
        public List<string> ConsoleMessages { get; set; } = new();
        public string RecorderTrigger { get; set; } = "RMsg";
        public bool ConfirmReceipt { get; set; } = true;
        public List<string> Answering { get; set; } = new();
        public string ActiveCategory { get; set; } = "General";
        public Dictionary<string, PresetCategoryDto> Categories { get; set; } =
            new(StringComparer.OrdinalIgnoreCase);
    }

    private sealed class PresetCategoryDto
    {
        public string ColorHex { get; set; } = "#7DD3FC";
        public bool AllowRepeatTriggers { get; set; }
        public int CooldownSeconds { get; set; } = 30;
        public bool UseNamePrefix { get; set; }
        public List<AppDatabase.TriggerEntryData> Entries { get; set; } = new();
    }

    private void LoadPresetRegistry()
    {
        _presets.Clear();
        try
        {
            _presets.AddRange(AppDatabase.LoadPresets());
        }
        catch { }
        if (_presets.Count == 0)
        {
            _presets.Add(new AppDatabase.PresetInfo { Id = "en", Name = "English", SortOrder = 0 });
            _presets.Add(new AppDatabase.PresetInfo { Id = "ru", Name = "Русский", SortOrder = 1 });
        }
        _presetsReady = true;
        PopulatePresetCombo();
    }

    private IEnumerable<string> AppLanguageCodes()
    {
        if (_presets.Count == 0)
        {
            yield return "en";
            yield return "ru";
            yield break;
        }
        foreach (var p in _presets)
            yield return p.Id;
    }

    private void PopulatePresetCombo()
    {
        if (AppLanguageCombo == null) return;
        string keep = _currentLanguage;
        _presetComboQuiet = true;
        _appLanguageSyncing = true;
        try
        {
            AppLanguageCombo.Items.Clear();
            foreach (var p in _presets)
                AppLanguageCombo.Items.Add(new ComboBoxItem { Content = p.Name, Tag = p.Id });
            AppLanguageCombo.Items.Add(new ComboBoxItem { Content = "Manage Presets", Tag = ManagePresetsTag });
            SelectAppLanguageCombo(keep);
        }
        finally
        {
            _appLanguageSyncing = false;
            _presetComboQuiet = false;
        }
    }

    private void SaveActivePresetPack()
    {
        if (!_presetsReady || string.IsNullOrWhiteSpace(_currentLanguage)) return;
        try
        {
            AppDatabase.SavePresetPack(_currentLanguage, CaptureActivePresetPack());
        }
        catch { }
    }

    private AppDatabase.PresetPack CaptureActivePresetPack() => new()
    {
        Welcome1Enabled = _welcome1Enabled,
        Welcome1Whisper = _welcome1.AsWhisper,
        Welcome2Enabled = _welcome2Enabled,
        Welcome2Whisper = _welcome2.AsWhisper,
        ConsoleWhisper = _dmAsWhisper,
        ConsolePrefix = _chipMessagePrefix,
        RecorderTrigger = string.IsNullOrWhiteSpace(_recorderTrigger) ? "RMsg" : _recorderTrigger,
        ConfirmReceipt = _confirmReceipt
    };

    private void LoadActivePresetPack()
    {
        if (!_presetsReady || string.IsNullOrWhiteSpace(_currentLanguage)) return;
        var pack = AppDatabase.LoadPresetPack(_currentLanguage);
        _welcomeUiSyncing = true;
        _dmUiSyncing = true;
        try
        {
            _welcome1Enabled = pack.Welcome1Enabled;
            _welcome1.AsWhisper = pack.Welcome1Whisper;
            _welcome2Enabled = pack.Welcome2Enabled;
            _welcome2.AsWhisper = pack.Welcome2Whisper;
            _dmAsWhisper = pack.ConsoleWhisper;
            _chipMessagePrefix = pack.ConsolePrefix;
            string t = NormalizeRecorderTrigger(pack.RecorderTrigger);
            if (!string.IsNullOrEmpty(t))
                _recorderTrigger = t;
            _confirmReceipt = pack.ConfirmReceipt;
            if (RecorderTriggerBox != null)
                RecorderTriggerBox.Text = _recorderTrigger;
            if (RecorderConfirmReceiptCheck != null)
                RecorderConfirmReceiptCheck.IsChecked = _confirmReceipt;
        }
        finally
        {
            _welcomeUiSyncing = false;
            _dmUiSyncing = false;
        }
        RefreshWelcomeUi();
        UpdateReceiptEditorVisibility();
        RefreshReceiptList();
    }

    /// <summary>
    /// Rebind every settings surface to the current preset without first saving
    /// stale UI back over imported data.
    /// </summary>
    private void ReloadActivePresetUi()
    {
        string lang = _currentLanguage;
        if (string.IsNullOrEmpty(lang) || lang == ManagePresetsTag) return;

        SeedWelcomeLanguageIfEmpty(lang);
        SeedTriggerLanguageIfEmpty(lang);
        BindCategorySettingsToLanguage(lang);

        if (_commandsReady)
        {
            if (!_activeCategoryByLang.TryGetValue(lang, out var cat) ||
                string.IsNullOrEmpty(cat) ||
                !CategoryExistsForCurrentLanguage(cat))
                cat = CategoriesForLanguage(lang).FirstOrDefault() ?? "General";
            _currentCommandCategory = cat;
            _activeCommandCategory = cat;
            _activeCategoryByLang[lang] = cat;
            _commandFilterCategory = null;
            _commandsPageIndex = 0;
            PopulateCategoryCombo();
            PopulateCategoryFilterCombo(selectAll: true);
            EnsureCategoryComboSelected();
            RefreshCommandsList();
        }

        if (_dmReady)
        {
            SeedConsoleLanguageIfEmpty(lang);
            ApplyConsoleLanguage(lang);
            UpdateDmSendButton();
        }

        LoadActivePresetPack();
        if (_recorderReady)
            ApplyReceiptLanguage(lang);
    }

    private void OpenPresetManager()
    {
        SaveActivePresetPack();
        if (_messagesReady) SaveMessages();
        if (_commandsReady) SaveCommands();
        if (_dmReady) SaveDmMessages();
        if (_recorderReady) SaveRecorderSettings();
        RefreshPresetManagerList();
        if (PresetNameEditBox != null) PresetNameEditBox.Text = "";
        UpdatePresetNamePlaceholder();
        if (PresetManagerModal != null)
            PresetManagerModal.Visibility = Visibility.Visible;
    }

    private void PresetManagerDone_Click(object sender, RoutedEventArgs e)
    {
        if (PresetManagerModal != null)
            PresetManagerModal.Visibility = Visibility.Collapsed;
        PopulatePresetCombo();
    }

    private void RefreshPresetManagerList()
    {
        var checkedIds = new HashSet<string>(_presetLines.Where(p => p.IsChecked).Select(p => p.Id),
            StringComparer.OrdinalIgnoreCase);
        string? selectedId = (PresetManagerList?.SelectedItem as PresetLineVm)?.Id;
        _presetLines.Clear();
        int last = _presets.Count - 1;
        for (int i = 0; i < _presets.Count; i++)
        {
            var p = _presets[i];
            _presetLines.Add(new PresetLineVm
            {
                Id = p.Id,
                Name = p.Name,
                Number = (i + 1) + ".",
                Index = i,
                CanMoveUp = i > 0,
                CanMoveDown = i < last,
                IsChecked = checkedIds.Contains(p.Id)
            });
        }
        if (PresetManagerList != null)
        {
            SizeGrowingList(PresetManagerList, _presetLines.Count);
            PresetManagerList.ItemsSource = null;
            PresetManagerList.ItemsSource = _presetLines;
            if (!string.IsNullOrEmpty(selectedId))
            {
                var keep = _presetLines.FirstOrDefault(x => string.Equals(x.Id, selectedId, StringComparison.OrdinalIgnoreCase));
                if (keep != null)
                    PresetManagerList.SelectedItem = keep;
            }
            ResetGrowingListScroll(PresetManagerList, _presetLines.Count);
        }
        UpdatePresetExportEnabled();
        UpdatePresetNamePlaceholder();
    }

    private void UpdatePresetExportEnabled()
    {
        bool any = _presetLines.Any(p => p.IsChecked);
        if (PresetExportBtn == null) return;
        PresetExportBtn.IsEnabled = any;
        PresetExportBtn.Opacity = any ? 1.0 : 0.45;
    }

    private void UpdatePresetNamePlaceholder()
    {
        if (PresetNamePlaceholder != null)
            PresetNamePlaceholder.Visibility = string.IsNullOrEmpty(PresetNameEditBox?.Text)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void PresetLineCheck_Changed(object sender, RoutedEventArgs e) =>
        UpdatePresetExportEnabled();

    private void PresetManagerList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PresetManagerList?.SelectedItem is PresetLineVm row && PresetNameEditBox != null)
            PresetNameEditBox.Text = row.Name;
        UpdatePresetNamePlaceholder();
    }

    private void PresetMoveUp_Preview(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: PresetLineVm row })
            MovePreset(row.Index, -1);
    }

    private void PresetMoveDown_Preview(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        if (sender is FrameworkElement { Tag: PresetLineVm row })
            MovePreset(row.Index, 1);
    }

    private void MovePreset(int index, int delta)
    {
        int dest = index + delta;
        if (index < 0 || dest < 0 || index >= _presets.Count || dest >= _presets.Count)
            return;
        (_presets[index], _presets[dest]) = (_presets[dest], _presets[index]);
        PersistPresetOrder();
        RefreshPresetManagerList();
        PopulatePresetCombo();
    }

    private void PersistPresetOrder()
    {
        for (int i = 0; i < _presets.Count; i++)
            _presets[i].SortOrder = i;
        AppDatabase.SavePresets(_presets);
    }

    private void PresetAdd_Click(object sender, RoutedEventArgs e)
    {
        string name = (PresetNameEditBox?.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowPresetError("Missing name", "Enter a name for the new preset.", overwrite: false);
            return;
        }
        if (_presets.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowPresetError("Preset exists", $"A preset named '{name}' already exists.", overwrite: false);
            return;
        }
        string id = "p" + Guid.NewGuid().ToString("N")[..10];
        _presets.Add(new AppDatabase.PresetInfo { Id = id, Name = name, SortOrder = _presets.Count });
        PersistPresetOrder();
        AppDatabase.SavePresetPack(id, new AppDatabase.PresetPack());
        SeedWelcomeLanguageIfEmpty(id);
        SeedTriggerLanguageIfEmpty(id);
        SeedConsoleLanguageIfEmpty(id);
        SeedAnsweringLanguageIfEmpty(id);
        if (_messagesReady) SaveMessages();
        if (_commandsReady) SaveCommands();
        if (_dmReady) SaveDmMessages();
        if (_recorderReady) SaveRecorderSettings();
        if (PresetNameEditBox != null) PresetNameEditBox.Text = "";
        RefreshPresetManagerList();
        PopulatePresetCombo();
        AppendLog("Preset added: " + name, LogCategory.Info);
    }

    private void PresetUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (PresetManagerList?.SelectedItem is not PresetLineVm row) return;
        string name = (PresetNameEditBox?.Text ?? "").Trim();
        if (string.IsNullOrEmpty(name))
        {
            ShowPresetError("Missing name", "Enter a preset name.", overwrite: false);
            return;
        }
        if (_presets.Any(p =>
            !string.Equals(p.Id, row.Id, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            ShowPresetError("Preset exists", $"A preset named '{name}' already exists.", overwrite: false);
            return;
        }
        var info = _presets.FirstOrDefault(p => string.Equals(p.Id, row.Id, StringComparison.OrdinalIgnoreCase));
        if (info == null) return;
        info.Name = name;
        PersistPresetOrder();
        RefreshPresetManagerList();
        PopulatePresetCombo();
        AppendLog("Preset renamed: " + name, LogCategory.Info);
    }

    private void PresetDelete_Click(object sender, RoutedEventArgs e)
    {
        if (PresetManagerList?.SelectedItem is not PresetLineVm row) return;
        if (_presets.Count <= 1)
        {
            ShowPresetError("Cannot delete", "At least one preset must remain.", overwrite: false);
            return;
        }
        string id = row.Id;
        if (string.Equals(id, _currentLanguage, StringComparison.OrdinalIgnoreCase))
        {
            var next = _presets.First(p => !string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
            SetAppLanguage(next.Id, refreshUi: true);
        }
        _presets.RemoveAll(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));
        AppDatabase.DeletePresetKeyedData(id);
        PersistPresetOrder();
        DropPresetFromMemory(id);
        if (PresetNameEditBox != null) PresetNameEditBox.Text = "";
        RefreshPresetManagerList();
        PopulatePresetCombo();
        AppendLog("Preset deleted: " + row.Name, LogCategory.Info);
    }

    private void DropPresetFromMemory(string id)
    {
        _welcome1.Messages.Remove(id);
        _welcome2.Messages.Remove(id);
        _consoleByLang.Remove(id);
        _receiptByLang.Remove(id);
        _activeCategoryByLang.Remove(id);
        _settingsByLang.Remove(id);
        foreach (var cat in _commandCategories.Values)
            cat?.Remove(id);
    }

    private void PresetExport_Click(object sender, RoutedEventArgs e)
    {
        var selected = _presetLines.Where(p => p.IsChecked).ToList();
        if (selected.Count == 0) return;
        SaveActivePresetPack();
        if (_messagesReady) SaveMessages();
        if (_commandsReady) SaveCommands();
        if (_dmReady) SaveDmMessages();
        if (_recorderReady) SaveRecorderSettings();
        var dlg = new SaveFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            FileName = $"imvucompanion-presets-{DateTime.Now:yyyyMMdd}.json",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            var file = new PresetFileDto();
            foreach (var row in selected)
            {
                var bundle = BuildPresetBundle(row.Id, row.Name);
                if (bundle != null) file.Presets.Add(bundle);
            }
            File.WriteAllText(dlg.FileName, JsonSerializer.Serialize(file, new JsonSerializerOptions { WriteIndented = true }));
            AppendLog($"Exported {file.Presets.Count} preset(s)", LogCategory.Info);
        }
        catch (Exception ex)
        {
            ShowPresetError("Export failed", ex.Message, overwrite: false);
        }
    }

    private void PresetImport_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Filter = "JSON files (*.json)|*.json|All files (*.*)|*.*",
            DefaultExt = ".json"
        };
        if (dlg.ShowDialog() != true) return;
        try
        {
            string json = File.ReadAllText(dlg.FileName);
            var file = JsonSerializer.Deserialize<PresetFileDto>(json);
            if (file?.Presets == null || file.Presets.Count == 0)
            {
                ShowPresetError("Import failed", "File has no presets.", overwrite: false);
                return;
            }
            var names = file.Presets.Select(p => (p.Name ?? "").Trim()).Where(n => n.Length > 0).ToList();
            var conflicts = names.Where(n => _presets.Any(p => string.Equals(p.Name, n, StringComparison.OrdinalIgnoreCase))).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (conflicts.Count > 0)
            {
                _pendingPresetImport = file.Presets;
                ShowPresetError(
                    "Preset exists",
                    $"A preset named '{conflicts[0]}' already exists.\nExisting data for that preset will be overwritten.",
                    overwrite: true);
                return;
            }
            ApplyPresetImport(file.Presets, overwrite: false);
        }
        catch (Exception ex)
        {
            ShowPresetError("Import failed", ex.Message, overwrite: false);
        }
    }

    private void ApplyPresetImport(List<PresetBundleDto> bundles, bool overwrite)
    {
        foreach (var bundle in bundles)
        {
            string name = (bundle.Name ?? "").Trim();
            if (string.IsNullOrEmpty(name)) continue;
            var existing = _presets.FirstOrDefault(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                if (!overwrite) continue;
                WritePresetBundle(existing.Id, bundle);
                continue;
            }
            string id = "p" + Guid.NewGuid().ToString("N")[..10];
            _presets.Add(new AppDatabase.PresetInfo { Id = id, Name = name, SortOrder = _presets.Count });
            WritePresetBundle(id, bundle);
        }
        PersistPresetOrder();
        ReloadActivePresetUi();
        if (_messagesReady) SaveMessages();
        if (_commandsReady) SaveCommands();
        if (_dmReady) SaveDmMessages();
        if (_recorderReady) SaveRecorderSettings();
        RefreshPresetManagerList();
        PopulatePresetCombo();
        AppendLog($"Imported {bundles.Count} preset(s)", LogCategory.Info);
        int n = bundles.Count;
        ShowPresetError(
            "Import complete",
            n == 1
                ? "The preset was imported and the active preset has been refreshed."
                : $"{n} presets were imported and the active preset has been refreshed.",
            overwrite: false,
            success: true);
    }

    private PresetBundleDto? BuildPresetBundle(string id, string name)
    {
        var pack = AppDatabase.LoadPresetPack(id);
        var bundle = new PresetBundleDto
        {
            Name = name,
            Welcome1Enabled = pack.Welcome1Enabled,
            Welcome1Whisper = pack.Welcome1Whisper,
            Welcome2Enabled = pack.Welcome2Enabled,
            Welcome2Whisper = pack.Welcome2Whisper,
            Welcome1 = _welcome1.Messages.TryGetValue(id, out var w1) ? w1.ToList() : new List<string>(),
            Welcome2 = _welcome2.Messages.TryGetValue(id, out var w2) ? w2.ToList() : new List<string>(),
            ConsoleWhisper = pack.ConsoleWhisper,
            ConsolePrefix = pack.ConsolePrefix,
            ConsoleMessages = _consoleByLang.TryGetValue(id, out var cm) ? cm.ToList() : new List<string>(),
            RecorderTrigger = pack.RecorderTrigger,
            ConfirmReceipt = pack.ConfirmReceipt,
            Answering = _receiptByLang.TryGetValue(id, out var an) ? an.ToList() : new List<string>(),
            ActiveCategory = _activeCategoryByLang.TryGetValue(id, out var ac) ? ac : "General"
        };
        foreach (var catKv in _commandCategories)
        {
            if (catKv.Value == null || !catKv.Value.TryGetValue(id, out var entries) || entries == null)
                continue;
            _settingsByLang.TryGetValue(id, out var bag);
            CategorySettings? st = null;
            bag?.TryGetValue(catKv.Key, out st);
            bundle.Categories[catKv.Key] = new PresetCategoryDto
            {
                ColorHex = st?.ColorHex ?? "#7DD3FC",
                AllowRepeatTriggers = st?.AllowRepeatTriggers ?? false,
                CooldownSeconds = st?.CooldownSeconds ?? 30,
                UseNamePrefix = st?.UseNamePrefix ?? false,
                Entries = entries.Select(e => new AppDatabase.TriggerEntryData { Command = e.Command, Response = e.Response }).ToList()
            };
        }
        return bundle;
    }

    private void WritePresetBundle(string id, PresetBundleDto bundle)
    {
        AppDatabase.SavePresetPack(id, new AppDatabase.PresetPack
        {
            Welcome1Enabled = bundle.Welcome1Enabled,
            Welcome1Whisper = bundle.Welcome1Whisper,
            Welcome2Enabled = bundle.Welcome2Enabled,
            Welcome2Whisper = bundle.Welcome2Whisper,
            ConsoleWhisper = bundle.ConsoleWhisper,
            ConsolePrefix = bundle.ConsolePrefix,
            RecorderTrigger = bundle.RecorderTrigger,
            ConfirmReceipt = bundle.ConfirmReceipt
        });
        _welcome1.Messages[id] = bundle.Welcome1 ?? new List<string>();
        _welcome2.Messages[id] = bundle.Welcome2 ?? new List<string>();
        _consoleByLang[id] = bundle.ConsoleMessages ?? new List<string>();
        PutAnsweringLang(id, bundle.Answering);
        if (!string.IsNullOrWhiteSpace(bundle.ActiveCategory))
            _activeCategoryByLang[id] = bundle.ActiveCategory;
        if (!_settingsByLang.TryGetValue(id, out var bag) || bag == null)
        {
            bag = new Dictionary<string, CategorySettings>(StringComparer.OrdinalIgnoreCase);
            _settingsByLang[id] = bag;
        }
        foreach (var kv in _commandCategories.ToList())
            kv.Value?.Remove(id);
        if (bundle.Categories != null)
        {
            foreach (var catKv in bundle.Categories)
            {
                if (!_commandCategories.TryGetValue(catKv.Key, out var byLang) || byLang == null)
                {
                    byLang = new Dictionary<string, List<CommandEntry>>(StringComparer.OrdinalIgnoreCase);
                    _commandCategories[catKv.Key] = byLang;
                }
                byLang[id] = (catKv.Value.Entries ?? new()).Select(e => new CommandEntry
                {
                    Command = e.Command,
                    Response = e.Response
                }).ToList();
                bag[catKv.Key] = new CategorySettings
                {
                    ColorHex = catKv.Value.ColorHex,
                    AllowRepeatTriggers = catKv.Value.AllowRepeatTriggers,
                    CooldownSeconds = catKv.Value.CooldownSeconds,
                    UseNamePrefix = catKv.Value.UseNamePrefix
                };
            }
        }
        SeedWelcomeLanguageIfEmpty(id);
        SeedTriggerLanguageIfEmpty(id);
        SeedConsoleLanguageIfEmpty(id);
        SeedAnsweringLanguageIfEmpty(id);
    }

    private void ShowPresetError(string title, string message, bool overwrite, bool success = false)
    {
        if (PresetErrorTitle != null)
        {
            PresetErrorTitle.Text = title;
            PresetErrorTitle.Foreground = success ? HeaderActiveGreen : OverlayErrorFg;
        }
        if (PresetErrorMessage != null) PresetErrorMessage.Text = message;
        if (PresetErrorOverwriteBtn != null)
            PresetErrorOverwriteBtn.Visibility = overwrite && !success ? Visibility.Visible : Visibility.Collapsed;
        if (PresetErrorOverlay != null)
            PresetErrorOverlay.Visibility = Visibility.Visible;
    }

    private void PresetErrorClose_Click(object sender, RoutedEventArgs e)
    {
        _pendingPresetImport = null;
        if (PresetErrorOverwriteBtn != null)
            PresetErrorOverwriteBtn.Visibility = Visibility.Collapsed;
        if (PresetErrorOverlay != null)
            PresetErrorOverlay.Visibility = Visibility.Collapsed;
    }

    private void PresetErrorOverwrite_Click(object sender, RoutedEventArgs e)
    {
        var pending = _pendingPresetImport;
        PresetErrorClose_Click(sender, e);
        if (pending != null)
            ApplyPresetImport(pending, overwrite: true);
    }
}
