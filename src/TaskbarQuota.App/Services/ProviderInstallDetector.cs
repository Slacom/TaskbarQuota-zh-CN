using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using TaskbarQuota.Usage;
using TaskbarQuota.Usage.Providers;

namespace TaskbarQuota;

/// <summary>
/// Detects whether a provider's CLI or desktop app is installed on this machine.
/// Used to distinguish "not set up" from "installed but waiting for the app to run".
/// </summary>
internal static class ProviderInstallDetector
{
    private static readonly string[] KnownClis =
        ["antigravity", "codex", "grok", "claude", "devin", "gh", "opencode", "cline", "kimi"];

    private static readonly ConcurrentDictionary<string, bool> CliAvailability = new(StringComparer.OrdinalIgnoreCase);
    private static volatile bool _cliCacheReady;

    internal static Func<ProviderId, bool>? IsInstalledOverrideForTesting;

    /// <summary>Precomputes CLI availability once so startup does not spawn many where.exe processes.</summary>
    public static void WarmCliCache()
    {
        if (_cliCacheReady)
            return;

        foreach (var name in KnownClis)
            CliAvailability[name] = ProbeCli(name);

        _cliCacheReady = true;
    }

    internal static void ResetCliCacheForTesting()
    {
        CliAvailability.Clear();
        _cliCacheReady = false;
    }

    public static bool IsInstalled(ProviderId id)
    {
        if (IsInstalledOverrideForTesting is { } overrideFn)
            return overrideFn(id);

        return _cliCacheReady ? IsInstalledCore(id) : IsInstalledWithoutCliProbe(id);
    }

    private static bool IsInstalledCore(ProviderId id) => id switch
    {
        ProviderId.Antigravity => IsAntigravityInstalled(),
        ProviderId.Codex => IsCliAvailable("codex") || File.Exists(CodexAuthPath()),
        ProviderId.Grok => IsCliAvailable("grok") || File.Exists(GrokAuthPath()),
        ProviderId.Claude => IsCliAvailable("claude")
            || File.Exists(ClaudeCredentialsPath())
            || IsDesktopAppInstalled("Claude"),
        ProviderId.Devin => IsCliAvailable("devin")
            || File.Exists(DevinCredentialsPath())
            || IsDevinAppInstalled(),
        ProviderId.Copilot => IsCliAvailable("gh")
            || !string.IsNullOrWhiteSpace(CredentialStore.Instance.For(ProviderId.Copilot).ApiKey),
        ProviderId.Cursor => IsCursorInstalled(),
        ProviderId.OpenCode => IsCliAvailable("opencode")
            || !string.IsNullOrWhiteSpace(CredentialStore.Instance.For(ProviderId.OpenCode).CookieHeader),
        ProviderId.OpenCodeGo => IsCliAvailable("opencode") || HasOpenCodeGoKey(),
        ProviderId.Cline or ProviderId.ClinePass => IsCliAvailable("cline") || File.Exists(ClineProvidersPath()),
        _ => true,
    };

    /// <summary>Fast path for the UI thread before <see cref="WarmCliCache"/> finishes — never spawns processes.</summary>
    private static bool IsInstalledWithoutCliProbe(ProviderId id) => id switch
    {
        ProviderId.Antigravity => IsAntigravityInstalledWithoutCli(),
        ProviderId.Codex => File.Exists(CodexAuthPath()) || HasCachedCli("codex"),
        ProviderId.Grok => File.Exists(GrokAuthPath()) || HasCachedCli("grok"),
        ProviderId.Claude => File.Exists(ClaudeCredentialsPath())
            || IsDesktopAppInstalled("Claude")
            || HasCachedCli("claude"),
        ProviderId.Devin => File.Exists(DevinCredentialsPath())
            || IsDevinAppInstalled()
            || HasCachedCli("devin"),
        ProviderId.Copilot => !string.IsNullOrWhiteSpace(CredentialStore.Instance.For(ProviderId.Copilot).ApiKey)
            || HasCachedCli("gh"),
        ProviderId.Cursor => IsCursorInstalled(),
        ProviderId.OpenCode =>
            !string.IsNullOrWhiteSpace(CredentialStore.Instance.For(ProviderId.OpenCode).CookieHeader)
            || HasCachedCli("opencode"),
        ProviderId.OpenCodeGo => HasOpenCodeGoKey() || HasCachedCli("opencode"),
        ProviderId.Cline or ProviderId.ClinePass => File.Exists(ClineProvidersPath()) || HasCachedCli("cline"),
        _ => true,
    };

    private static bool HasCachedCli(string name)
        => CliAvailability.TryGetValue(name, out bool available) && available;

    private static bool HasOpenCodeGoKey()
        => OpenCodeGoProvider.TryLoadApiKey() is { Length: > 0 };

    public static string WaitingMessage(ProviderId id) => id switch
    {
        ProviderId.Antigravity => "正在等待 Antigravity 启动。",
        ProviderId.Devin => "正在等待 Devin 应用启动。",
        ProviderId.Claude => "正在等待 Claude 启动。",
        ProviderId.Cursor => "正在等待 Cursor 启动。",
        _ => "正在等待应用启动。",
    };

