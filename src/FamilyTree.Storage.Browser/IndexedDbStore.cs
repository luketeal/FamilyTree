using System.Text.Json;
using FamilyTree.Application.Common;
using FamilyTree.Storage.Browser.Records;
using Microsoft.JSInterop;

namespace FamilyTree.Storage.Browser;

/// <summary>
/// Thin typed wrapper over the IndexedDB JavaScript module. The repositories
/// speak in records; this speaks in stores and keys.
/// </summary>
public sealed class IndexedDbStore(IJSRuntime js) : IAsyncDisposable
{
    // Bumped when a persisted record shape changes. Written alongside the data
    // so that a future version can tell which shape it is reading before it
    // tries. ImportService is the first thing to read it, since a file is the
    // first payload this app did not write itself.
    //
    // Shared with the export format rather than duplicated: the stamp describes
    // the shape of a person and a link, and a store that reshaped one without
    // the other would write files that lie about their own contents.
    public const int SchemaVersion = TreeSchema.Version;

    public const string People = "people";
    public const string BiologicalLinks = "biologicalLinks";
    public const string AdoptiveLinks = "adoptiveLinks";
    public const string Marriages = "marriages";
    public const string StepparentLinks = "stepparentLinks";
    private const string Meta = "meta";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private IJSObjectReference? _module;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>(
            "import", "./_content/FamilyTree.Storage.Browser/familytree-store.js");

    public async Task<IReadOnlyList<T>> GetAllAsync<T>(string store, CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<T[]>("getAll", ct, store);
    }

    public async Task<T?> GetAsync<T>(string store, Guid id, CancellationToken ct = default)
        where T : class
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<T?>("get", ct, store, id);
    }

    /// <summary>Several records by key, in one call and one transaction.</summary>
    public async Task<IReadOnlyList<T>> GetManyAsync<T>(
        string store, IReadOnlyCollection<Guid> ids, CancellationToken ct = default)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var module = await ModuleAsync();
        return await module.InvokeAsync<T[]>("getMany", ct, store, ids);
    }

    public async Task PutAsync<T>(string store, T record, CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("put", ct, store, record);
    }

    public async Task DeleteAsync(string store, Guid id, CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("remove", ct, store, id);
    }

    /// <summary>
    /// Removes one record and writes another in a single transaction.
    /// </summary>
    /// <remarks>
    /// The atomic half of the repository's replace: as two calls it can
    /// half-apply, leaving the old record gone and the new one never written.
    /// </remarks>
    public async Task ReplaceAsync<T>(string store, Guid deleteId, T record, CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("replaceOne", ct, store, deleteId, record);
    }

    /// <summary>
    /// Removes a record together with the dependents pointing at it, optionally
    /// writing a replacement, in one transaction.
    /// </summary>
    /// <remarks>
    /// Deleting a marriage has to take its stepparent labels with it (US-038),
    /// and replacing a spouse has to do the same while writing the new record.
    /// Either as two calls can half-apply and leave a label naming a marriage
    /// that is gone.
    /// </remarks>
    /// <returns>How many dependent records went with it.</returns>
    public async Task<int> DeleteWithDependentsAsync<T>(
        string store,
        Guid id,
        string dependentStore,
        string dependentKey,
        T? replacement = null,
        CancellationToken ct = default)
        where T : class
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<int>(
            "removeWithDependents", ct, store, id, dependentStore, dependentKey, replacement);
    }

    /// <summary>
    /// Removes one record, reporting whether it existed.
    /// </summary>
    /// <remarks>
    /// The answer comes from the same transaction as the delete, so a caller can
    /// tell "removed" from "was not there" without a read of its own — which for
    /// the stepparent labels meant reading the entire store to check one id.
    /// </remarks>
    public async Task<bool> DeleteIfPresentAsync(
        string store, Guid id, CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<bool>("removeIfPresent", ct, store, id);
    }

    /// <summary>Replaces every store in one transaction, so it cannot half-apply.</summary>
    public async Task ReplaceAllAsync(DatasetRecord dataset, CancellationToken ct = default)
    {
        var module = await ModuleAsync();

        var payload = new Dictionary<string, object>
        {
            [People] = dataset.People,
            [BiologicalLinks] = dataset.BiologicalLinks,
            [AdoptiveLinks] = dataset.AdoptiveLinks,
            [Marriages] = dataset.Marriages,
            [StepparentLinks] = dataset.StepparentLinks,
            [Meta] = new[] { SchemaStamp },
        };

        await module.InvokeVoidAsync("replaceAll", ct, payload);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("clearAll", ct, SchemaStamp);
    }

    private static object SchemaStamp => new { id = Guid.Empty, schemaVersion = SchemaVersion };

    /// <summary>Reads a browser-local setting that is not part of the tree.</summary>
    public async Task<string?> ReadSettingAsync(string key, CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<string?>("readSetting", ct, key);
    }

    public async Task WriteSettingAsync(string key, string value, CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("writeSetting", ct, key, value);
    }

    /// <summary>
    /// Asks the browser to make this origin's storage persistent. The browser is
    /// the only copy of the user's tree (ADR-004), so eviction is data loss.
    /// </summary>
    public async Task<PersistenceStatus> RequestPersistenceAsync(CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        var raw = await module.InvokeAsync<JsonElement>("requestPersistence", ct);

        return new PersistenceStatus(
            raw.GetProperty("supported").GetBoolean(),
            raw.GetProperty("persisted").GetBoolean(),
            raw.TryGetProperty("usageBytes", out var usage) && usage.ValueKind == JsonValueKind.Number
                ? usage.GetInt64()
                : null);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is not null)
        {
            try
            {
                await _module.DisposeAsync();
            }
            catch (JSDisconnectedException)
            {
                // The page is going away; there is nothing left to dispose.
            }
        }
    }
}

public sealed record PersistenceStatus(bool Supported, bool Persisted, long? UsageBytes);
