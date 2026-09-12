using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// Captures screenshots of the published site as a build artifact. These are
/// deliberately not assertions: they exist because locator assertions verify
/// that elements are present and wired up, not that the page looks right. A
/// broken stylesheet or a collapsed layout passes every assertion in this
/// suite while rendering unusably.
/// </summary>
[Collection(nameof(StaticSiteCollection))]
public class ScreenshotCapture(StaticSiteFixture fixture)
{
    private static string OutputDirectory =>
        Environment.GetEnvironmentVariable("FAMILYTREE_SCREENSHOT_DIR")
        ?? Path.Combine(Path.GetTempPath(), "familytree-screenshots");

    [Theory]
    [InlineData("desktop", 1440, 900)]
    [InlineData("mobile", 390, 844)]
    public async Task CaptureShell(string name, int width, int height)
    {
        Directory.CreateDirectory(OutputDirectory);

        var page = await fixture.Browser.NewPageAsync(new BrowserNewPageOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });

        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"shell-{name}.png"),
            FullPage = true,
        });
    }

    // The confirmation only exists in response to a click, so the shell capture
    // above never shows it. It is the last thing a user reads before an
    // irreversible wipe, which makes "is it actually legible" a real question
    // and not one any assertion in this suite answers.
    [Theory]
    [InlineData("desktop", 1440, 900)]
    [InlineData("mobile", 390, 844)]
    public async Task CaptureDestructiveConfirmation(string name, int width, int height)
    {
        Directory.CreateDirectory(OutputDirectory);

        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");

        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-confirm")).ToBeVisibleAsync();

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"confirm-{name}.png"),
            FullPage = true,
        });
    }

    // The person flow is the first screen a user actually fills in, and forms
    // are where a two-column grid on a phone goes wrong quietly.
    [Theory]
    [InlineData("desktop", 1440, 900)]
    [InlineData("mobile", 390, 844)]
    public async Task CapturePersonFlow(string name, int width, int height)
    {
        Directory.CreateDirectory(OutputDirectory);

        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await Assertions.Expect(page.GetByTestId("people-empty-state")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"people-empty-{name}.png"),
            FullPage = true,
        });

        await page.GotoAsync(fixture.BaseUrl + "people/add", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("input-first-name").FillAsync("Ada");
        await page.GetByTestId("input-last-name").FillAsync("Lovelace");
        await page.GetByTestId("input-birth-surname").FillAsync("Byron");
        // Every part of the date control filled in, because four controls and a
        // checkbox on one line is exactly the shape that wraps badly on a phone
        // and no assertion answers "does it look like one field".
        await page.GetByTestId("input-birth-year").FillAsync("1815");
        await page.GetByTestId("input-birth-year-month").SelectOptionAsync("12");
        await page.GetByTestId("input-birth-year-day").FillAsync("10");
        await page.GetByTestId("input-birth-place").FillAsync("London, England");
        await page.GetByTestId("input-death-year").FillAsync("1852");
        await page.GetByTestId("input-death-year-approx").CheckAsync();
        await page.GetByTestId("input-notes").FillAsync("Wrote the first algorithm intended for a machine.");
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"person-form-{name}.png"),
            FullPage = true,
        });

        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"person-profile-{name}.png"),
            FullPage = true,
        });

        // The popover is a desktop control. Below the 768px breakpoint the same
        // button goes to the full form, which the capture above already covers.
        if (width > 768)
        {
            await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
            await page.GetByTestId("add-person-button").ClickAsync();
            await Assertions.Expect(page.GetByTestId("quick-add")).ToBeVisibleAsync();
            await page.ScreenshotAsync(new PageScreenshotOptions
            {
                Path = Path.Combine(OutputDirectory, $"quick-add-{name}.png"),
                FullPage = true,
            });
        }
    }

    // The date control's error state, which only exists after an interaction and
    // is the row most likely to push a phone sideways: the message sits under
    // four controls that have already wrapped.
    [Theory]
    [InlineData("desktop", 1440, 900)]
    [InlineData("mobile", 390, 844)]
    public async Task CaptureDateError(string name, int width, int height)
    {
        Directory.CreateDirectory(OutputDirectory);

        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl + "people/add", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("input-birth-year").FillAsync("1815");
        await page.GetByTestId("input-birth-year-month").SelectOptionAsync("2");
        await page.GetByTestId("input-birth-year-day").FillAsync("31");
        await Assertions.Expect(page.GetByTestId("error-input-birth-year")).ToBeVisibleAsync();

        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"date-error-{name}.png"),
            FullPage = true,
        });
    }

    // Export and Import are new page shapes — cards, a radio group, a file
    // picker and a warning banner in the shell — and none of them has ever been
    // looked at on a phone. The import preview and the replace confirmation only
    // exist after an interaction, so nothing else in this file would show them.
    [Theory]
    [InlineData("desktop", 1440, 900)]
    [InlineData("mobile", 390, 844)]
    public async Task CaptureExportAndImport(string name, int width, int height)
    {
        Directory.CreateDirectory(OutputDirectory);

        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            AcceptDownloads = true,
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");

        // The reminder only renders once there is something to lose, so it never
        // appears in the shell capture above.
        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"backup-reminder-{name}.png"),
            FullPage = true,
        });

        await page.GotoAsync(fixture.BaseUrl + "export", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"export-{name}.png"),
            FullPage = true,
        });

        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await page.GetByTestId("export-download").ClickAsync();
        });
        await Assertions.Expect(page.GetByTestId("export-summary")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"export-done-{name}.png"),
            FullPage = true,
        });

        using var stream = await download.CreateReadStreamAsync();
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();

        await page.GotoAsync(fixture.BaseUrl + "import", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"import-empty-{name}.png"),
            FullPage = true,
        });

        await page.GetByTestId("import-file").SetInputFilesAsync(new FilePayload
        {
            Name = "familytree-backup.json",
            MimeType = "application/json",
            Buffer = System.Text.Encoding.UTF8.GetBytes(json),
        });
        await Assertions.Expect(page.GetByTestId("import-preview")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"import-preview-{name}.png"),
            FullPage = true,
        });

        // The last thing a user reads before replacing their tree, which makes
        // "is it actually legible" a real question that no assertion answers.
        await page.GetByTestId("resolution-overwrite").ClickAsync();
        await page.GetByTestId("import-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("import-confirm")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"import-confirm-{name}.png"),
            FullPage = true,
        });

        await page.GetByTestId("import-confirm-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("import-result")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"import-result-{name}.png"),
            FullPage = true,
        });
    }

    // The reminder's other wording. The never-backed-up copy is what the shell
    // capture above shows; this is the sentence a returning user actually reads,
    // and it is longer, so it is the one that wraps badly if anything does.
    [Theory]
    [InlineData("desktop", 1440, 900)]
    [InlineData("mobile", 390, 844)]
    public async Task CaptureBackupReminderAfterAnEdit(string name, int width, int height)
    {
        Directory.CreateDirectory(OutputDirectory);

        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
            AcceptDownloads = true,
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");

        await page.GotoAsync(fixture.BaseUrl + "export", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.RunAndWaitForDownloadAsync(async () =>
        {
            await page.GetByTestId("export-download").ClickAsync();
        });
        await Assertions.Expect(page.GetByTestId("export-summary")).ToBeVisibleAsync();

        // An edit through the ordinary path, which is what puts the tree out of
        // step with the file that was just written.
        await page.GotoAsync(fixture.BaseUrl + "people/add", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("input-first-name").FillAsync("Ada");
        await page.GetByTestId("input-last-name").FillAsync("Lovelace");
        await page.GetByTestId("save-person").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToBeVisibleAsync();

        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"backup-reminder-changed-{name}.png"),
            FullPage = true,
        });
    }

    // The relationship sections and the wizard. A profile with parents, children
    // and siblings is a much denser page than the one PR 4 shipped, and the
    // wizard is a modal with a search field — the shape that breaks at 390px.
    // Neither exists without an interaction, so nothing else here would show it.
    [Theory]
    [InlineData("desktop", 1440, 900)]
    [InlineData("mobile", 390, 844)]
    public async Task CaptureRelationships(string name, int width, int height)
    {
        Directory.CreateDirectory(OutputDirectory);

        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            ViewportSize = new ViewportSize { Width = width, Height = height },
        });
        var page = await context.NewPageAsync();

        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("10 people · 14 relationships");

        // Susan has two parents, a child, a full sibling and a half-sibling, so
        // every section has something in it at once.
        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByText("Susan Hartley").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("siblings-list")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"profile-relationships-{name}.png"),
            FullPage = true,
        });

        // Margaret's mother is the unidentified ancestor, so this is the only
        // profile where a phantom chip sits next to a genuinely empty slot.
        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByText("Margaret Whitfield").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("parents-list")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"profile-phantom-parent-{name}.png"),
            FullPage = true,
        });

        // Daniel has an empty parent slot, so the wizard opens from his profile.
        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByText("Daniel Whitfield").First.ClickAsync();
        await page.GetByTestId("add-biological-parent").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"relationship-wizard-person-{name}.png"),
            FullPage = true,
        });

        // The inline create form, which is the tallest the dialog ever gets and
        // the one most likely to run off the bottom of a phone.
        await page.GetByTestId("relationship-dialog-search-create").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog-search-create-form"))
            .ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"relationship-wizard-create-{name}.png"),
            FullPage = true,
        });

        await page.GetByTestId("relationship-dialog-search-create-cancel").ClickAsync();
        await page.GetByTestId("relationship-dialog-search-results")
            .GetByText("Vera Whitfield").First.ClickAsync();
        await page.GetByTestId("relationship-dialog-next").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog-summary")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"relationship-wizard-details-{name}.png"),
            FullPage = true,
        });

        await page.GetByTestId("relationship-dialog-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("relationship-dialog")).ToBeHiddenAsync();

        // The removal confirmation, which is the last thing read before a link
        // is severed and says in as many words that nobody is deleted.
        await page.Locator("[data-testid^='remove-parent-']").First.ClickAsync();
        await Assertions.Expect(page.GetByTestId("remove-link-modal")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new PageScreenshotOptions
        {
            Path = Path.Combine(OutputDirectory, $"remove-link-confirm-{name}.png"),
            FullPage = true,
        });
    }

    // The one appearance check worth asserting: if the design tokens fail to
    // resolve, every component silently falls back to browser defaults.
    [Fact]
    public async Task DesignTokens_ResolveOnTheDeployedStylesheet()
    {
        var page = await fixture.Browser.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        var paper = await page.EvaluateAsync<string>(
            "() => getComputedStyle(document.documentElement).getPropertyValue('--paper').trim()");

        Assert.Equal("#fbfaf7", paper, ignoreCase: true);
    }
}
