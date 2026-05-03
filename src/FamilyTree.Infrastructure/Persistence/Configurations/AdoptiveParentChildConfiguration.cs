using FamilyTree.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

internal sealed class AdoptiveParentChildConfiguration : IEntityTypeConfiguration<AdoptiveParentChild>
{
    public void Configure(EntityTypeBuilder<AdoptiveParentChild> builder)
    {
        builder.HasKey(l => l.Id);

        builder.HasIndex(l => new { l.ParentId, l.ChildId }).IsUnique();
        builder.Property(l => l.Certainty).IsRequired().HasDefaultValue(Domain.Enums.RelationshipCertainty.Confirmed);

        builder.OwnsOne(l => l.AdoptionDate, owned =>
        {
            owned.Property(d => d.Year).HasColumnName("AdoptionDate_Year").IsRequired();
            owned.Property(d => d.Month).HasColumnName("AdoptionDate_Month");
            owned.Property(d => d.Day).HasColumnName("AdoptionDate_Day");
            owned.Property(d => d.IsApproximate).HasColumnName("AdoptionDate_IsApproximate").IsRequired();
        });
    }
}
