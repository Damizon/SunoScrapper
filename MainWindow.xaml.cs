using System.Diagnostics;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SunoScrapper;

public partial class MainWindow : Window
{
    private static readonly HttpClient DownloadClient = CreateDownloadClient();
    private readonly string _libraryRoot;
    private readonly LibraryScanner _scanner;
    private LibraryCatalog _catalog = new();
    private List<WorkflowGroup> _visibleGroups = [];
    private readonly DispatcherTimer _searchTimer;
    private bool _isFullScreen;
    private WindowState _previousState;
    private WindowStyle _previousStyle;
    private ResizeMode _previousResizeMode;

    public MainWindow()
    {
        InitializeComponent();
        _libraryRoot = ResolveLibraryRoot();
        _scanner = new LibraryScanner(_libraryRoot);
        _searchTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        _searchTimer.Tick += (_, _) => { _searchTimer.Stop(); ApplyFilterAndSort(); };
        RootPathText.Text = _libraryRoot;
        Loaded += MainWindow_Loaded;
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        var cached = await _scanner.LoadCacheAsync();
        if (cached is not null)
        {
            _catalog = cached;
            ShowCatalog();
            StatusText.Text = $"Loaded catalog from {cached.GeneratedAt:g}";
        }
        else
        {
            await ScanAsync();
        }
    }

    private async Task ScanAsync()
    {
        RescanButton.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        var progress = new Progress<string>(message => StatusText.Text = message);
        try
        {
            _catalog = await _scanner.ScanAsync(progress);
            ShowCatalog();
            StatusText.Text = $"Scan complete — {_catalog.Songs.Count} unique songs";
        }
        catch (Exception ex)
        {
            StatusText.Text = "Scan failed";
            MessageBox.Show(this, ex.Message, "Suno Library — Scan error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            RescanButton.IsEnabled = true;
            ScanProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowCatalog()
    {
        foreach (var song in _catalog.Songs) LibraryScanner.LoadCover(song);
        ApplyFilterAndSort();
    }

    private void ApplyFilterAndSort()
    {
        var query = SearchBox.Text.Trim();
        IEnumerable<SongRecord> songs = _catalog.Songs.Concat(_catalog.Duplicates);
        if (query.Length > 0)
        {
            songs = songs.Where(song => SearchableText(song).Contains(query, StringComparison.CurrentCultureIgnoreCase));
        }

        songs = SortBox.SelectedIndex switch
        {
            1 => songs.OrderByDescending(x => x.CreatedAt),
            2 => songs.OrderBy(x => x.CreatedAt),
            3 => songs.OrderBy(x => x.Title, StringComparer.CurrentCultureIgnoreCase),
            4 => songs.OrderByDescending(x => x.Duration),
            _ => songs.OrderBy(x => x.Workflow, StringComparer.CurrentCultureIgnoreCase).ThenByDescending(x => x.CreatedAt)
        };

        var groups = songs.GroupBy(x => x.IsDuplicate ? "Duplicates" : x.Workflow, StringComparer.CurrentCultureIgnoreCase)
            .Select(x => new WorkflowGroup { Name = x.Key, Songs = x.ToList(), IsExpanded = query.Length > 0 })
            .OrderBy(x => x.Name.Equals("Duplicates", StringComparison.OrdinalIgnoreCase) ? -1 : x.Name.Equals("safety", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .ThenBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

        _visibleGroups = groups;
        WorkflowList.ItemsSource = _visibleGroups;
        var shown = groups.Sum(x => x.Songs.Count);
        StatsText.Text = $"{shown} shown  ·  {_catalog.Songs.Count} unique  ·  {_catalog.Duplicates.Count} duplicate copies";
        EmptyState.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private static string SearchableText(SongRecord song) => string.Join('\n', song.Title, song.Artist, song.Workflow, song.StylePrompt, song.Lyrics,
        song.GptDescription, song.PersonaName, song.ModelName, song.ModelDisplayName, song.GenerationType, string.Join(' ', song.DisplayTags),
        song.IsDuplicate ? $"duplicate duplicates {song.DuplicateOfTitle} {song.DuplicateOfWorkflow}" : song.AlsoFoundIn.Count > 0 ? "duplicate duplicates" : "");

    private async void Rescan_Click(object sender, RoutedEventArgs e) => await ScanAsync();
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsLoaded) return;
        _searchTimer.Stop();
        _searchTimer.Start();
    }
    private void SortBox_SelectionChanged(object sender, SelectionChangedEventArgs e) { if (IsLoaded) ApplyFilterAndSort(); }
    private void Fullscreen_Click(object sender, RoutedEventArgs e) => ToggleFullScreen();
    private void ExpandAll_Click(object sender, RoutedEventArgs e) { foreach (var group in _visibleGroups) group.IsExpanded = true; }
    private void CollapseAll_Click(object sender, RoutedEventArgs e) { foreach (var group in _visibleGroups) group.IsExpanded = false; }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F11) { ToggleFullScreen(); e.Handled = true; }
        else if (e.Key == Key.Escape && _isFullScreen) { ToggleFullScreen(); e.Handled = true; }
        else if (e.Key == Key.F && Keyboard.Modifiers.HasFlag(ModifierKeys.Control)) { SearchBox.Focus(); SearchBox.SelectAll(); e.Handled = true; }
    }

    private void ToggleFullScreen()
    {
        if (!_isFullScreen)
        {
            _previousState = WindowState;
            _previousStyle = WindowStyle;
            _previousResizeMode = ResizeMode;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            WindowState = WindowState.Maximized;
            FullscreenButton.Content = "Exit full screen  Esc";
        }
        else
        {
            WindowStyle = _previousStyle;
            ResizeMode = _previousResizeMode;
            WindowState = _previousState == WindowState.Minimized ? WindowState.Normal : _previousState;
            FullscreenButton.Content = "Full screen  F11";
        }
        _isFullScreen = !_isFullScreen;
    }

    private static SongRecord? SongFrom(object sender) => (sender as FrameworkElement)?.Tag as SongRecord;
    private void OpenMp3_Click(object sender, RoutedEventArgs e) => OpenTarget(SongFrom(sender)?.AudioUrl);
    private void OpenWav_Click(object sender, RoutedEventArgs e) => OpenTarget(SongFrom(sender)?.WavPath);
    private static HttpClient CreateDownloadClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("SunoScrapper/1.0");
        return client;
    }

    private async void DownloadMp3_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || SongFrom(sender) is not { } song || !song.CanDownloadMp3) return;

