using Microsoft.JSInterop;

namespace FamilyTree.UI.Shared;

public interface IViewportInterop
{
    /// <summary>
    /// Whether the shell is in its narrow layout, matching the 768px breakpoint
    /// the stylesheets use.
    /// </summary>
    Task<bool> IsNarrowAsync();
}

/// <inheritdoc cref="IViewportInterop" />
/// <remarks>
/// Blazor has no way to read a media query, and guessing from a user-agent
/// string would disagree with the CSS on a narrow desktop window. Asking the
/// browser keeps one definition of "narrow" for both.
/// </remarks>
public sealed class ViewportInterop(IJSRuntime js) : IViewportInterop, IAsyncDisposable
{
    private IJSObjectReference? _module;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>(
            "import", "./_content/FamilyTree.UI/js/viewport.js");

    public async Task<bool> IsNarrowAsync()
    {
        var module = await ModuleAsync();
        return await module.InvokeAsync<bool>("isNarrow");
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