    public static string NotInstalledMessage(ProviderId id) => id switch
    {
        ProviderId.Antigravity => "请安装 Antigravity，以便在此查看用量。",
        ProviderId.Grok => "请安装 Grok CLI 并运行 grok login。",
        ProviderId.Devin => "请安装 Devin CLI 或应用，然后登录。",
        ProviderId.Codex => "请安装 Codex CLI 并运行 codex login。",
        ProviderId.Claude => "请在浏览器中打开 Claude，或安装 Claude CLI/应用并登录。",
        ProviderId.Copilot => "请安装 GitHub CLI，或在设置中添加 Token。",
        ProviderId.Cursor => "请安装 Cursor 并登录。",
        ProviderId.OpenCode => "请登录 opencode.ai，或通过“修复”粘贴 Cookie。",
        ProviderId.OpenCodeGo => "请登录 OpenCode Go，或设置 OPENCODE_API_KEY。",
        ProviderId.Cline or ProviderId.ClinePass => "请安装 Cline CLI（npm i -g cline）并登录。",
        _ => "请设置此服务，以便在此查看用量。",
    };

    internal static string ClineProvidersPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".cline", "data", "settings", "providers.json");

    internal static string CodexAuthPath()
    {
        var codexHome = Environment.GetEnvironmentVariable("CODEX_HOME")?.Trim();
        if (!string.IsNullOrEmpty(codexHome))
            return Path.Combine(codexHome, "auth.json");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");
    }

    internal static string GrokAuthPath()
    {
        var grokHome = Environment.GetEnvironmentVariable("GROK_HOME")?.Trim();
        if (!string.IsNullOrEmpty(grokHome))
            return Path.Combine(grokHome, "auth.json");
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".grok", "auth.json");
    }

    internal static string ClaudeCredentialsPath()
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");

    internal static string DevinCredentialsPath()
    {
        string? fallback = null;
        foreach (var path in DevinCredentialsPaths())
        {
            fallback ??= path;
            if (File.Exists(path)) return path;
        }
        return fallback!;
    }

    private static IEnumerable<string> DevinCredentialsPaths()
    {
        // Windows: the CLI and app share credentials.toml under %APPDATA%\Devin.
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrEmpty(appData))
        {
            yield return Path.Combine(appData, "Devin", "credentials.toml");
            yield return Path.Combine(appData, "Devin - Next", "credentials.toml");
        }

        // XDG / `~/.local/share/devin` (Linux, macOS).
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME")?.Trim();
        var root = !string.IsNullOrEmpty(dataHome)
            ? dataHome
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
        yield return Path.Combine(root, "devin", "credentials.toml");
    }

    internal static bool IsCliAvailable(string name)
    {
        if (CliAvailability.TryGetValue(name, out bool cached))
            return cached;

        bool available = ProbeCli(name);
        CliAvailability[name] = available;
        return available;
    }

    private static bool ProbeCli(string name)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "where.exe",
                Arguments = name,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            using var process = Process.Start(psi);
            if (process == null)
                return false;

            string output = process.StandardOutput.ReadToEnd();
            if (!process.WaitForExit(500))
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                return false;
            }
            return process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsAntigravityInstalled()
    {
        if (IsCliAvailable("antigravity"))
            return true;

        return IsAntigravityInstalledWithoutCli();
    }

    private static bool IsAntigravityInstalledWithoutCli()
    {
        foreach (var root in AntigravityInstallRoots())
        {
            if (!Directory.Exists(root))
                continue;

            if (File.Exists(Path.Combine(root, "Antigravity.exe"))
                || File.Exists(Path.Combine(root, "bin", "antigravity.exe")))
                return true;

            foreach (var subdir in new[] { root, Path.Combine(root, "bin"), Path.Combine(root, "app") })
            {
                if (!Directory.Exists(subdir))
                    continue;

                try
                {
                    if (Directory.EnumerateFiles(subdir, "language_server*.exe", SearchOption.TopDirectoryOnly).Any())
                        return true;
                }
                catch
                {
                    // Ignore unreadable install directories.
                }
            }
        }

        return false;
    }

    private static IEnumerable<string> AntigravityInstallRoots()
    {
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            "Antigravity");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Antigravity");
        yield return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            "Antigravity");
    }

    private static bool IsDevinAppInstalled()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Directory.Exists(Path.Combine(appData, "Devin"))
            || Directory.Exists(Path.Combine(appData, "Devin - Next"));
    }

    private static bool IsCursorInstalled()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[]
        {
            Path.Combine(localAppData, "Programs", "cursor"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Cursor"),
        };

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            if (File.Exists(Path.Combine(root, "Cursor.exe")))
                return true;
        }

        return false;
    }

    private static bool IsKimiCodeInstalled()
    {
        var kimiBin = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".kimi-code", "bin", "kimi.exe");
        return File.Exists(kimiBin)
            || IsCliAvailable("kimi");
    }

    private static bool IsZCodeInstalled()
    {
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs", "ZCode");
        return File.Exists(Path.Combine(localPrograms, "ZCode.exe"));
    }

    private static bool IsDesktopAppInstalled(string appFolderName)
    {
        var localPrograms = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Programs",
            appFolderName);
        return Directory.Exists(localPrograms);
    }
}
