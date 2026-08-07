using Bunit;
using FamilyTree.Application.Common;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Repositories;
using FamilyTree.UI.Shared;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FamilyTree.UI.Tests;

/// <summary>
/// Base context for shell component tests, which now pull services for the live
/// tree counts, the storage-persistence request, and export and import.
/// </summary>
/// <remarks>
/// Repositories are mocked at the domain interface, so these stay unit tests:
/// no IndexedDB, no browser. JS interop runs in loose mode because the shell
/// calls into a JS module on first render and this suite is asserting markup,
/// not interop — the real calls are covered end to end in FamilyTree.E2E.Tests.
/// </remarks>
public abstract class ShellTestContext : BunitContext
{
    protected Mock<IPersonRepository> People { get; } = new();

    protected Mock<IBiologicalRelationshipRepository> Biological { get; } = new();

    protected Mock<IAdoptiveRelationshipRepository> Adoptive { get; } = new();

    protected Mock<IMarriageRepository> Marriages { get; } = new();

    protected Mock<ITreeDataAdministration> TreeData { get; } = new();

    protected StubBackupJournal Backups { get; } = new();

    protected RecordingDownloads Downloads { get; } = new();

    /// <summary>Fixed, so "three days since the last backup" is a fact rather than a wait.</summary>
    protected FixedClock Clock { get; } = new(new DateTimeOffset(2026, 8, 7, 12, 0, 0, TimeSpan.Zero));

    protected ShellTestContext()
    {
        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Biological.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Adoptive.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        Marriages.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(People.Object);
        Services.AddSingleton(Biological.Object);
        Services.AddSingleton(Adoptive.Object);
        Services.AddSingleton(Marriages.Object);
        Services.AddSingleton<TreeStatsService>();
        Services.AddSingleton<TreeDataNotifier>();
        // The top bar hosts the quick-add popover, and the layout hosts toasts,
        // so shell tests now need both even when asserting only on markup.
        Services.AddSingleton<PersonService>();
        Services.AddSingleton<ToastService>();
        Services.AddSingleton<ExportService>();
        Services.AddSingleton<ImportService>();
        Services.AddSingleton<TimeProvider>(Clock);
        Services.AddSingleton<IBackupJournal>(Backups);
        Services.AddSingleton<BackupTracker>();
        Services.AddSingleton<IFileDownloadInterop>(Downloads);
        // A double rather than the real interop: the overlays only ask it to pin
        // the page, and the JS module behind it is covered end to end in
        // FamilyTree.E2E.Tests.
        Services.AddSingleton(new Mock<IOverlayInterop>().Object);
        // Defaults to the roomy layout; tests that care set their own double.
        Services.AddSingleton(new Mock<IViewportInterop>().Object);
        // A no-op double: the layout only asks whether storage is durable, and
        // the real IndexedDB call is covered end to end in FamilyTree.E2E.Tests.
        TreeData.Setup(t => t.EnsureDurableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageDurability(true, true));
        Services.AddSingleton(TreeData.Object);
    }

    /// <summary>Records what the browser was asked to save, without a browser.</summary>
    protected sealed class RecordingDownloads : IFileDownloadInterop
    {
        public string? FileName { get; private set; }

        public string? Content { get; private set; }

        public int Count { get; private set; }

        public Task DownloadTextAsync(
            string fileName, string content, string mimeType = "application/json")
        {
            FileName = fileName;
            Content = content;
            Count++;
            return Task.CompletedTask;
        }
    }

    protected sealed class StubBackupJournal : IBackupJournal
    {
        public DateTimeOffset? LastExport { get; set; }

        public DateTimeOffset? LastChange { get; set; }

        public Task<BackupState> ReadAsync(CancellationToken ct = default) =>
            Task.FromResult(new BackupState(LastExport, LastChange));

        public Task RecordExportAsync(DateTimeOffset moment, CancellationToken ct = default)
        {
            LastExport = moment;
            return Task.CompletedTask;
        }

        public Task RecordChangeAsync(DateTimeOffset moment, CancellationToken ct = default)
        {
            LastChange = moment;
            return Task.CompletedTask;
        }
    }

    protected sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }
}
