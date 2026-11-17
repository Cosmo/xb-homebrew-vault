#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XBVault.Models;
using XBVault.Services;
using XBVault.ViewModels;
using Xunit;

namespace XBVault.Tests;

public class ScreenshotViewModelTests
{
    private sealed class FakeSystem : IXboxSystemService
    {
        public int CaptureCalls { get; private set; }

        public Task<byte[]?> CaptureScreenshotAsync(CancellationToken ct = default)
        {
            CaptureCalls++;
            return Task.FromResult<byte[]?>(null);
        }

        public Task<string?> GetSystemInfoAsync() => Task.FromResult<string?>(null);
        public Task<ConsoleInfo?> GetConsoleInfoAsync() => Task.FromResult<ConsoleInfo?>(null);
        public Task<string?> GetMachineNameAsync() => Task.FromResult<string?>(null);
        public Task<IReadOnlyList<XboxSetting>> GetXboxSettingsAsync() => Task.FromResult<IReadOnlyList<XboxSetting>>([]);
        public Task<string?> GetCrashDumpsAsync() => Task.FromResult<string?>(null);
        public Task<bool> DeleteCrashDumpAsync(string filename) => Task.FromResult(true);
        public Task<string?> GetCrashControlAsync() => Task.FromResult<string?>(null);
        public Task<bool> SetCrashControlAsync(bool enabled) => Task.FromResult(true);
        public Task<bool> RestartXboxAsync() => Task.FromResult(true);
        public Task<bool> ShutdownXboxAsync() => Task.FromResult(true);
    }

    [Fact]
    public void AutoSaveLabel_ShowsTheFolderNameOnceSet()
    {
        var vm = new ScreenshotViewModel(new FakeSystem());
        Assert.False(vm.HasAutoSaveFolder);
        Assert.Equal("Auto-Save To...", vm.AutoSaveLabel);

        vm.AutoSaveFolder = Path.Combine(Path.GetTempPath(), "XBVault");
        Assert.True(vm.HasAutoSaveFolder);
        Assert.Equal("Auto-Save: XBVault", vm.AutoSaveLabel);
    }

    [Fact]
    public void BuildFileName_IsTimestampedToTheMillisecond()
    {
        var name = ScreenshotViewModel.BuildFileName(new DateTime(2026, 11, 17, 18, 42, 7, 314));
        Assert.Equal("xbox-2026-11-17_18-42-07-314.png", name);
    }

    [Fact]
    public async Task WriteToFolder_CreatesFolderAndWritesTheCapturedBytes()
    {
        var folder = Path.Combine(Path.GetTempPath(), "xbvault-tests-" + Guid.NewGuid().ToString("N"), "shots");
        try
        {
            var vm = new ScreenshotViewModel(new FakeSystem()) { AutoSaveFolder = folder };
            byte[] data = [1, 2, 3, 4];

            var path = await vm.WriteToFolderAsync(data);

            Assert.Equal(folder, Path.GetDirectoryName(path));
            Assert.Equal(data, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(Path.GetDirectoryName(folder)!, true);
        }
    }

    [Fact]
    public async Task ChooseAndClearFolder_UpdateTheFolder_AndNotifyTheHost()
    {
        var notified = new List<string?>();
        var vm = new ScreenshotViewModel(new FakeSystem())
        {
            PickFolderDialog = () => Task.FromResult<string?>("/picked"),
            AutoSaveFolderChanged = notified.Add
        };

        await vm.ChooseFolderCommand.ExecuteAsync(null);
        Assert.Equal("/picked", vm.AutoSaveFolder);

        vm.ClearFolderCommand.Execute(null);
        Assert.Null(vm.AutoSaveFolder);

        Assert.Equal(["/picked", null], notified);
    }

    [Fact]
    public async Task ChooseFolder_KeepsTheCurrentFolderWhenThePickerIsCancelled()
    {
        var vm = new ScreenshotViewModel(new FakeSystem())
        {
            AutoSaveFolder = "/current",
            PickFolderDialog = () => Task.FromResult<string?>(null)
        };

        await vm.ChooseFolderCommand.ExecuteAsync(null);

        Assert.Equal("/current", vm.AutoSaveFolder);
    }
}
