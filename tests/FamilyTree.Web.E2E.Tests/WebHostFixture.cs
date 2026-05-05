using FamilyTree.Domain.Repositories;
using FamilyTree.Infrastructure.Persistence;
using FamilyTree.Infrastructure.Persistence.Repositories;
using FamilyTree.Web.Components;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Playwright;

namespace FamilyTree.Web.E2E.Tests;

/// <summary>
/// xUnit collection fixture that boots the FamilyTree Blazor Server app on a random
/// localhost port against a fresh per-fixture SQLite file, then launches a single
/// headless Chromium browser. Tests create their own BrowserContext for isolation.
/// </summary>
public sealed class WebHostFixture : IAsyncLifetime
{
    private WebApplication? _app;
    private string? _tempDir;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;
    public string BaseUrl { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        EnsurePlaywrightBrowsersPath();

        _tempDir = Path.Combine(Path.GetTempPath(), "familytree-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var dbPath = Path.Combine(_tempDir, "familytree-e2e.db");
        var contentRoot = LocateWebContentRoot();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = contentRoot,
            WebRootPath = Path.Combine(contentRoot, "wwwroot"),
            EnvironmentName = "Development"
        });

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddDbContext<FamilyTreeDbContext>(opts => opts.UseSqlite($"Data Source={dbPath}"));
        builder.Services.AddScoped<IPersonRepository, PersonRepository>();
        builder.Services.AddScoped<IBiologicalRelationshipRepository, BiologicalRelationshipRepository>();
        builder.Services.AddScoped<IAdoptiveRelationshipRepository, AdoptiveRelationshipRepository>();
        builder.Services.AddScoped<IMarriageRepository, MarriageRepository>();

        var app = builder.Build();
        app.Urls.Clear();
        app.Urls.Add("http://127.0.0.1:0");

        using (var scope = app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<FamilyTreeDbContext>();
            db.Database.Migrate();
        }

        app.UseStaticFiles();
        app.UseAntiforgery();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode();

        await app.StartAsync();
        _app = app;

        var server = app.Services.GetRequiredService<IServer>();
        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses.FirstOrDefault()
            ?? throw new InvalidOperationException("Could not determine bound address.");
        BaseUrl = address;

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task DisposeAsync()
    {
        if (Browser is not null) await Browser.CloseAsync();
        _playwright?.Dispose();

        if (_app is not null)
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        if (_tempDir is not null && Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { /* best-effort cleanup */ }
        }
    }

    /// <summary>
    /// If PLAYWRIGHT_BROWSERS_PATH isn't already set, point it at any pre-provisioned
    /// browser cache so dev environments and CI don't need to download Chromium per run.
    /// </summary>
    private static void EnsurePlaywrightBrowsersPath()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH"))) return;

        foreach (var candidate in new[] { "/opt/pw-browsers", "/ms-playwright" })
        {
            if (Directory.Exists(candidate))
            {
                Environment.SetEnvironmentVariable("PLAYWRIGHT_BROWSERS_PATH", candidate);
                return;
            }
        }
    }

    /// <summary>
    /// Walks up from the test bin directory to locate src/FamilyTree.Web so that
    /// static assets and Razor components resolve correctly when running under dotnet test.
    /// </summary>
    private static string LocateWebContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "FamilyTree.Web");
            if (Directory.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Unable to locate src/FamilyTree.Web from " + AppContext.BaseDirectory);
    }
}

[CollectionDefinition(nameof(WebHostCollection))]
public sealed class WebHostCollection : ICollectionFixture<WebHostFixture> { }
