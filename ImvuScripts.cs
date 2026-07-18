using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;

namespace IMVUCompanion;

/// <summary>
/// IMVU page-side scripts loaded from embedded Scripts/Imvu/*.js resources.
/// Edit those .js files when IMVU's DOM/CSS changes — not giant C# string literals.
/// </summary>
internal static class ImvuScripts
{
    private static readonly Assembly Asm = typeof(ImvuScripts).Assembly;
    private static readonly Lazy<string> FindChatRootLazy = new(() => Load("find-chat-root.js"));
    private static readonly Lazy<string> ExitWhisperModeLazy = new(() => Load("exit-whisper-mode.js"));
    private static readonly Lazy<string> WhisperFindRowLazy = new(() => Load("whisper-find-row.js"));
    private static readonly Lazy<string> ProactiveWhisperLazy = new(() => Load("proactive-whisper.js"));
    private static readonly Lazy<string> ActiveChatHookLazy = new(() => Load("active-chat-hook.js"));
    private static readonly Lazy<string> ChatObserverLazy = new(() => Load("chat-observer.js"));
    private static readonly Lazy<string> CollectJoinUidsLazy = new(() => Load("collect-join-uids.js"));

    public static string FindChatRoot => FindChatRootLazy.Value;
    public static string ExitWhisperMode => ExitWhisperModeLazy.Value;
    public static string WhisperFindRow => WhisperFindRowLazy.Value;
    public static string ProactiveWhisper => ProactiveWhisperLazy.Value;
    public static string ActiveChatHook => ActiveChatHookLazy.Value;
    public static string ChatObserver => ChatObserverLazy.Value;
    public static string CollectJoinUids => CollectJoinUidsLazy.Value;

    /// <summary>Find-chat-root helpers + observer body (same as former ChatObserverJs const).</summary>
    public static string ChatObserverFull => FindChatRoot + ChatObserver;

    /// <summary>Find-chat-root + collect join uids.</summary>
    public static string CollectJoinUidsFull => FindChatRoot + CollectJoinUids;

    /// <summary>Find-chat-root + proactive whisper helpers.</summary>
    public static string ProactiveWhisperFull => FindChatRoot + ProactiveWhisper;

    private static string Load(string fileName)
    {
        string? resource = Asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileName, StringComparison.OrdinalIgnoreCase)
                                 || n.EndsWith("." + fileName.Replace('-', '_'), StringComparison.OrdinalIgnoreCase)
                                 || n.Contains(fileName, StringComparison.OrdinalIgnoreCase));

        if (resource == null)
        {
            // Fallback: load from disk next to the app / project (dev)
            string[] candidates =
            {
                Path.Combine(AppContext.BaseDirectory, "Scripts", "Imvu", fileName),
                Path.Combine(AppContext.BaseDirectory, "Scripts", "Imvu", fileName.Replace('-', '_')),
            };
            foreach (string path in candidates)
            {
                if (File.Exists(path))
                    return File.ReadAllText(path, Encoding.UTF8);
            }

            throw new InvalidOperationException(
                "Missing embedded IMVU script: " + fileName +
                ". Resources: " + string.Join(", ", Asm.GetManifestResourceNames()));
        }

        using var stream = Asm.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException("Cannot open resource: " + resource);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        return reader.ReadToEnd();
    }
}
