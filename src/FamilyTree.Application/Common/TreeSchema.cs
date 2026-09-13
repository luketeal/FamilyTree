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
    /// <summary>
    /// 2 — phantoms.
    /// </summary>
    /// <remarks>
    /// Version 1 files carry no <c>phantoms</c> section and no link that names
    /// one, which is exactly what a version 1 export was: import reads them
    /// unchanged, finds no phantoms, and behaves as it always did. The bump is
    /// so a version 1 <em>app</em> cannot read a version 2 file and silently
    /// discard the placeholders and their links as orphans.
    /// </remarks>
    public const int Version = 2;
}
