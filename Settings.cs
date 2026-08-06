using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace TaskbarMarker;

/// <summary>Raw shape of rules.json.</summary>
internal sealed class Config
{
    public int PollIntervalMs { get; set; } = 750;

    /// <summary>Thickness (px @100% DPI) of the colored bar drawn under a taskbar button.</summary>
    public int BarHeight { get; set; } = 4;

    /// <summary>Horizontal inset (px @100% DPI) applied to each side of the bar.</summary>
    public int BarInset { get; set; } = 6;

    public bool ShowLabel { get; set; } = true;

    public int LabelFontSize { get; set; } = 9;

    /// <summary>
    /// When false (default) only running-app buttons are considered. Set to true to also allow
    /// matching Start / Search / Widgets / tray icons.
    /// </summary>
    public bool IncludeAllButtons { get; set; }

    public List<RuleDto> Rules { get; set; } = new();

    public Config Clone() => new()
    {
        PollIntervalMs = PollIntervalMs,
        BarHeight = BarHeight,
        BarInset = BarInset,
        ShowLabel = ShowLabel,
        LabelFontSize = LabelFontSize,
        IncludeAllButtons = IncludeAllButtons,
        Rules = Rules.ConvertAll(rule => rule.Clone()),
    };
}

internal sealed class RuleDto
{
    /// <summary>Regex matched (case-insensitive) against the taskbar button's accessible name.</summary>
    public string? Match { get; set; }

    /// <summary>
    /// Regex matched against the button's application id. This is the only way to tell apart two
    /// buttons of the same app while taskbar grouping is on, because their names are identical.
    /// </summary>
    public string? MatchAppId { get; set; }

    /// <summary>Hex color, e.g. "#E53935".</summary>
    public string Color { get; set; } = "#E53935";

    /// <summary>Optional short text drawn next to the taskbar button.</summary>
    public string? Label { get; set; }

    public RuleDto Clone() => new()
    {
        Match = Match,
        MatchAppId = MatchAppId,
        Color = Color,
        Label = Label,
    };
}

internal sealed record CompiledRule(Regex? NamePattern, Regex? AppIdPattern, Color Color, string? Label)
{
    public bool Matches(TaskbarButton button)
    {
        if (NamePattern is not null && !NamePattern.IsMatch(button.Name))
            return false;
        if (AppIdPattern is not null && !AppIdPattern.IsMatch(button.AppId))
            return false;
        return NamePattern is not null || AppIdPattern is not null;
    }
}

internal sealed class Settings
{
    public required Config Raw { get; init; }
    public required IReadOnlyList<CompiledRule> Rules { get; init; }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static string DefaultPath
    {
        get
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskbarMarker");
            Directory.CreateDirectory(directory);

            string path = Path.Combine(directory, "rules.json");
            string legacyLocalPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "TaskbarNote",
                "rules.json");
            string legacyPortablePath = Path.Combine(AppContext.BaseDirectory, "rules.json");

            if (!File.Exists(path))
            {
                string? source = File.Exists(legacyLocalPath)
                    ? legacyLocalPath
                    : File.Exists(legacyPortablePath) ? legacyPortablePath : null;
                if (source is not null)
                    File.Copy(source, path);
            }

            return path;
        }
    }

    public static void Save(string path, Config config) =>
        File.WriteAllText(path, JsonSerializer.Serialize(config, WriteOptions));

    public static Settings Load(string path, out string? error)
    {
        error = null;
        Config config;
        try
        {
            config = JsonSerializer.Deserialize<Config>(File.ReadAllText(path), JsonOptions) ?? new Config();
        }
        catch (Exception ex)
        {
            error = ex.Message;
            config = new Config();
        }

        var compiled = new List<CompiledRule>();
        foreach (var dto in config.Rules)
        {
            if (string.IsNullOrWhiteSpace(dto.Match) && string.IsNullOrWhiteSpace(dto.MatchAppId))
                continue;

            Regex? namePattern;
            Regex? appIdPattern;
            try
            {
                namePattern = Compile(dto.Match);
                appIdPattern = Compile(dto.MatchAppId);
            }
            catch (ArgumentException ex)
            {
                error ??= $"Invalid regex in rule \"{dto.Label ?? dto.Match ?? dto.MatchAppId}\": {ex.Message}";
                continue;
            }

            compiled.Add(new CompiledRule(namePattern, appIdPattern, ParseColor(dto.Color), dto.Label));
        }

        return new Settings { Raw = config, Rules = compiled };
    }

    private static Regex? Compile(string? pattern) =>
        string.IsNullOrWhiteSpace(pattern)
            ? null
            : new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public CompiledRule? Match(TaskbarButton button)
    {
        foreach (var rule in Rules)
        {
            if (rule.Matches(button))
                return rule;
        }
        return null;
    }

    public static Color ParseColor(string value)
    {
        try
        {
            var parsed = ColorTranslator.FromHtml(value);
            // Treat a fully transparent parse result as "unset" and fall back to a visible default.
            return parsed.A == 0 ? Color.FromArgb(255, parsed) : parsed;
        }
        catch (Exception)
        {
            return Color.FromArgb(0xE5, 0x39, 0x35);
        }
    }

    public static string ToHex(Color color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
