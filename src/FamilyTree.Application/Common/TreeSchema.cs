namespace FamilyTree.Application.Common;

/// <summary>
/// The version stamped on every persisted and exported payload.
/// </summary>
/// <remarks>
/// One number, deliberately. The browser store writes it into its meta store and
/// the export writes it into the file header, and the two describe the same
/// thing: the shape of a person, a link, a marriage. Keeping a second constant in
/// the storage project would let a record shape change bump one and not the
/// other, and the first symptom of that is an import that silently reads a field
/// that no longer means what it did.
///
/// Bump it whenever a persisted shape changes, in the same PR as the change, and
/// teach <see cref="Services.ImportService"/> how to read the older shape.
/// </remarks>
public static class TreeSchema
{
    public const int Version = 1;
}
