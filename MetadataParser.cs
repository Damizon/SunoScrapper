using System.Text.Json;
using System.Text.RegularExpressions;

namespace SunoScrapper;

public static class MetadataParser
{
    private const string RawMarker = "--- Raw API Response ---";

    public static bool IsMetadataFile(string path)
    {
        try
        {
            using var reader = new StreamReader(path);
            while (reader.ReadLine() is { } line)
                if (line.Equals(RawMarker, StringComparison.Ordinal)) return true;
        }
        catch { }
        return false;
    }

    public static SongRecord Parse(string metadataPath, string fallbackWorkflow, IReadOnlyList<string> audioFiles)
    {
        var text = File.ReadAllText(metadataPath);
        var markerIndex = text.IndexOf(RawMarker, StringComparison.Ordinal);
        if (markerIndex < 0) throw new InvalidDataException("Raw API response marker was not found.");

        var rawJson = text[(markerIndex + RawMarker.Length)..].Trim();
        using var document = JsonDocument.Parse(rawJson);
        var root = document.RootElement;
        var metadata = GetObject(root, "metadata");
        var persona = GetObject(root, "persona");
        var project = GetObject(root, "project");
        var controls = metadata is { } m ? GetObject(m, "control_sliders") : null;

        var title = String(root, "title");
        var song = new SongRecord
        {
            Id = String(root, "id"),
            Title = string.IsNullOrWhiteSpace(title) ? HeaderValue(text, "Title") ?? "Untitled" : title,
            Artist = String(root, "display_name", HeaderValue(text, "Artist") ?? ""),
            Year = IntFromHeader(text, "Year"),
            CreatedAt = Date(root, "created_at"),
            AudioUrl = String(root, "audio_url"),
            ImageUrl = String(root, "image_large_url", String(root, "image_url")),
            ModelName = String(root, "model_name"),
            IsLiked = Bool(root, "is_liked") ?? false,
            IsPublic = Bool(root, "is_public") ?? false,
            Workflow = ResolveWorkflow(project, text, title, fallbackWorkflow),
            MetadataPath = metadataPath,
        };

        if (metadata is { } md)
        {
            song.Lyrics = String(md, "prompt");
            song.StylePrompt = String(md, "tags", HeaderSection(text, "--- Creation Details ---", "--- Lyrics ---", "Prompt:"));
            song.GptDescription = String(md, "gpt_description_prompt");
            song.Duration = Double(md, "duration");
            song.GenerationType = FriendlyType(String(md, "type"));
            song.HasVocal = Bool(md, "has_vocal");
            song.MakeInstrumental = Bool(md, "make_instrumental");
            song.PersonaId = String(md, "persona_id");
            song.StemSourceId = String(md, "stem_from_id");
            song.SourceClipId = FirstNonEmpty(md, "cover_clip_id", "edited_clip_id", "stem_from_id", "upsample_clip_id", "artist_clip_id");
            song.StemType = String(md, "stem_type_group_name", String(md, "stem_type_id"));
            song.ModelDisplayName = NestedString(md, "model_badges", "songrow", "display_name");
            song.SecondaryBadge = NestedString(md, "secondary_badges", "display_name");
        }

        if (controls is { } control)
        {
            song.StyleWeight = Double(control, "style_weight");
            song.Weirdness = Double(control, "weirdness_constraint");
            song.AudioWeight = Double(control, "audio_weight");
        }

        if (persona is { } p)
        {
            song.PersonaName = String(p, "name");
            song.PersonaType = String(p, "persona_type");
            if (string.IsNullOrWhiteSpace(song.PersonaId)) song.PersonaId = String(p, "id");
        }

        if (root.TryGetProperty("display_tags", out var tags) && tags.ValueKind == JsonValueKind.Array)
            song.DisplayTags = tags.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()!).Where(x => !string.IsNullOrWhiteSpace(x)).ToList();