        var destination = Path.ChangeExtension(song.WavPath, ".mp3");
        if (File.Exists(destination))
        {
            var overwrite = MessageBox.Show(this, $"MP3 already exists:\n\n{destination}\n\nReplace it?", "Download MP3",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (overwrite != MessageBoxResult.Yes)
            {
                StatusText.Text = "MP3 download cancelled";
                return;
            }
        }

        var temporary = destination + ".download";
        button.IsEnabled = false;
        ScanProgress.Visibility = Visibility.Visible;
        StatusText.Text = $"Downloading MP3: {song.Title}";

        try
        {
            using var response = await DownloadClient.GetAsync(song.AudioUrl, HttpCompletionOption.ResponseHeadersRead);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            {
                await using var source = await response.Content.ReadAsStreamAsync();
                await using var target = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await source.ReadAsync(buffer)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read));
                    received += read;
                    StatusText.Text = total > 0
                        ? $"Downloading MP3: {song.Title} — {(double)received / total.Value:P0}"
                        : $"Downloading MP3: {song.Title} — {received / 1024d / 1024d:F1} MB";
                }
                await target.FlushAsync();
            }

            File.Move(temporary, destination, true);
            StatusText.Text = $"MP3 saved: {Path.GetFileName(destination)}";
        }
        catch (Exception ex)
        {
            StatusText.Text = "MP3 download failed";
            ShowActionError("Could not download the MP3 file.", ex);
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); } catch { }
            button.IsEnabled = song.CanDownloadMp3;
            ScanProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void RevealWav_Click(object sender, RoutedEventArgs e)
    {
        var path = SongFrom(sender)?.WavPath;
        if (!File.Exists(path)) return;
        Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
    }

    private void CopyWav_Click(object sender, RoutedEventArgs e)
    {
        var path = SongFrom(sender)?.WavPath;
        if (!File.Exists(path)) return;
        try
        {
            var files = new System.Collections.Specialized.StringCollection { path };
            Clipboard.SetFileDropList(files);
            StatusText.Text = $"Copied WAV: {Path.GetFileName(path)}";
        }
        catch (Exception ex) { ShowActionError("Could not copy the WAV file.", ex); }
    }

    private void CopyPath_Click(object sender, RoutedEventArgs e) => CopyText(SongFrom(sender)?.WavPath, "WAV path copied");

    private async void DeleteDuplicate_Click(object sender, RoutedEventArgs e)
    {
        if (SongFrom(sender) is not { IsDuplicate: true } duplicate) return;
        var primary = _catalog.Songs.FirstOrDefault(x => !string.IsNullOrWhiteSpace(duplicate.Id) && x.Id.Equals(duplicate.Id, StringComparison.OrdinalIgnoreCase));
        var canDeleteWav = File.Exists(duplicate.WavPath)
            && !string.Equals(primary?.WavPath, duplicate.WavPath, StringComparison.OrdinalIgnoreCase)
            && _catalog.Duplicates.Count(x => string.Equals(x.WavPath, duplicate.WavPath, StringComparison.OrdinalIgnoreCase)) == 1;
        var files = new List<string>();
        if (File.Exists(duplicate.MetadataPath)) files.Add(duplicate.MetadataPath);
        if (canDeleteWav) files.Add(duplicate.WavPath);
        if (files.Count == 0) return;

        var message = $"Delete this duplicate of '{duplicate.DuplicateOfTitle}'?\n\n" + string.Join("\n", files) +
            (canDeleteWav ? "" : "\n\nThe WAV is shared with another entry and will be kept.");
        if (MessageBox.Show(this, message, "Delete duplicate", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;
        try
        {
            foreach (var file in files) File.Delete(file);
            StatusText.Text = "Duplicate deleted — rescanning library";
            await ScanAsync();
        }
        catch (Exception ex) { ShowActionError("Could not delete the duplicate files.", ex); }
    }
    private void CopyPrompt_Click(object sender, RoutedEventArgs e) => CopyText(SongFrom(sender)?.StylePrompt, "Prompt copied");
    private void CopyLyrics_Click(object sender, RoutedEventArgs e) => CopyText(SongFrom(sender)?.Lyrics, "Lyrics copied");

    private void CopyText(string? value, string status)
    {
        if (string.IsNullOrWhiteSpace(value)) return;
        try { Clipboard.SetText(value); StatusText.Text = status; }
        catch (Exception ex) { ShowActionError("Could not access the Windows clipboard.", ex); }
    }

    private void OpenTarget(string? target)
    {
        if (string.IsNullOrWhiteSpace(target)) return;
        try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
        catch (Exception ex) { ShowActionError("Windows could not open this item.", ex); }
    }

    private void ShowActionError(string message, Exception ex) => MessageBox.Show(this, $"{message}\n\n{ex.Message}", "Suno Library", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static string ResolveLibraryRoot()
    {
        var argument = Environment.GetCommandLineArgs().Skip(1).FirstOrDefault(Directory.Exists);
        if (argument is not null) return Path.GetFullPath(argument);
        var executableDirectory = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        if (ContainsWorkflowMetadata(executableDirectory)) return executableDirectory;
        var current = Environment.CurrentDirectory;
        return ContainsWorkflowMetadata(current) ? current : executableDirectory;
    }

    private static bool ContainsWorkflowMetadata(string path)
    {
        try { return Directory.GetDirectories(path).Any(x => !Path.GetFileName(x).Equals("scrapper_db", StringComparison.OrdinalIgnoreCase) && Directory.EnumerateFiles(x, "*.txt", SearchOption.AllDirectories).Any()); }
        catch { return false; }
    }
}


