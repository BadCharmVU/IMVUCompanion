using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace IMVUCompanion;

public partial class MainWindow
{
    private static readonly string AiSettingsFile = UserDataPaths.GetConfigFile("ai_settings.json");
    private const string AiCommandToken = "!bbot";
    private const string AiMaintenanceReply = "Sorry, my conversation module is under maintenance.";

    private static readonly string[] AiProviderNames =
    {
        "Grok", "Claude", "Gemini", "GPT", "DeepSeek", "Mistral", "Qwen", "Llama", "Fireworks"
    };

    private readonly Dictionary<string, FrameworkElement> _aiDynamicFields = new(StringComparer.OrdinalIgnoreCase);
    private AiSettings _aiSettings = new();
    private string _botDisplayName = "";
    private string? _aiFieldsBoundProvider;
    private bool _aiProviderUiSyncing;

    private sealed class ProviderConfig
    {
        public string ApiKey { get; set; } = "";
        public string Endpoint { get; set; } = "";
        public string Model { get; set; } = "";
        public double Temperature { get; set; } = 0.7;
        public int MaxTokens { get; set; } = 1024;
        public bool Enabled { get; set; }
    }

    private sealed class AiSettings
    {
        public string SelectedProvider { get; set; } = "Grok";
        public string BotDisplayName { get; set; } = "";
        public Dictionary<string, ProviderConfig> Providers { get; set; } =
            new Dictionary<string, ProviderConfig>(StringComparer.OrdinalIgnoreCase);
    }

    private static ProviderConfig DefaultProviderConfig(string name) => name switch
    {
        "Grok" => new ProviderConfig
        {
            Endpoint = "https://api.x.ai/v1",
            Model = "grok-2",
            Temperature = 0.7,
            MaxTokens = 1024
        },
        "Claude" => new ProviderConfig
        {
            Endpoint = "https://api.anthropic.com/v1",
            Model = "claude-sonnet-4-20250514",
            Temperature = 0.7,
            MaxTokens = 1024
        },
        "Gemini" => new ProviderConfig
        {
            Endpoint = "https://generativelanguage.googleapis.com/v1beta",
            Model = "gemini-2.0-flash",
            Temperature = 0.7,
            MaxTokens = 1024
        },
        "GPT" => new ProviderConfig
        {
            Endpoint = "https://api.openai.com/v1",
            Model = "gpt-4o",
            Temperature = 0.7,
            MaxTokens = 1024
        },
        "DeepSeek" => new ProviderConfig
        {
            Endpoint = "https://api.deepseek.com/v1",
            Model = "deepseek-chat",
            Temperature = 0.7,
            MaxTokens = 1024
        },
        "Mistral" => new ProviderConfig
        {
            Endpoint = "https://api.mistral.ai/v1",
            Model = "mistral-large-latest",
            Temperature = 0.7,
            MaxTokens = 1024
        },
        "Qwen" => new ProviderConfig
        {
            Endpoint = "https://dashscope.aliyuncs.com/compatible-mode/v1",
            Model = "qwen-plus",
            Temperature = 0.7,
            MaxTokens = 1024
        },
        "Llama" => new ProviderConfig
        {
            Endpoint = "https://api.together.xyz/v1",
            Model = "meta-llama/Llama-3.3-70B-Instruct-Turbo",
            Temperature = 0.7,
            MaxTokens = 1024
        },
        "Fireworks" => new ProviderConfig
        {
            Endpoint = "https://api.fireworks.ai/inference/v1",
            Model = "accounts/fireworks/models/llama-v3p1-70b-instruct",
            Temperature = 0.7,
            MaxTokens = 1024
        },
        _ => new ProviderConfig()
    };

    private void InitAiProvidersUi()
    {
        if (AiProviderCombo == null) return;

        AiProviderCombo.Items.Clear();
        foreach (string name in AiProviderNames)
            AiProviderCombo.Items.Add(new ComboBoxItem { Content = name, Tag = name });

        LoadAiSettings();

        _aiProviderUiSyncing = true;
        try
        {
            string selected = _aiSettings.SelectedProvider;
            for (int i = 0; i < AiProviderCombo.Items.Count; i++)
            {
                if (AiProviderCombo.Items[i] is ComboBoxItem cbi &&
                    string.Equals(cbi.Tag?.ToString(), selected, StringComparison.OrdinalIgnoreCase))
                {
                    AiProviderCombo.SelectedIndex = i;
                    break;
                }
            }
            if (AiProviderCombo.SelectedIndex < 0 && AiProviderCombo.Items.Count > 0)
                AiProviderCombo.SelectedIndex = 0;
        }
        finally
        {
            _aiProviderUiSyncing = false;
        }

        if (BotDisplayNameBox != null)
            BotDisplayNameBox.Text = _aiSettings.BotDisplayName;

        _botDisplayName = _aiSettings.BotDisplayName?.Trim() ?? "";
        _aiFieldsBoundProvider = GetSelectedAiProviderName();
        RebuildAiProviderFields();
    }

