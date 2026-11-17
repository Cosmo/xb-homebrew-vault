#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XBVault.Services;
using Avalonia.Input;

namespace XBVault.ViewModels;

public partial class ScreenshotViewModel : ObservableObject, IDisposable
{
    private readonly IXboxSystemService _systemService;
    private CancellationTokenSource? _liveCts;

    public void Dispose()
    {
        _liveCts?.Cancel();
        _liveCts?.Dispose();
        GC.SuppressFinalize(this);
    }

    public ScreenshotViewModel(IXboxSystemService systemService)
    {
        _systemService = systemService;
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Cursor))]
    private bool _isCapturing;

    [ObservableProperty]
    private Bitmap? _screenshotImage;

    public bool HasScreenshot => ScreenshotImage is not null;

    public bool CanSaveAs => HasScreenshot && !IsLiveCapturing;

    partial void OnScreenshotImageChanged(Bitmap? value)
    {
        OnPropertyChanged(nameof(HasScreenshot));
        OnPropertyChanged(nameof(CanSaveAs));
    }

    /// <summary>
    /// When set, every Capture is also written to this folder. Null = saving is manual (Save As).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAutoSaveFolder))]
    [NotifyPropertyChangedFor(nameof(AutoSaveLabel))]
    private string? _autoSaveFolder;

    public bool HasAutoSaveFolder => !string.IsNullOrEmpty(AutoSaveFolder);

    public string AutoSaveLabel
    {
        get
        {
            if (!HasAutoSaveFolder) return "Auto-Save To...";
            var name = Path.GetFileName(Path.TrimEndingDirectorySeparator(AutoSaveFolder!));
            return $"Auto-Save: {(name.Length > 0 ? name : AutoSaveFolder)}";
        }
    }

    /// <summary>Shows a folder picker and returns the chosen path, or null when cancelled.</summary>
    public Func<Task<string?>>? PickFolderDialog { get; set; }

    /// <summary>Raised when the folder is set, changed or cleared, so the host can persist it.</summary>
    public Action<string?>? AutoSaveFolderChanged { get; set; }

    partial void OnAutoSaveFolderChanged(string? value) => AutoSaveFolderChanged?.Invoke(value);

    public enum StatusSeverity { None, Success, Error, Info }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    [NotifyPropertyChangedFor(nameof(IsStatusSuccess))]
    [NotifyPropertyChangedFor(nameof(IsStatusError))]
    [NotifyPropertyChangedFor(nameof(IsStatusInfo))]
    [NotifyPropertyChangedFor(nameof(StatusIconPath))]
    [NotifyPropertyChangedFor(nameof(StatusForeground))]
    [NotifyPropertyChangedFor(nameof(StatusBackground))]
    [NotifyPropertyChangedFor(nameof(StatusBorderBrush))]
    private StatusSeverity _statusType;

    [ObservableProperty]
    private string? _statusMessage;

    public bool HasStatus => StatusType != StatusSeverity.None;
    public bool IsStatusSuccess => StatusType == StatusSeverity.Success;
    public bool IsStatusError => StatusType == StatusSeverity.Error;
    public bool IsStatusInfo => StatusType == StatusSeverity.Info;

    public string StatusIconPath => StatusType switch
    {
        StatusSeverity.Success => "avares://XBVault/Assets/Views/FileExplorerView/fileexplorer-status-success-20.png",
        StatusSeverity.Error => "avares://XBVault/Assets/Views/FileExplorerView/fileexplorer-status-error-20.png",
        StatusSeverity.Info => "avares://XBVault/Assets/Views/FileExplorerView/fileexplorer-status-info-20.png",
        _ => string.Empty
    };

    public string StatusForeground => StatusType switch
    {
        StatusSeverity.Success => "#55FF55",
        StatusSeverity.Error => "#FF5555",
        StatusSeverity.Info => "#3399FF",
        _ => "Transparent"
    };

    public string StatusBackground => StatusType switch
    {
        StatusSeverity.Success => "#3355FF55",
        StatusSeverity.Error => "#33FF5555",
        StatusSeverity.Info => "#333399FF",
        _ => "Transparent"
    };

    public string StatusBorderBrush => StatusType switch
    {
        StatusSeverity.Success => "#5555FF55",
        StatusSeverity.Error => "#55FF5555",
        StatusSeverity.Info => "#553399FF",
        _ => "Transparent"
    };

    public List<string> IntervalOptions { get; } =
        ["0.8s", "1.0s", "1.5s", "2.0s", "2.5s", "3.0s", "3.5s", "4.0s", "4.5s", "5.0s"];

    [ObservableProperty]
    private string _selectedInterval = "1.0s";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(LiveButtonText))]
    [NotifyPropertyChangedFor(nameof(Cursor))]
    [NotifyPropertyChangedFor(nameof(CanSaveAs))]
    private bool _isLiveCapturing;

    public Cursor? Cursor => (IsCapturing || IsLiveCapturing) ? AppStartingCursor : null;

    private static readonly Cursor AppStartingCursor = new(StandardCursorType.AppStarting);

    public string LiveButtonText => IsLiveCapturing ? "Stop Live" : "Start Live";

    public Func<Stream, Task<string?>>? SaveScreenshotDialog { get; set; }

    public void Cleanup()
    {
        _liveCts?.Cancel();
    }

    private async Task<byte[]?> CaptureAsync(CancellationToken ct)
    {
        return await _systemService.CaptureScreenshotAsync(ct);
    }

    [RelayCommand]
    private async Task ChooseFolderAsync()
    {
        if (PickFolderDialog is null) return;

        var folder = await PickFolderDialog();
        if (!string.IsNullOrEmpty(folder))
            AutoSaveFolder = folder;
    }

    [RelayCommand]
    private void ClearFolder() => AutoSaveFolder = null;

    // Milliseconds keep quick successive captures from colliding
    internal static string BuildFileName(DateTime now) => $"xbox-{now:yyyy-MM-dd_HH-mm-ss-fff}.png";

    internal async Task<string> WriteToFolderAsync(byte[] data)
    {
        Directory.CreateDirectory(AutoSaveFolder!);
        var path = Path.Combine(AutoSaveFolder!, BuildFileName(DateTime.Now));
        await File.WriteAllBytesAsync(path, data);
        return path;
    }

    private async Task AutoSaveAsync(byte[] data)
    {
        try
        {
            var path = await WriteToFolderAsync(data);
            StatusMessage = $"Saved {Path.GetFileName(path)}";
            StatusType = StatusSeverity.Success;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not save screenshot: {ex.Message}";
            StatusType = StatusSeverity.Error;
            Logger.Error(ex, "Auto-save screenshot failed");
        }
    }

    [RelayCommand]
    private async Task CaptureScreenshotAsync()
    {
        // During live: with an auto-save folder the frame is saved and live carries on;
        // without one, live stops so the captured frame stays on screen for Save As.
        if (IsLiveCapturing && !HasAutoSaveFolder)
            _liveCts?.Cancel();

        IsCapturing = true;
        StatusMessage = null;
        StatusType = StatusSeverity.None;

        var data = await _systemService.CaptureScreenshotAsync();
        if (data is null)
        {
            StatusMessage = "Screenshot not available — Xbox Dev Mode does not support this API";
            StatusType = StatusSeverity.Info;
            IsCapturing = false;
            return;
        }

        try
        {
            using var ms = new MemoryStream(data);
            var bitmap = new Bitmap(ms);

            await XBVault.Helpers.UIHelpers.RunOnUIAsync(() =>
            {
                ScreenshotImage?.Dispose();
                ScreenshotImage = bitmap;
                return Task.CompletedTask;
            });

            if (HasAutoSaveFolder)
                await AutoSaveAsync(data);
        }
        catch (Exception ex)
        {
            StatusMessage = "Screenshot failed";
            StatusType = StatusSeverity.Error;
            Logger.Error(ex, "CaptureScreenshot failed");
        }
        finally
        {
            IsCapturing = false;
        }
    }

    [RelayCommand]
    private void ToggleLiveCapture()
    {
        if (IsLiveCapturing)
        {
            _liveCts?.Cancel();
            return;
        }

        IsLiveCapturing = true;
        IsCapturing = false;
        StatusMessage = null;
        StatusType = StatusSeverity.None;

        _liveCts?.Dispose();
        _liveCts = new CancellationTokenSource();

        _ = RunLiveCaptureAsync(_liveCts.Token);
    }

    private async Task RunLiveCaptureAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                var msDelay = (int)(double.Parse(SelectedInterval.TrimEnd('s'), System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture) * 1000);
                await Task.Delay(msDelay, token);
                token.ThrowIfCancellationRequested();

                var data = await CaptureAsync(token);
                if (data is not null)
                {
                    try
                    {
                        using var ms = new MemoryStream(data);
                        var bitmap = new Bitmap(ms);

                        await XBVault.Helpers.UIHelpers.RunOnUIAsync(() =>
                        {
                            ScreenshotImage?.Dispose();
                            ScreenshotImage = bitmap;
                            return Task.CompletedTask;
                        });
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Live capture frame failed");
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            await XBVault.Helpers.UIHelpers.RunOnUIAsync(() =>
            {
                IsLiveCapturing = false;
                return Task.CompletedTask;
            });
        }
    }

    [RelayCommand]
    private async Task SaveScreenshotAsync()
    {
        if (ScreenshotImage is null) return;
        if (SaveScreenshotDialog is null) return;

        using var ms = new MemoryStream();
        ScreenshotImage.Save(ms);
        ms.Position = 0;

        var path = await SaveScreenshotDialog(ms);
        if (!string.IsNullOrWhiteSpace(path))
        {
            StatusMessage = $"Saved to {path}";
            StatusType = StatusSeverity.Success;
        }
    }
}
