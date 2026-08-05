namespace FamilyTree.Application.Services;

public enum ToastLevel
{
    Success,
    Warning,
    Error,
}

public sealed record Toast(string Message, ToastLevel Level);

/// <summary>
/// Transient confirmations raised from anywhere and rendered once by the layout.
/// </summary>
/// <remarks>
/// A page that navigates away as part of its own success — saving a person and
/// returning to the list — cannot show its own confirmation, because it stops
/// existing before the message would be read. Raising it here lets the message
/// outlive the page that caused it.
/// </remarks>
public sealed class ToastService
{
    public event Action<Toast>? Raised;

    public void Show(string message, ToastLevel level = ToastLevel.Success) =>
        Raised?.Invoke(new Toast(message, level));
}
