using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Media.Imaging;

namespace SunoScrapper;

public sealed class LibraryScanner
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private readonly string _root;
    private readonly string _dbDirectory;
    private readonly string _imagesDirectory;

    public LibraryScanner(string root)
    {
        _root = root;
        _dbDirectory = Path.Combine(root, "scrapper_db");
        _imagesDirectory = Path.Combine(_dbDirectory, "images");
    }

    public async Task<LibraryCatalog> ScanAsync(IProgress<string>? progress = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_dbDirectory);
        Directory.CreateDirectory(_imagesDirectory);
        var catalog = new LibraryCatalog { SchemaVersion = 5, GeneratedAt = DateTime.Now, LibraryRoot = _root };
        var metadataFiles = Directory.GetFiles(_root, "*.txt", SearchOption.AllDirectories)
            .Where(x => !IsInDatabaseDirectory(x))
            .Where(MetadataParser.IsMetadataFile)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var audioFiles = Directory.EnumerateFiles(_root, "*.*", SearchOption.AllDirectories)
            .Where(x => Path.GetExtension(x).Equals(".wav", StringComparison.OrdinalIgnoreCase)
                || Path.GetExtension(x).Equals(".mp3", StringComparison.OrdinalIgnoreCase))
            .Where(x => !IsInDatabaseDirectory(x))
            .ToArray();

        foreach (var metadataPath in metadataFiles)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var containingDirectory = Path.GetDirectoryName(metadataPath) ?? _root;
            var fallbackWorkflow = Path.GetFileName(containingDirectory);
            if (string.IsNullOrWhiteSpace(fallbackWorkflow)) fallbackWorkflow = "Unassigned";
            progress?.Report($"Scanning {Path.GetFileName(metadataPath)}…");
            try
            {
                catalog.Songs.Add(MetadataParser.Parse(metadataPath, fallbackWorkflow, audioFiles));
            }
            catch (Exception ex)
            {
                catalog.Issues.Add(new ScanIssue { Type = "Metadata", Path = metadataPath, Message = ex.Message });
            }
        }

        MergeSafetyDuplicates(catalog);
        await DownloadImagesAsync(catalog.Songs, progress, cancellationToken);
        await File.WriteAllTextAsync(Path.Combine(_dbDirectory, "catalog.json"), JsonSerializer.Serialize(catalog, JsonOptions), cancellationToken);
        await WriteReportAsync(catalog, cancellationToken);
        return catalog;
    }

    private bool IsInDatabaseDirectory(string path) =>
        Path.GetFullPath(path).StartsWith(_dbDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    public async Task<LibraryCatalog?> LoadCacheAsync()
    {
        var path = Path.Combine(_dbDirectory, "catalog.json");
        if (!File.Exists(path)) return null;
        try
        {
            await using var stream = File.OpenRead(path);
            var catalog = await JsonSerializer.DeserializeAsync<LibraryCatalog>(stream, JsonOptions);
            return catalog?.SchemaVersion == 5 ? catalog : null;
        }
        catch { return null; }
    }

    public static void LoadCover(SongRecord song)
    {
        if (!File.Exists(song.LocalImagePath)) return;
        try
        {
            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 240;
            image.UriSource = new Uri(song.LocalImagePath, UriKind.Absolute);
            image.EndInit();
            image.Freeze();
            song.CoverImage = image;
        }
        catch { }
    }

    private void MergeSafetyDuplicates(LibraryCatalog catalog)
    {
        var result = new List<SongRecord>();
        foreach (var group in catalog.Songs.GroupBy(x => string.IsNullOrWhiteSpace(x.Id) ? $"path:{x.MetadataPath}" : x.Id, StringComparer.OrdinalIgnoreCase))
        {
            var records = group.ToList();
            var primary = records
                .OrderBy(x => WorkspaceFolderMatchRank(x))
                .ThenBy(x => HasDownloadCopyMarker(x.MetadataPath) ? 1 : 0)
                .ThenBy(x => BaseFileNameLength(x.MetadataPath))
                .ThenBy(x => x.MetadataPath, StringComparer.OrdinalIgnoreCase)
                .First();
            var duplicates = records.Where(x => !ReferenceEquals(x, primary)).ToList();
            primary.AlsoFoundIn = duplicates.Select(x => x.Workflow).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var duplicate in duplicates)
            {
                duplicate.IsDuplicate = true;
                duplicate.DuplicateOfTitle = primary.Title;
                duplicate.DuplicateOfWorkflow = primary.Workflow;
                catalog.Duplicates.Add(duplicate);
            }
            if (!primary.HasLocalAudio)
            {
                var copyWithAudio = records.FirstOrDefault(x => x.HasLocalAudio);
                if (copyWithAudio is not null) primary.LocalAudioPath = copyWithAudio.LocalAudioPath;
            }
            result.Add(primary);
        }
        catalog.Songs = result;
    }

    private int WorkspaceFolderMatchRank(SongRecord song)
    {
        var relative = Path.GetRelativePath(_root, song.MetadataPath);
        var separator = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
        if (separator < 0) return 2;
        var folder = NormalizeName(relative[..separator]);
        var workspace = NormalizeName(song.Workflow);
        if (folder.Length == 0 || workspace.Length == 0) return 2;
        if (folder.Equals(workspace, StringComparison.Ordinal)) return 0;
        return folder.Contains(workspace, StringComparison.Ordinal) || workspace.Contains(folder, StringComparison.Ordinal) ? 1 : 2;
    }

    private static bool HasDownloadCopyMarker(string path) =>
        Regex.IsMatch(Path.GetFileName(path), @"(?:\(\d+\)|\bcopy\b|\bkopia\b)", RegexOptions.IgnoreCase);

    private static int BaseFileNameLength(string path) => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(path)).Length;

    private static string NormalizeName(string value) => Regex.Replace(value.ToLowerInvariant(), @"[^\p{L}\p{N}]", "");

    private async Task DownloadImagesAsync(IEnumerable<SongRecord> songs, IProgress<string>? progress, CancellationToken cancellationToken)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(25) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SunoLibraryScrapper/1.0");
        using var gate = new SemaphoreSlim(8);
        var tasks = songs.Select(async song =>
        {
            if (string.IsNullOrWhiteSpace(song.Id) || string.IsNullOrWhiteSpace(song.ImageUrl)) return;
            var extension = Uri.TryCreate(song.ImageUrl, UriKind.Absolute, out var uri) ? Path.GetExtension(uri.AbsolutePath) : ".jpg";
            if (string.IsNullOrWhiteSpace(extension) || extension.Length > 5) extension = ".jpg";
            var path = Path.Combine(_imagesDirectory, song.Id + extension);
            song.LocalImagePath = path;
            if (File.Exists(path)) return;
            await gate.WaitAsync(cancellationToken);
            try
            {
                progress?.Report($"Downloading artwork: {song.Title}");
                var bytes = await client.GetByteArrayAsync(song.ImageUrl, cancellationToken);
                var temp = path + ".tmp";
                await File.WriteAllBytesAsync(temp, bytes, cancellationToken);
                File.Move(temp, path, true);
            }
            catch
            {
                // Image failures should never abort the catalog build.
            }
            finally { gate.Release(); }
        });
        await Task.WhenAll(tasks);
    }

    private async Task WriteReportAsync(LibraryCatalog catalog, CancellationToken token)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Suno Library scan report");
        sb.AppendLine($"Generated: {catalog.GeneratedAt:O}");
        sb.AppendLine($"Unique songs: {catalog.Songs.Count}");
        sb.AppendLine($"Duplicate copies: {catalog.Duplicates.Count}");
        sb.AppendLine($"Missing local audio matches: {catalog.Songs.Count(x => !x.HasLocalAudio)}");
        sb.AppendLine($"Missing MP3 links: {catalog.Songs.Count(x => !x.HasMp3)}");
        sb.AppendLine($"Issues: {catalog.Issues.Count}");
        foreach (var issue in catalog.Issues) sb.AppendLine($"[{issue.Type}] {issue.Path}: {issue.Message}");
        await File.WriteAllTextAsync(Path.Combine(_dbDirectory, "scan-report.txt"), sb.ToString(), token);
    }
}
