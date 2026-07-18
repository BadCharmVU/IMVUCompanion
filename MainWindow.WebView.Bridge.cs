using System;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace IMVUCompanion;

public partial class MainWindow
{
    private static string JsIife(string body)
    {
        var t = body.Trim();
        if (t.Length == 0) return "(() => {})();";
        if (t.StartsWith("(() =>", StringComparison.Ordinal) && t.EndsWith(")();", StringComparison.Ordinal))
            return t;
        return "(() => {" + t + "})();";
    }

    private static string? ParseJsStringResult(string json)
    {
        if (string.IsNullOrWhiteSpace(json) || json == "null" || json == "undefined") return null;
        try { return JsonSerializer.Deserialize<string>(json); }
        catch
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind == JsonValueKind.String)
                    return doc.RootElement.GetString();
                // Non-string results (numbers, objects) — stringify for diagnostics
                if (doc.RootElement.ValueKind != JsonValueKind.Null &&
                    doc.RootElement.ValueKind != JsonValueKind.Undefined)
                    return doc.RootElement.ToString();
            }
            catch { }
            // Last resort: strip surrounding quotes if present
            var t = json.Trim();
            if (t.Length >= 2 && t[0] == '"' && t[^1] == '"')
                return t[1..^1];
            return t.Length > 0 ? t : null;
        }
    }

    /// <summary>Run a complete JS expression as-is (e.g. top-level async IIFE). WebView2 awaits returned promises.</summary>
    private async Task<string?> RunJsExpressionAsync(string expression, bool logErrors = false)
    {
        if (!IsWebViewReady) return null;
        try
        {
            string json = await ImvuWebView.CoreWebView2.ExecuteScriptAsync(expression.Trim());
            return ParseJsStringResult(json);
        }
        catch (Exception ex)
        {
            if (logErrors) AppendLog("JS error: " + ex.Message, LogCategory.Warning);
            return null;
        }
    }

    private async Task<string?> RunJsStringAsync(string js, bool logErrors = false)
    {
        if (!IsWebViewReady) return null;
        try
        {
            string script = JsIife(js);
            string json = await ImvuWebView.CoreWebView2.ExecuteScriptAsync(script);
            var parsed = ParseJsStringResult(json);
            if (parsed == null && logErrors && !string.IsNullOrEmpty(json) && json != "null")
                AppendLog("JS raw result: " + (json.Length > 180 ? json[..180] + "…" : json), LogCategory.Warning);
            return parsed;
        }
        catch (Exception ex)
        {
            if (logErrors) AppendLog("JS error: " + ex.Message, LogCategory.Warning);
            return null;
        }
    }

    private async Task<string[]?> RunJsStringArrayAsync(string js)
    {
        if (!IsWebViewReady) return null;
        try
        {
            string json = await ImvuWebView.CoreWebView2.ExecuteScriptAsync(JsIife(js));
            if (json == "null" || json == "undefined") return null;
            return JsonSerializer.Deserialize<string[]>(json);
        }
        catch { return null; }
    }

    private async Task<bool> RunJsVoidAsync(string js, bool logErrors = false)
    {
        if (!IsWebViewReady) return false;
        try
        {
            await ImvuWebView.CoreWebView2.ExecuteScriptAsync(JsIife(js));
            return true;
        }
        catch (Exception ex)
        {
            if (logErrors) AppendLog("JS error: " + ex.Message, LogCategory.Warning);
            return false;
        }
    }

    /// <summary>
    /// Trusted mouse click via CDP. Synthetic DOM events are often ignored by IMVU's user menu;
    /// reply-whisper works with JS clicks because that control listens differently.
    /// </summary>
    private async Task<bool> CdpMouseClickAsync(double x, double y, string button = "left")
    {
        if (!IsWebViewReady) return false;
        try
        {
            var core = ImvuWebView.CoreWebView2;
            string Move(string type, string btn) =>
                $$"""{"type":"{{type}}","x":{{x.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"y":{{y.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"button":"{{btn}}","buttons":{{(type == "mousePressed" ? "1" : "0")}},"clickCount":1}""";

            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent",
                $$"""{"type":"mouseMoved","x":{{x.ToString(System.Globalization.CultureInfo.InvariantCulture)}},"y":{{y.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}""");
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", Move("mousePressed", button));
            await core.CallDevToolsProtocolMethodAsync("Input.dispatchMouseEvent", Move("mouseReleased", button));
            return true;
        }
        catch (Exception ex)
        {
            AppendLog("CDP click failed: " + ex.Message, LogCategory.Warning);
            return false;
        }
    }

    private static bool TryParsePoint(string? s, out double x, out double y)
    {
        x = y = 0;
        if (string.IsNullOrWhiteSpace(s)) return false;
        var parts = s.Split(',');
        if (parts.Length < 2) return false;
        return double.TryParse(parts[0].Trim(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out x)
            && double.TryParse(parts[1].Trim(), System.Globalization.NumberStyles.Float,
                   System.Globalization.CultureInfo.InvariantCulture, out y);
    }
}
