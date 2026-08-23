using System.Text;
using System.Text.Json;
using Microsoft.Playwright;

namespace FamilyTree.E2E.Tests;

/// <summary>
/// The durability mechanism, end to end in a real browser.
/// </summary>
/// <remarks>
/// None of this can be a unit test. The download is browser machinery, the file
/// picker is browser machinery, and the property that matters — a tree written
/// to a file comes back out of it after the database is wiped — spans both plus
/// IndexedDB in between.
/// </remarks>
[Collection(nameof(StaticSiteCollection))]
public class ExportImportTests(StaticSiteFixture fixture)
{
    // The sample family holds 11 people, one of whom is an unidentified
    // ancestor, and 14 relationships, one of which is a link to her. The
    // placeholder rides in its own section of the file and the link with it, so
    // a restored tree is the same size as the one exported. It used to come back
    // with 13 — that gap is ADR-007's, closed in PR 7, and this is where a
    // regression would show up first.
    private const string SampleStats = "10 people · 14 relationships";
    private const string RestoredStats = SampleStats;

    private async Task<IPage> OpenAsync(string path = "settings")
    {
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            AcceptDownloads = true,
        });
        var page = await context.NewPageAsync();
        await page.GotoAsync(fixture.BaseUrl + path, new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        return page;
    }

    private static async Task LoadSampleAsync(IPage page)
    {
        await page.GetByTestId("load-sample").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats")).ToHaveTextAsync(SampleStats);
    }

    /// <summary>Clicks Download on the export page and returns what the browser saved.</summary>
    private async Task<(string FileName, string Json)> DownloadAsync(IPage page)
    {
        if (!page.Url.EndsWith("export", StringComparison.Ordinal))
        {
            await page.GotoAsync(fixture.BaseUrl + "export", new PageGotoOptions
            {
                WaitUntil = WaitUntilState.NetworkIdle,
            });
        }

        var download = await page.RunAndWaitForDownloadAsync(async () =>
        {
            await page.GetByTestId("export-download").ClickAsync();
        });
        await Assertions.Expect(page.GetByTestId("export-summary")).ToBeVisibleAsync();

        using var stream = await download.CreateReadStreamAsync();
        using var reader = new StreamReader(stream);
        return (download.SuggestedFilename, await reader.ReadToEndAsync());
    }

    private static async Task ChooseFileAsync(IPage page, string json)
    {
        await page.GetByTestId("import-file").SetInputFilesAsync(new FilePayload
        {
            Name = "familytree-backup.json",
            MimeType = "application/json",
            Buffer = Encoding.UTF8.GetBytes(json),
        });
        await Assertions.Expect(page.GetByTestId("import-preview")).ToBeVisibleAsync();
    }

    // ---- Export ----

    [Fact]
    public async Task TheBrowserSavesAFileWhenTheTreeIsExported()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);

        var (fileName, json) = await DownloadAsync(page);

        Assert.Matches(@"^familytree-\d{4}-\d{2}-\d{2}\.json$", fileName);
        Assert.False(string.IsNullOrWhiteSpace(json));
    }

    [Fact]
    public async Task TheExportedFileCarriesTheSchemaStamp()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);

        var (_, json) = await DownloadAsync(page);

        using var document = JsonDocument.Parse(json);
        Assert.Equal(2, document.RootElement.GetProperty("schemaVersion").GetInt32());
        Assert.Equal("FamilyTree", document.RootElement.GetProperty("application").GetString());
    }

    [Fact]
    public async Task TheExportedFileContainsTheWholeSeededFamily()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);

        var (_, json) = await DownloadAsync(page);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        // 10 rather than 11: the unidentified ancestor is a person nobody can
        // name, so she is not in `people` — she is in `phantoms`, and the
        // biological link naming her is in the file with the other eight.
        Assert.Equal(10, root.GetProperty("people").GetArrayLength());
        Assert.Equal(1, root.GetProperty("phantoms").GetArrayLength());
        Assert.Equal(9, root.GetProperty("biologicalLinks").GetArrayLength());
        Assert.Equal(2, root.GetProperty("adoptiveLinks").GetArrayLength());
        Assert.Equal(3, root.GetProperty("marriages").GetArrayLength());
    }

    // The precision that PartialDate exists to carry has now survived IndexedDB,
    // the record mapping and the file format.
    [Fact]
    public async Task TheExportedFileKeepsDatePrecision()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);

        var (_, json) = await DownloadAsync(page);

        using var document = JsonDocument.Parse(json);
        var arthur = document.RootElement.GetProperty("people").EnumerateArray()
            .First(p => p.GetProperty("firstName").GetString() == "Arthur");
        var birth = arthur.GetProperty("birthDate");

        Assert.Equal(1918, birth.GetProperty("year").GetInt32());
        Assert.False(birth.TryGetProperty("month", out _));
    }

    [Fact]
    public async Task ExportingAnEmptyTreeIsRefusedRatherThanSavingAnEmptyFile()
    {
        var page = await OpenAsync("export");

        await page.GetByTestId("export-download").ClickAsync();

        await Assertions.Expect(page.GetByTestId("export-error"))
            .ToContainTextAsync("nothing to export");
    }

    // ---- The property the whole PR exists for ----

    [Fact]
    public async Task AWipedTreeCanBeRestoredFromItsExport()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        var (_, json) = await DownloadAsync(page);

        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("clear-data").ClickAsync();
        await page.GetByTestId("confirm-destructive").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("0 people · 0 relationships");

        await page.GotoAsync(fixture.BaseUrl + "import", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await ChooseFileAsync(page, json);
        await page.GetByTestId("import-run").ClickAsync();

        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync(RestoredStats);
    }

    // The link that used to be lost, checked on the profile rather than in a
    // count: Margaret's unidentified mother comes back as a recorded parent, not
    // as a second empty slot.
    [Fact]
    public async Task AnUnidentifiedAncestorSurvivesTheRoundTrip()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        var (_, json) = await DownloadAsync(page);

        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("clear-data").ClickAsync();
        await page.GetByTestId("confirm-destructive").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("0 people · 0 relationships");

        await page.GotoAsync(fixture.BaseUrl + "import", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await ChooseFileAsync(page, json);
        await page.GetByTestId("import-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("import-result")).ToBeVisibleAsync();

        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByText("Margaret Whitfield (née Ellery)").ClickAsync();

        // One recorded parent — the placeholder — and therefore one empty slot,
        // rather than the two empty slots a lost link would leave.
        await Assertions.Expect(page.GetByTestId("parent-unknown-slot")).ToHaveCountAsync(1);
        await Assertions.Expect(page.GetByTestId("parents-list")).ToContainTextAsync("Unknown");
    }

    [Fact]
    public async Task RestoredPeopleKeepTheirDetails()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        var (_, json) = await DownloadAsync(page);

        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("clear-data").ClickAsync();
        await page.GetByTestId("confirm-destructive").ClickAsync();
        await Assertions.Expect(page.GetByTestId("settings-stats"))
            .ToHaveTextAsync("0 people · 0 relationships");

        await page.GotoAsync(fixture.BaseUrl + "import", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await ChooseFileAsync(page, json);
        await page.GetByTestId("import-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("import-result")).ToBeVisibleAsync();

        await page.GotoAsync(fixture.BaseUrl + "people", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        // The née name and the life span both come back, which means the birth
        // surname and both partial dates round-tripped.
        await Assertions.Expect(page.GetByTestId("people-list"))
            .ToContainTextAsync("Margaret Whitfield (née Ellery)");
        await Assertions.Expect(page.GetByTestId("people-list")).ToContainTextAsync("1921–2004");
    }

    // A restore that only lasted until the tab closed would not be a restore.
    [Fact]
    public async Task RestoredDataSurvivesAReload()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        var (_, json) = await DownloadAsync(page);

        await page.GotoAsync(fixture.BaseUrl + "import", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await ChooseFileAsync(page, json);
        await page.GetByTestId("resolution-overwrite").ClickAsync();
        await page.GetByTestId("import-run").ClickAsync();
        await page.GetByTestId("import-confirm-run").ClickAsync();
        await Assertions.Expect(page.GetByTestId("import-result")).ToBeVisibleAsync();

        await page.ReloadAsync(new PageReloadOptions { WaitUntil = WaitUntilState.NetworkIdle });

        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync(RestoredStats);
    }

    // Importing a backup on top of the tree it came from must be a no-op, not a
    // doubling. This is the mistake a worried user makes.
    [Fact]
    public async Task ImportingABackupOverItsOwnTreeChangesNothing()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        var (_, json) = await DownloadAsync(page);

        await page.GotoAsync(fixture.BaseUrl + "import", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await ChooseFileAsync(page, json);
        await page.GetByTestId("import-run").ClickAsync();

        // Still 14: Skip left the unidentified ancestor and her link untouched.
        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync(SampleStats);
    }

    // ---- The two steps that exist for engines this container cannot run ----

    // download.js does two things purely for Firefox and Safari: it puts the
    // anchor in the document before clicking it, and it defers revoking the
    // object URL by a turn of the event loop. Neither is engine-branched, so
    // Chromium exercises the same single code path — but Chromium does not
    // *need* either step, which means removing one leaves this suite green and
    // silently breaks the download for everybody not on Chromium.
    //
    // These assertions cannot confirm Gecko or WebKit behave as documented. What
    // they can do is make the workarounds impossible to delete quietly, which is
    // the failure this file can actually prevent.
    private static async Task<IPage> InstrumentedPageAsync(StaticSiteFixture fixture)
    {
        var context = await fixture.Browser.NewContextAsync(new BrowserNewContextOptions
        {
            AcceptDownloads = true,
        });

        // Init script rather than an evaluate, so the prototypes are patched
        // before the download module is ever imported.
        await context.AddInitScriptAsync(@"
            window.__download = {
                clicked: false,
                inDocumentAtClick: null,
                clickTurnEnded: false,
                revokedBeforeClick: false,
                revokedInClickTurn: false,
                revokedAfterClick: false,
            };

            const click = HTMLAnchorElement.prototype.click;
            HTMLAnchorElement.prototype.click = function () {
                if (this.hasAttribute('download')) {
                    window.__download.clicked = true;
                    window.__download.inDocumentAtClick = document.contains(this);
                    // Queued from inside the click's own turn, so it fires at the
                    // start of the next task. Anything that revokes before this
                    // has run did so without yielding, which is the thing Safari
                    // cannot survive. Queued before the real click so it is
                    // always ahead of any timer the caller sets afterwards.
                    setTimeout(() => { window.__download.clickTurnEnded = true; }, 0);
                }
                return click.apply(this, arguments);
            };

            const revoke = URL.revokeObjectURL;
            URL.revokeObjectURL = function (url) {
                if (window.__download.clicked) {
                    window.__download.revokedAfterClick = true;
                    if (!window.__download.clickTurnEnded) {
                        window.__download.revokedInClickTurn = true;
                    }
                } else {
                    window.__download.revokedBeforeClick = true;
                }
                return revoke.apply(this, arguments);
            };
        ");

        return await context.NewPageAsync();
    }

    [Fact]
    public async Task TheDownloadAnchorIsInTheDocumentWhenItIsClicked()
    {
        var page = await InstrumentedPageAsync(fixture);
        await page.GotoAsync(fixture.BaseUrl + "settings",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await LoadSampleAsync(page);
        await DownloadAsync(page);

        var clicked = await page.EvaluateAsync<bool>("() => window.__download.clicked");
        var inDocument = await page.EvaluateAsync<bool?>("() => window.__download.inDocumentAtClick");

        Assert.True(clicked, "No download anchor was clicked, so the probe did not observe the export.");
        Assert.True(inDocument,
            "The download anchor was clicked while detached from the document. Chromium tolerates "
            + "that; Firefox is documented not to, so the appendChild in download.js is load-bearing "
            + "for users this suite cannot reach.");
    }

    [Fact]
    public async Task TheObjectUrlOutlivesTheClickThatUsesIt()
    {
        var page = await InstrumentedPageAsync(fixture);
        await page.GotoAsync(fixture.BaseUrl + "settings",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await LoadSampleAsync(page);
        await DownloadAsync(page);

        // Asserted as "not in the same task as the click" rather than "not before
        // the click". A synchronous revoke happens after click() returns and so
        // passes the weaker check, while still being exactly the bug: Safari has
        // not read the blob by then, and revoking without yielding cancels the
        // download.
        var revokedInClickTurn = await page.EvaluateAsync<bool>(
            "() => window.__download.revokedInClickTurn");
        var revokedBefore = await page.EvaluateAsync<bool>(
            "() => window.__download.revokedBeforeClick");

        Assert.False(revokedBefore, "The object URL was revoked before the anchor was clicked.");
        Assert.False(revokedInClickTurn,
            "The object URL was revoked in the same task as the click, without yielding. Chromium "
            + "tolerates that; Safari has not read the blob yet and cancels the download — which is "
            + "why download.js defers the revoke by a turn of the event loop.");
    }

    // The deferral must not become a leak: the URL still has to be released.
    [Fact]
    public async Task TheObjectUrlIsReleasedAfterTheDownload()
    {
        var page = await InstrumentedPageAsync(fixture);
        await page.GotoAsync(fixture.BaseUrl + "settings",
            new PageGotoOptions { WaitUntil = WaitUntilState.NetworkIdle });
        await LoadSampleAsync(page);
        await DownloadAsync(page);

        // Polled rather than read once: the revoke is deliberately on a later
        // turn of the event loop, so a single read can race it.
        await page.WaitForFunctionAsync("() => window.__download.revokedAfterClick === true");
    }

    // ---- Refusing what cannot be read ----

    [Fact]
    public async Task AFileFromANewerVersionIsRefusedAndNothingIsWritten()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);

        await page.GotoAsync(fixture.BaseUrl + "import", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await page.GetByTestId("import-file").SetInputFilesAsync(new FilePayload
        {
            Name = "from-the-future.json",
            MimeType = "application/json",
            Buffer = Encoding.UTF8.GetBytes("""{"schemaVersion": 99, "people": []}"""),
        });

        await Assertions.Expect(page.GetByTestId("import-error")).ToContainTextAsync("newer version");
        await Assertions.Expect(page.GetByTestId("import-preview")).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync(SampleStats);
    }

    [Fact]
    public async Task AFileThatIsNotAnExportIsRefused()
    {
        var page = await OpenAsync("import");

        await page.GetByTestId("import-file").SetInputFilesAsync(new FilePayload
        {
            Name = "notes.txt",
            MimeType = "text/plain",
            Buffer = Encoding.UTF8.GetBytes("my grandmother was born in 1921"),
        });

        await Assertions.Expect(page.GetByTestId("import-error")).ToContainTextAsync("not readable");
    }

    // ---- The destructive path ----

    [Fact]
    public async Task ReplacingATreeAsksBeforeItDestroysAnything()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        var (_, json) = await DownloadAsync(page);

        await page.GotoAsync(fixture.BaseUrl + "import", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await ChooseFileAsync(page, json);
        await page.GetByTestId("resolution-overwrite").ClickAsync();
        await page.GetByTestId("import-run").ClickAsync();

        await Assertions.Expect(page.GetByTestId("import-confirm")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("import-confirm-text"))
            .ToContainTextAsync("10 people and 14 relationships");
        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync(SampleStats);
    }

    // role="alertdialog" claims the panel behaves like a dialog. bUnit can check
    // neither half of that: it has no focus model, and FocusAsync is stubbed
    // interop there.
    [Fact]
    public async Task TheReplaceConfirmationTakesFocusAndEscapeCancelsIt()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        var (_, json) = await DownloadAsync(page);

        await page.GotoAsync(fixture.BaseUrl + "import", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });
        await ChooseFileAsync(page, json);
        await page.GetByTestId("resolution-overwrite").ClickAsync();
        await page.GetByTestId("import-run").ClickAsync();

        await Assertions.Expect(page.GetByTestId("import-confirm")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Escape");

        await Assertions.Expect(page.GetByTestId("import-confirm")).ToBeHiddenAsync();
        await Assertions.Expect(page.GetByTestId("tree-stats")).ToHaveTextAsync(SampleStats);
    }

    // ---- The reminder ----

    [Fact]
    public async Task TheBackupReminderAppearsOnceThereIsSomethingToLose()
    {
        var page = await OpenAsync();

        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeHiddenAsync();

        await LoadSampleAsync(page);

        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("backup-reminder-text"))
            .ToContainTextAsync("never been backed up");
    }

    // The reminder is about backing up, and the export page is where that
    // happens: telling somebody to go where they already are is noise.
    //
    // Followed by clicking the reminder's own link rather than by reloading the
    // page, which is the whole point. A component that only recomputes on
    // startup renders correctly after a reload and then never changes again —
    // so the banner would follow the user onto the page it sent them to, and a
    // reload-based test would call that a pass.
    [Fact]
    public async Task TheBackupReminderTakesItselfOffScreenWhenYouFollowItsLink()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeVisibleAsync();

        await page.GetByTestId("backup-reminder-export").ClickAsync();

        await Assertions.Expect(page.GetByTestId("export-download")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeHiddenAsync();
    }

    // The reminder has to notice an export it did not perform itself, or it
    // keeps nagging somebody who has already done what it asked. Every step is
    // a client-side navigation, so nothing here is re-initialised by a reload.
    [Fact]
    public async Task TheBackupReminderGoesAwayAfterAnExport()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await page.GetByTestId("backup-reminder-export").ClickAsync();
        await Assertions.Expect(page.GetByTestId("export-download")).ToBeVisibleAsync();

        await DownloadAsync(page);

        await page.GetByTestId("nav-people").ClickAsync();
        await Assertions.Expect(page.GetByTestId("people-list")).ToBeVisibleAsync();

        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeHiddenAsync();
    }

    // The complaint the change-based trigger exists to answer: export, then enter
    // somebody new, and be told that the file no longer describes the tree. An
    // elapsed-time rule stays silent here for a week.
    //
    // Driven through the real add-a-person path rather than by back-dating a
    // timestamp, because the chain under test is mutation -> notifier -> journal
    // -> banner, and back-dating would skip the first two links of it.
    [Fact]
    public async Task TheBackupReminderComesBackWhenSomebodyIsAddedAfterAnExport()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await page.GetByTestId("backup-reminder-export").ClickAsync();
        await DownloadAsync(page);

        await page.GetByTestId("nav-people").ClickAsync();
        await Assertions.Expect(page.GetByTestId("people-list")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeHiddenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Ada");
        await page.GetByTestId("quick-last-name").FillAsync("Lovelace");
        await page.GetByTestId("quick-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Ada Lovelace");

        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeVisibleAsync();
        await Assertions.Expect(page.GetByTestId("backup-reminder-text"))
            .ToContainTextAsync("changes that are not in any backup");
    }

    // Dismissing acknowledges the changes the user was shown, not every change
    // they will ever make.
    [Fact]
    public async Task DismissingTheReminderDoesNotSilenceTheNextChange()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeVisibleAsync();

        await page.GetByTestId("backup-reminder-dismiss").ClickAsync();
        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeHiddenAsync();

        await page.GetByTestId("add-person-button").ClickAsync();
        await page.GetByTestId("quick-first-name").FillAsync("Ada");
        await page.GetByTestId("quick-last-name").FillAsync("Lovelace");
        await page.GetByTestId("quick-save").ClickAsync();
        await Assertions.Expect(page.GetByTestId("profile-name")).ToHaveTextAsync("Ada Lovelace");

        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeVisibleAsync();
    }

    // The one thing the user is left with afterwards, so it has to be right.
    [Fact]
    public async Task SettingsReportsWhenTheTreeWasLastExported()
    {
        var page = await OpenAsync();
        await LoadSampleAsync(page);
        await Assertions.Expect(page.GetByTestId("settings-backup")).ToHaveTextAsync("Never exported");

        await DownloadAsync(page);

        await page.GotoAsync(fixture.BaseUrl + "settings", new PageGotoOptions
        {
            WaitUntil = WaitUntilState.NetworkIdle,
        });

        await Assertions.Expect(page.GetByTestId("settings-backup")).ToHaveTextAsync("Last exported today");
        await Assertions.Expect(page.GetByTestId("backup-reminder")).ToBeHiddenAsync();
    }
}
