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
    /// 3 — stepparent labels.
    /// </summary>
    /// <remarks>
    /// Version 1 carried no <c>phantoms</c> section; version 2 added it. Version 3
    /// adds <c>stepparentLinks</c>, and the bump is for the same reason as the
    /// last one: older files simply have no such section and import reads them
    /// unchanged, but a version 2 <em>app</em> reading a version 3 file would drop
    /// every step relationship in it without saying so.
    /// <para>
    /// The stepparent store and record have existed since PR 2 and were always
    /// exported as nothing, which was true while nothing could write one. PR 9 is
    /// where they become writable, so it is where the file format gains them and
    /// where the stamp has to move — a version that could create a record its own
    /// backup discarded would be the silent round-trip loss ADR-007 was written
    /// about.
    /// </para>
    /// </remarks>
    public const int Version = 3;
}
