using FamilyTree.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

internal sealed class StepparentRelationshipConfiguration : IEntityTypeConfiguration<StepparentRelationship>
{
    public void Configure(EntityTypeBuilder<StepparentRelationship> builder)
    {
        builder.HasKey(s => s.Id);

        // A given stepparent can only be labeled once per stepchild per marriage
        builder.HasIndex(s => new { s.StepparentId, s.StepchildId, s.MarriageId }).IsUnique();
    }
}
