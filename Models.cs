using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;
using System.Windows.Media.Imaging;

namespace SunoScrapper;

public sealed class LibraryCatalog
{
    public int SchemaVersion { get; set; }
    public int CacheFormatVersion { get; set; }
    public DateTime GeneratedAt { get; set; }
    public string LibraryRoot { get; set; } = "";
    public List<SongRecord> Songs { get; set; } = [];
    public List<SongRecord> Stems { get; set; } = [];
    public List<SongRecord> Duplicates { get; set; } = [];
    public List<ScanIssue> Issues { get; set; } = [];
}

public sealed class SongRecord : INotifyPropertyChanged
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "Untitled";
    public string Artist { get; set; } = "";
    public int? Year { get; set; }
    public DateTime? CreatedAt { get; set; }
    public double? Duration { get; set; }
    public string StylePrompt { get; set; } = "";
    public string Lyrics { get; set; } = "";
    public string GptDescription { get; set; } = "";
    public string AudioUrl { get; set; } = "";
    public string ImageUrl { get; set; } = "";
    public string LocalImagePath { get; set; } = "";
    public string MetadataPath { get; set; } = "";
    public string LocalAudioPath { get; set; } = "";
    public string Workflow { get; set; } = "";
    public List<string> AlsoFoundIn { get; set; } = [];
    public bool IsDuplicate { get; set; }
    public string DuplicateOfTitle { get; set; } = "";
    public string DuplicateOfWorkflow { get; set; } = "";
    public string ModelName { get; set; } = "";
    public string ModelDisplayName { get; set; } = "";
    public string GenerationType { get; set; } = "";
    public string SecondaryBadge { get; set; } = "";
    public string PersonaName { get; set; } = "";
    public string PersonaId { get; set; } = "";
    public string PersonaType { get; set; } = "";
    public bool? HasVocal { get; set; }
    public bool? MakeInstrumental { get; set; }
    public bool IsLiked { get; set; }
    public bool IsPublic { get; set; }
    public double? StyleWeight { get; set; }
    public double? Weirdness { get; set; }
    public double? AudioWeight { get; set; }
    public List<string> DisplayTags { get; set; } = [];
    public string SourceClipId { get; set; } = "";
    public string StemSourceId { get; set; } = "";
    public string StemType { get; set; } = "";
    [JsonIgnore] public bool IsStem => !string.IsNullOrWhiteSpace(StemSourceId);
    [JsonIgnore] public string StemSourceTitle => IsStem
        ? System.Text.RegularExpressions.Regex.Replace(Title, @"\s*\([^()]+\)\s*$", "").Trim()
        : Title;
    [JsonIgnore] public string DurationText => Duration is null ? "" : TimeSpan.FromSeconds(Duration.Value).ToString(Duration >= 3600 ? @"h\:mm\:ss" : @"m\:ss");
    [JsonIgnore] public string DateText => CreatedAt?.ToLocalTime().ToString("dd MMM yyyy, HH:mm") ?? (Year?.ToString() ?? "");
    [JsonIgnore] public string ModelText => !string.IsNullOrWhiteSpace(ModelDisplayName) ? ModelDisplayName : ModelName;
    [JsonIgnore] public string TagsText => string.Join("  ·  ", DisplayTags);
    [JsonIgnore] public string PersonaText => string.IsNullOrWhiteSpace(PersonaName) ? "" : $"Voice / Persona: {PersonaName}";
    [JsonIgnore] public string DuplicateText => IsDuplicate
        ? $"DUPLICATE OF: {DuplicateOfTitle}  ·  {DuplicateOfWorkflow}"
        : AlsoFoundIn.Count == 0 ? "" : $"Duplicates found in: {string.Join(", ", AlsoFoundIn)}";
    [JsonIgnore] public string DuplicateLocationText
    {
        get
        {
            if (!IsDuplicate) return "";
            var path = !string.IsNullOrWhiteSpace(LocalAudioPath) ? LocalAudioPath : MetadataPath;
            var directory = string.IsNullOrWhiteSpace(path) ? "" : Path.GetDirectoryName(path);
            return string.IsNullOrWhiteSpace(directory) ? "" : $"Duplicate location: {directory}";
        }
    }
    [JsonIgnore] public bool HasPrompt => !string.IsNullOrWhiteSpace(StylePrompt);
    [JsonIgnore] public bool HasLyrics => !string.IsNullOrWhiteSpace(Lyrics);
    [JsonIgnore] public bool HasMp3 => Uri.TryCreate(AudioUrl, UriKind.Absolute, out _);
    [JsonIgnore] public bool HasLocalAudio => File.Exists(LocalAudioPath);
    [JsonIgnore] public bool LocalAudioIsMp3 => HasLocalAudio && Path.GetExtension(LocalAudioPath).Equals(".mp3", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public bool HasGenerationDetails => StyleWeight is not null || Weirdness is not null || AudioWeight is not null || !string.IsNullOrWhiteSpace(PersonaName) || !string.IsNullOrWhiteSpace(SourceClipId);
    [JsonIgnore] public bool CanDownloadMp3 => HasMp3 && HasLocalAudio && !LocalAudioIsMp3;

    private bool _isPromptOpen;
    private bool _isLyricsOpen;
    private bool _isGenerationDetailsOpen;
    [JsonIgnore] public bool IsPromptOpen { get => _isPromptOpen; set => SetPanelState(ref _isPromptOpen, value); }
    [JsonIgnore] public bool IsLyricsOpen { get => _isLyricsOpen; set => SetPanelState(ref _isLyricsOpen, value); }
    [JsonIgnore] public bool IsGenerationDetailsOpen { get => _isGenerationDetailsOpen; set => SetPanelState(ref _isGenerationDetailsOpen, value); }
    [JsonIgnore] public int OpenPanelCount => Math.Max(1, (_isPromptOpen ? 1 : 0) + (_isLyricsOpen ? 1 : 0) + (_isGenerationDetailsOpen ? 1 : 0));
    [JsonIgnore] public bool HasOpenPanels => _isPromptOpen || _isLyricsOpen || _isGenerationDetailsOpen;

    private void SetPanelState(ref bool field, bool value, [CallerMemberName] string? propertyName = null)
    {
        if (field == value) return;
        field = value;
        OnPropertyChanged(propertyName);
        OnPropertyChanged(nameof(OpenPanelCount));
        OnPropertyChanged(nameof(HasOpenPanels));
    }
    [JsonIgnore] public string SliderText
    {
        get
        {
            var parts = new List<string>();
            if (StyleWeight is not null) parts.Add($"Style {StyleWeight:P0}");
            if (Weirdness is not null) parts.Add($"Weirdness {Weirdness:P0}");
            if (AudioWeight is not null) parts.Add($"Audio influence {AudioWeight:P0}");
            return string.Join("  ·  ", parts);
        }
    }

    private BitmapImage? _coverImage;
    [JsonIgnore] public bool IsCoverLoading { get; set; }
    [JsonIgnore]
    public BitmapImage? CoverImage
    {
        get => _coverImage;
        set { _coverImage = value; OnPropertyChanged(); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    public void OnPropertyChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class WorkflowGroup : INotifyPropertyChanged
{
    public string Name { get; set; } = "";
    public List<SongRecord> Songs { get; set; } = [];
    public bool IsStemGroup { get; set; }
    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }
    public string CountText => IsStemGroup
        ? $"{Songs.Count} stem{(Songs.Count == 1 ? "" : "s")}"
        : $"{Songs.Count} song{(Songs.Count == 1 ? "" : "s")}";
    public string HeaderBackground => Name.Equals("Duplicates", StringComparison.OrdinalIgnoreCase) ? "#2A1915" : "#151A20";
    public string HeaderBorderBrush => Name.Equals("Duplicates", StringComparison.OrdinalIgnoreCase) ? "#D66A42" : "#303841";
    public string HeaderForeground => Name.Equals("Duplicates", StringComparison.OrdinalIgnoreCase) ? "#FFB08F" : "#EEF2F5";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class ScanIssue
{
    public string Type { get; set; } = "";
    public string Path { get; set; } = "";
    public string Message { get; set; } = "";
}

