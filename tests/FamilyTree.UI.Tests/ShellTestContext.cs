using Bunit;
using FamilyTree.Application.Services;
using FamilyTree.Domain.Repositories;
using FamilyTree.UI.Shared;
using Microsoft.Extensions.DependencyInjection;
using Moq;

namespace FamilyTree.UI.Tests;

/// <summary>
/// Base context for shell component tests, which now pull services for the live
/// tree counts and the storage-persistence request.
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

    protected ShellTestContext()
    {
        var biological = new Mock<IBiologicalRelationshipRepository>();
        var adoptive = new Mock<IAdoptiveRelationshipRepository>();
        var marriages = new Mock<IMarriageRepository>();

        People.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        biological.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        adoptive.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
        marriages.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);

        JSInterop.Mode = JSRuntimeMode.Loose;

        Services.AddSingleton(People.Object);
        Services.AddSingleton(biological.Object);
        Services.AddSingleton(adoptive.Object);
        Services.AddSingleton(marriages.Object);
        Services.AddSingleton<TreeStatsService>();
        Services.AddSingleton<TreeDataNotifier>();
        // The top bar hosts the quick-add popover, and the layout hosts toasts,
        // so shell tests now need both even when asserting only on markup.
        Services.AddSingleton<PersonService>();
        Services.AddSingleton<ToastService>();
        // A double rather than the real interop: the overlays only ask it to pin
        // the page, and the JS module behind it is covered end to end in
        // FamilyTree.E2E.Tests.
        Services.AddSingleton(new Mock<IOverlayInterop>().Object);
        // A no-op double: the layout only asks whether storage is durable, and
        // the real IndexedDB call is covered end to end in FamilyTree.E2E.Tests.
        var treeData = new Mock<ITreeDataAdministration>();
        treeData.Setup(t => t.EnsureDurableAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new StorageDurability(true, true));
        Services.AddSingleton(treeData.Object);
    }
}