        song.LocalAudioPath = FindLocalAudio(metadataPath, text, song, audioFiles);
        return song;
    }

    private static string FindLocalAudio(string metadataPath, string text, SongRecord song, IReadOnlyList<string> audioFiles)
    {
        var direct = metadataPath[..^4];
        if (File.Exists(direct)) return direct;

        var metadataFor = Regex.Match(text, @"(?m)^Metadata for:\s*(.+?)\s*$").Groups[1].Value.Trim();
        if (metadataFor.Length > 0)
        {
            var candidate = Path.Combine(Path.GetDirectoryName(metadataPath)!, metadataFor);
            if (File.Exists(candidate)) return candidate;
        }

        var oddSuffix = Regex.Replace(direct, @"\.(wav|mp3)\s+\((\d+)\)$", " ($2).$1", RegexOptions.IgnoreCase);
        if (File.Exists(oddSuffix)) return oddSuffix;
        if (!string.IsNullOrWhiteSpace(song.Id))
        {
            var byId = audioFiles.FirstOrDefault(x => Path.GetFileName(x).Contains(song.Id[..Math.Min(8, song.Id.Length)], StringComparison.OrdinalIgnoreCase));
            if (byId is not null) return byId;
        }

        var normalizedTitle = Normalize(song.Title);
        var matches = audioFiles.Where(x => Normalize(Path.GetFileNameWithoutExtension(x)).Contains(normalizedTitle, StringComparison.OrdinalIgnoreCase)).ToList();
        return matches.Count == 1 ? matches[0] : "";
    }

    private static string Normalize(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");
    private static string ResolveWorkflow(JsonElement? project, string text, string title, string fallbackWorkflow)
    {
        if (project is { } workspace)
        {
            var projectName = String(workspace, "name");
            if (!string.IsNullOrWhiteSpace(projectName)) return projectName;
        }

        var metadataFor = Regex.Match(text, @"(?m)^Metadata for:\s*(.+?)\s*$").Groups[1].Value.Trim();
        var fileStem = Path.GetFileNameWithoutExtension(metadataFor);
        var normalizedTitle = Normalize(title);
        if (!string.IsNullOrWhiteSpace(fileStem) && !string.IsNullOrWhiteSpace(normalizedTitle))
        {
            for (var index = 0; index < fileStem.Length; index++)
            {
                if (fileStem[index] != '-') continue;
                var suffix = fileStem[(index + 1)..];
                if (!Normalize(suffix).StartsWith(normalizedTitle, StringComparison.OrdinalIgnoreCase)) continue;
                var prefix = fileStem[..index].Replace('_', ' ').Trim();
                prefix = Regex.Replace(prefix, @"\s+", " ");
                if (!string.IsNullOrWhiteSpace(prefix)) return prefix;
            }
        }
        return fallbackWorkflow;
    }
    private static JsonElement? GetObject(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Object ? value : null;
    private static string String(JsonElement parent, string name, string fallback = "") => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? fallback : fallback;
    private static bool? Bool(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && (value.ValueKind is JsonValueKind.True or JsonValueKind.False) ? value.GetBoolean() : null;
    private static double? Double(JsonElement parent, string name) => parent.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
    private static DateTime? Date(JsonElement parent, string name) => DateTime.TryParse(String(parent, name), out var value) ? value : null;
    private static string FirstNonEmpty(JsonElement parent, params string[] names) => names.Select(x => String(parent, x)).FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";
    private static string NestedString(JsonElement parent, params string[] path)
    {
        var current = parent;
        foreach (var part in path)
        {
            if (current.ValueKind != JsonValueKind.Object) return "";
            if (!current.TryGetProperty(part, out current)) return "";
        }
        return current.ValueKind == JsonValueKind.String ? current.GetString() ?? "" : "";
    }
    private static string FriendlyType(string type) => type switch
    {
        "gen" => "Generation", "upsample" => "Upscale", "edit_v3_export" => "Edit",
        "upload" => "Upload", "concat" => "Combined", "concat_infilling" => "Combined / Infill", _ => type
    };
    private static string? HeaderValue(string text, string key)
    {
        var match = Regex.Match(text, $@"(?m)^{Regex.Escape(key)}:\s*(.*?)\s*$", RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }
    private static int? IntFromHeader(string text, string key) => int.TryParse(HeaderValue(text, key), out var number) ? number : null;
    private static string HeaderSection(string text, string start, string end, string prefix)
    {
        var a = text.IndexOf(start, StringComparison.Ordinal);
        var b = text.IndexOf(end, StringComparison.Ordinal);
        if (a < 0 || b <= a) return "";
        var section = text[(a + start.Length)..b].Trim();
        return section.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ? section[prefix.Length..].Trim() : section;
    }
}
