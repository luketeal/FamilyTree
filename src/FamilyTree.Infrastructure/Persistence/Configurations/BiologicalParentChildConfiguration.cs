using FamilyTree.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

internal sealed class BiologicalParentChildConfiguration : IEntityTypeConfiguration<BiologicalParentChild>
{
    public void Configure(EntityTypeBuilder<BiologicalParentChild> builder)
    {
        builder.HasKey(l => l.Id);

        builder.HasIndex(l => new { l.ParentId, l.ChildId }).IsUnique();
        builder.Property(l => l.Certainty).IsRequired().HasDefaultValue(Domain.Enums.RelationshipCertainty.Confirmed);

        // The parent-side FK is configured from PersonConfiguration (Restrict on child-side cascade).
        // Relationships are fully configured from the Person entity side.
    }
}
