using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace FamilyTree.UI.Shared;

/// <summary>
/// Keeps an open dialog inside the visible area and pins the page behind it.
/// </summary>
/// <remarks>
/// This exists because CSS cannot do it. With a soft keyboard open, iOS Safari
/// shrinks the visual viewport while the layout viewport — which is what
/// position:fixed is measured against — stays full height. The overlay then
/// extends well below the screen, and reaching it means panning the visual
/// viewport, a browser gesture that overflow:hidden does not affect. Only the
/// VisualViewport API reports the real numbers.
/// </remarks>
public interface IOverlayInterop
{
    Task OpenAsync(ElementReference overlay);

    Task CloseAsync(ElementReference overlay);
}

/// <inheritdoc cref="IOverlayInterop" />
public sealed class OverlayInterop(IJSRuntime js) : IOverlayInterop, IAsyncDisposable
{
    private IJSObjectReference? _module;

    private async Task<IJSObjectReference> ModuleAsync() =>
        _module ??= await js.InvokeAsync<IJSObjectReference>(
            "import", "./_content/FamilyTree.UI/js/overlay.js");

    public async Task OpenAsync(ElementReference overlay)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("open", overlay);
    }

    public async Task CloseAsync(ElementReference overlay)
    {
        var module = await ModuleAsync();
        await module.InvokeVoidAsync("close", overlay);
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
