using System.Diagnostics;
using System.Net;
using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Publishes the WASM app once per test run and serves it the way GitHub Pages
/// will: from a repository subpath, with unknown paths falling back to
/// <c>404.html</c>. Testing against the published output rather than a dev
/// server means the deployment shape itself — rewritten base href, SPA
/// fallback, static asset paths — is covered, not just the components.
/// </summary>
public sealed class StaticSiteFixture : IAsyncLifetime
{
    private const string BasePath = "/FamilyTree/";

    private HttpListener? _listener;
    private CancellationTokenSource? _cts;
    private string _rootDirectory = string.Empty;
    private IPlaywright? _playwright;

    public IBrowser Browser { get; private set; } = null!;

    public string BaseUrl { get; private set; } = string.Empty;

    /// <summary>Published wwwroot on disk, for tests that inspect build output.</summary>
    public string PublishedRoot => _rootDirectory;

    public async Task InitializeAsync()
    {
        _rootDirectory = PublishApp();
        ApplyGitHubPagesTransforms(_rootDirectory);

        var port = GetFreePort();
        BaseUrl = $"http://127.0.0.1:{port}{BasePath}";

        _cts = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        _ = Task.Run(() => ServeAsync(_cts.Token));

        _playwright = await Playwright.CreateAsync();
        Browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
        {
            Headless = true,
            // The container pre-provisions Chromium; fall back to Playwright's
            // own download location when it is absent.
            ExecutablePath = File.Exists("/opt/pw-browsers/chromium")
                ? "/opt/pw-browsers/chromium"
                : null,
        });
    }

    public async Task DisposeAsync()
    {
        _cts?.Cancel();
        _listener?.Stop();
        _listener?.Close();

        if (Browser is not null)
        {
            await Browser.CloseAsync();
        }

        _playwright?.Dispose();
    }

    private static string PublishApp()
    {
        var repoRoot = FindRepositoryRoot();

        // Unique per run: a fixed path means two concurrent runs delete each
        // other's publish output mid-test.
        var output = Path.Combine(
            Path.GetTempPath(),
            $"familytree-e2e-publish-{Guid.NewGuid():N}");

        var project = Path.Combine(repoRoot, "src", "FamilyTree.App", "FamilyTree.App.csproj");
        var psi = new ProcessStartInfo("dotnet")
        {
            ArgumentList = { "publish", project, "-c", "Release", "-o", output, "--nologo", "-v", "q" },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start dotnet publish.");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet publish failed:\n{stdout}\n{stderr}");
        }

        return Path.Combine(output, "wwwroot");
    }

    /// <summary>
    /// Mirrors the deploy workflow: rewrite the base href to the repository
    /// subpath and copy index.html to 404.html for SPA deep-link fallback.
    /// </summary>
    private static void ApplyGitHubPagesTransforms(string root)
    {
        var indexPath = Path.Combine(root, "index.html");
        var original = File.ReadAllText(indexPath);
        var html = original.Replace("<base href=\"/\" />", $"<base href=\"{BasePath}\" />");

        // string.Replace silently no-ops when the pattern is absent, which would
        // leave the suite testing a root-hosted site while claiming to cover the
        // subpath deployment. The deploy workflow guards its equivalent sed with
        // grep -q; this is the same guard.
        if (ReferenceEquals(html, original) || html == original)
        {
            throw new InvalidOperationException(
                "base href rewrite matched nothing — the published index.html shape changed. " +
                "Update both this fixture and the sed in .github/workflows/deploy.yml.");
        }

        File.WriteAllText(indexPath, html);
        File.WriteAllText(Path.Combine(root, "404.html"), html);
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "FamilyTree.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }

    private static int GetFreePort()
    {
        using var socket = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        socket.Start();
        var port = ((IPEndPoint)socket.LocalEndpoint).Port;
        socket.Stop();
        return port;
    }

    private async Task ServeAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _listener!.IsListening)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (HttpListenerException)
            {
                return;
            }
            catch (ObjectDisposedException)
            {
                return;
            }

            try
            {
                await WriteResponseAsync(context);
            }
            catch (HttpListenerException)
            {
                // Client disconnected mid-response; nothing to recover.
            }
            catch (Exception ex)
            {
                // Anything escaping here would otherwise unwind the accept loop
                // and kill the server for the rest of the run, so every later
                // test fails with an opaque navigation timeout instead of this.
                Console.Error.WriteLine($"StaticSiteFixture failed to serve a request: {ex}");
                try
                {
                    context.Response.StatusCode = 500;
                }
                catch (ObjectDisposedException)
                {
                    // Response already closed; nothing further to report.
                }
            }
            finally
            {
                context.Response.Close();
            }
        }
    }

    private async Task WriteResponseAsync(HttpListenerContext context)
    {
        var path = context.Request.Url!.AbsolutePath;

        // Anything outside the repository subpath is a 404 on GitHub Pages.
        if (!path.StartsWith(BasePath, StringComparison.Ordinal))
        {
            context.Response.StatusCode = 404;
            return;
        }

        var relative = path[BasePath.Length..];
        if (relative.Length == 0)
        {
            relative = "index.html";
        }

        var file = Path.Combine(_rootDirectory, relative.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(file))
        {
            // GitHub Pages serves 404.html for unknown paths, which is how deep
            // links into a client-routed SPA resolve.
            file = Path.Combine(_rootDirectory, "404.html");
            context.Response.StatusCode = 200;
        }

        context.Response.ContentType = ContentTypeFor(Path.GetExtension(file));
        var bytes = await File.ReadAllBytesAsync(file);
        context.Response.ContentLength64 = bytes.Length;
        await context.Response.OutputStream.WriteAsync(bytes);
    }

    private static string ContentTypeFor(string extension) => extension switch
    {
        ".html" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "text/javascript; charset=utf-8",
        ".json" => "application/json; charset=utf-8",
        ".wasm" => "application/wasm",
        ".png" => "image/png",
        ".dat" or ".blat" => "application/octet-stream",
        ".woff2" => "font/woff2",
        _ => "application/octet-stream",
    };
}

[CollectionDefinition(nameof(StaticSiteCollection))]
public sealed class StaticSiteCollection : ICollectionFixture<StaticSiteFixture>;