    private void LoadAiSettings()
    {
        _aiSettings = new AiSettings();
        try
        {
            if (File.Exists(AiSettingsFile))
            {
                string json = File.ReadAllText(AiSettingsFile);
                var loaded = JsonSerializer.Deserialize<AiSettings>(json);
                if (loaded != null) _aiSettings = loaded;
            }
        }
        catch (Exception ex) { AppendLog("Load AI settings err: " + ex.Message, LogCategory.Warning); }

        foreach (string name in AiProviderNames)
        {
            if (!_aiSettings.Providers.ContainsKey(name))
                _aiSettings.Providers[name] = DefaultProviderConfig(name);
        }

        if (string.IsNullOrWhiteSpace(_aiSettings.SelectedProvider) ||
            !AiProviderNames.Contains(_aiSettings.SelectedProvider, StringComparer.OrdinalIgnoreCase))
            _aiSettings.SelectedProvider = "Grok";
    }

    private void SaveAiSettings()
    {
        try
        {
            FlushAiFieldsToSettings(_aiFieldsBoundProvider);
            if (BotDisplayNameBox != null)
                _aiSettings.BotDisplayName = BotDisplayNameBox.Text.Trim();
            _botDisplayName = _aiSettings.BotDisplayName;

            Directory.CreateDirectory(UserDataPaths.Root);
            string json = JsonSerializer.Serialize(_aiSettings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(AiSettingsFile, json);
            AppendLog("AI settings saved to ai_settings.json.", LogCategory.Info);
        }
        catch (Exception ex) { AppendLog("Save AI settings err: " + ex.Message, LogCategory.Error); }
    }

    private string? GetSelectedAiProviderName()
    {
        if (AiProviderCombo?.SelectedItem is ComboBoxItem cbi)
            return cbi.Tag?.ToString() ?? cbi.Content?.ToString();
        return _aiSettings.SelectedProvider;
    }

    private ProviderConfig GetOrCreateProviderConfig(string name)
    {
        if (!_aiSettings.Providers.TryGetValue(name, out var cfg) || cfg == null)
        {
            cfg = DefaultProviderConfig(name);
            _aiSettings.Providers[name] = cfg;
        }
        return cfg;
    }

    private void FlushAiFieldsToSettings(string? providerName = null)
    {
        string? name = providerName ?? _aiFieldsBoundProvider ?? GetSelectedAiProviderName();
        if (string.IsNullOrEmpty(name)) return;

        var cfg = GetOrCreateProviderConfig(name);
        if (_aiDynamicFields.TryGetValue("ApiKey", out var apiEl) && apiEl is PasswordBox apiPb)
            cfg.ApiKey = apiPb.Password;
        if (_aiDynamicFields.TryGetValue("Endpoint", out var epEl) && epEl is TextBox epTb)
            cfg.Endpoint = epTb.Text.Trim();
        if (_aiDynamicFields.TryGetValue("Model", out var modelEl) && modelEl is TextBox modelTb)
            cfg.Model = modelTb.Text.Trim();
        if (_aiDynamicFields.TryGetValue("Temperature", out var tempEl) && tempEl is TextBox tempTb &&
            double.TryParse(tempTb.Text.Trim(), out double temp))
            cfg.Temperature = Math.Clamp(temp, 0, 2);
        if (_aiDynamicFields.TryGetValue("MaxTokens", out var tokEl) && tokEl is TextBox tokTb &&
            int.TryParse(tokTb.Text.Trim(), out int tok))
            cfg.MaxTokens = Math.Max(1, tok);
        if (_aiDynamicFields.TryGetValue("Enabled", out var enEl) && enEl is CheckBox enCb)
            cfg.Enabled = enCb.IsChecked == true;
    }

    private void RebuildAiProviderFields()
    {
        if (AiProviderFieldsPanel == null) return;

        string? name = GetSelectedAiProviderName();
        if (string.IsNullOrEmpty(name)) return;

        _aiSettings.SelectedProvider = name;
        _aiFieldsBoundProvider = name;
        var cfg = GetOrCreateProviderConfig(name);

        AiProviderFieldsPanel.Children.Clear();
        _aiDynamicFields.Clear();

        AiProviderFieldsPanel.Children.Add(MakeAiHint(
            name switch
            {
                "Grok" => "xAI Grok — OpenAI-compatible chat completions.",
                "Claude" => "Anthropic Claude — Messages API compatible endpoint.",
                "Gemini" => "Google Gemini — API key from Google AI Studio.",
                "GPT" => "OpenAI GPT — standard OpenAI API.",
                "DeepSeek" => "DeepSeek — OpenAI-compatible API.",
                "Mistral" => "Mistral AI — La Plateforme API.",
                "Qwen" => "Alibaba Qwen — DashScope compatible mode.",
                "Llama" => "Meta Llama — via Together or compatible host.",
                "Fireworks" => "Fireworks AI — fast inference API.",
                _ => "Configure API credentials for this provider."
            }));

        AddAiPasswordField("ApiKey", "API Key", cfg.ApiKey, "Paste your API key");
        AddAiTextField("Endpoint", "API Endpoint", cfg.Endpoint, "https://...");
        AddAiTextField("Model", "Model", cfg.Model, "model-id");
        AddAiTextField("Temperature", "Temperature", cfg.Temperature.ToString("0.##"), "0.0 – 2.0");
        AddAiTextField("MaxTokens", "Max Tokens", cfg.MaxTokens.ToString(), "e.g. 1024");
        AddAiCheckField("Enabled", "Enable this provider", cfg.Enabled);

        if (AiProviderStatusText != null)
            AiProviderStatusText.Text = cfg.Enabled
                ? $"{name} enabled — AI wiring comes in a future update."
                : $"{name} configured (disabled until enabled).";
    }

    private static TextBlock MakeAiHint(string text) => new()
    {
        Text = text,
        FontSize = 9,
        Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(0x70, 0x70, 0x90)),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 6)
    };

    private void AddAiLabel(string text)
    {
        AiProviderFieldsPanel.Children.Add(new TextBlock
        {
            Text = text,
            FontSize = 9,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xA0, 0xA0, 0xC8)),
            Margin = new Thickness(0, 4, 0, 2)
        });
    }

    private void AddAiTextField(string key, string label, string value, string placeholder)
    {
        AddAiLabel(label);
        var tb = new TextBox
        {
            Text = value,
            Height = 22,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x28)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC0, 0xC0, 0xE0)),
            ToolTip = placeholder
        };
        AiProviderFieldsPanel.Children.Add(tb);
        _aiDynamicFields[key] = tb;
    }

    private void AddAiPasswordField(string key, string label, string value, string placeholder)
    {
        AddAiLabel(label);
        var pb = new PasswordBox
        {
            Password = value,
            Height = 22,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0x14, 0x14, 0x28)),
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC0, 0xC0, 0xE0)),
            ToolTip = placeholder
        };
        AiProviderFieldsPanel.Children.Add(pb);
        _aiDynamicFields[key] = pb;
    }

    private void AddAiCheckField(string key, string label, bool value)
    {
        var cb = new CheckBox
        {
            Content = label,
            IsChecked = value,
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(0xC0, 0xC0, 0xE0)),
            FontSize = 10,
            Margin = new Thickness(0, 6, 0, 0)
        };
        AiProviderFieldsPanel.Children.Add(cb);
        _aiDynamicFields[key] = cb;
    }

    private void AiProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_aiProviderUiSyncing || !IsLoaded || AiProviderFieldsPanel == null) return;

        // Save fields to the provider we're leaving before loading the new one
        FlushAiFieldsToSettings(_aiFieldsBoundProvider);
        RebuildAiProviderFields();
    }

    private void SaveAiSettings_Click(object sender, RoutedEventArgs e) => SaveAiSettings();

    private void BotDisplayNameBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (BotDisplayNameBox == null) return;
        _botDisplayName = BotDisplayNameBox.Text.Trim();
        _aiSettings.BotDisplayName = _botDisplayName;
    }

    private static bool TryParseBbotCommand(string msg, out string userPrompt)
    {
        userPrompt = "";
        if (string.IsNullOrWhiteSpace(msg)) return false;
        msg = msg.Trim();
        if (!msg.StartsWith(AiCommandToken, StringComparison.OrdinalIgnoreCase)) return false;
        if (msg.Length > AiCommandToken.Length)
        {
            char next = msg[AiCommandToken.Length];
            if (!char.IsWhiteSpace(next)) return false;
            userPrompt = msg[(AiCommandToken.Length + 1)..].Trim();
        }
        return true;
    }

    private static readonly Regex FirstCommandTokenRegex =
        new(@"^(!\S+)", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static bool TryGetFirstCommandToken(string msg, out string token)
    {
        token = "";
        if (string.IsNullOrWhiteSpace(msg)) return false;
        var m = FirstCommandTokenRegex.Match(msg.Trim());
        if (!m.Success) return false;
        token = m.Groups[1].Value;
        return true;
    }

    private static bool CommandMatchesFirstToken(string msg, string cmd)
    {
        if (!TryGetFirstCommandToken(msg, out string token)) return false;
        return string.Equals(token, cmd, StringComparison.OrdinalIgnoreCase);
    }
}