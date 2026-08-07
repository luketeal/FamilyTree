using Microsoft.JSInterop;

namespace FamilyTree.UI.Shared;

public interface IFileDownloadInterop
{
    /// <summary>Offers generated text to the user as a downloaded file.</summary>
    Task DownloadTextAsync(string fileName, string content, string mimeType = "application/json");
}

/// <inheritdoc cref="IFileDownloadInterop" />
/// <remarks>
/// Behind an interface so component tests can assert that a download was
/// offered — bUnit has no download machinery, and "did the page call this"
/// is the only part of it a unit test can honestly answer. The real journey
/// from click to file on disk is covered in FamilyTree.E2E.Tests.
/// </remarks>
public sealed class FileDownloadInterop(IJSRuntime js) : IFileDownloadInterop, IAsyncDisposable
{
    private IJSObjectReference? _module;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>(
            "import", "./_content/FamilyTree.UI/js/download.js");

    public async Task DownloadTextAsync(
        string fileName, string content, string mimeType = "application/json")
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("downloadText", fileName, content, mimeType);
    }

    public async ValueTask DisposeAsync()
    {
        if (_module is null)
        {
            return;
        }

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
