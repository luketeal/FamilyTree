using FamilyTree.Application.Services;
using FamilyTree.Domain.Repositories;
using FamilyTree.Storage.Browser.Repositories;
using Microsoft.Extensions.DependencyInjection;

namespace FamilyTree.Storage.Browser;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers browser-backed storage. Swapping to a server would replace this
    /// one call with the equivalent from a FamilyTree.Storage.Api package and
    /// change nothing else (ADR-004).
    /// </summary>
    public static IServiceCollection AddBrowserStorage(this IServiceCollection services)
    {
        services.AddScoped<IndexedDbStore>();
        services.AddScoped<IPersonRepository, BrowserPersonRepository>();
        services.AddScoped<IBiologicalRelationshipRepository, BrowserBiologicalRelationshipRepository>();
        services.AddScoped<IAdoptiveRelationshipRepository, BrowserAdoptiveRelationshipRepository>();
        services.AddScoped<IMarriageRepository, BrowserMarriageRepository>();
        services.AddScoped<ITreeDataAdministration, BrowserTreeDataAdministration>();
        services.AddScoped<IBackupJournal, BrowserBackupJournal>();
        return services;
    }
}
