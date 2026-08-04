using System.Text.Json;
using FamilyTree.Storage.Browser.Records;
using Microsoft.JSInterop;

namespace FamilyTree.Storage.Browser;

/// <summary>
/// Thin typed wrapper over the IndexedDB JavaScript module. The repositories
/// speak in records; this speaks in stores and keys.
/// </summary>
public sealed class IndexedDbStore(IJSRuntime js) : IAsyncDisposable
{
    // Bumped when a persisted record shape changes. Stored alongside the data so
    // a mismatch can be detected and handled, rather than deserialising stale
    // records into the wrong shape and failing somewhere unrelated.
    public const int SchemaVersion = 1;

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

    /// <summary>
    /// One round trip for many keys. The repositories expose batched reads so
    /// callers never loop single gets — harmless here, ruinous over HTTP.
    /// </summary>
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
            [Meta] = new[] { new { id = Guid.Empty, schemaVersion = SchemaVersion } },
        };

        await module.InvokeVoidAsync("replaceAll", ct, payload);
    }

    public async Task ClearAllAsync(CancellationToken ct = default)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("clearAll", ct);
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
