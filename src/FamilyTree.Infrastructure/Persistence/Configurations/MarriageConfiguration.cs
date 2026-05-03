using FamilyTree.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

internal sealed class MarriageConfiguration : IEntityTypeConfiguration<Marriage>
{
    public void Configure(EntityTypeBuilder<Marriage> builder)
    {
        builder.HasKey(m => m.Id);

        builder.Property(m => m.StartPlace).HasMaxLength(500);
        builder.Property(m => m.Certainty).IsRequired().HasDefaultValue(Domain.Enums.RelationshipCertainty.Confirmed);

        builder.OwnsOne(m => m.StartDate, owned =>
        {
            owned.Property(d => d.Year).HasColumnName("StartDate_Year").IsRequired();
            owned.Property(d => d.Month).HasColumnName("StartDate_Month");
            owned.Property(d => d.Day).HasColumnName("StartDate_Day");
            owned.Property(d => d.IsApproximate).HasColumnName("StartDate_IsApproximate").IsRequired();
        });

        builder.OwnsOne(m => m.EndDate, owned =>
        {
            owned.Property(d => d.Year).HasColumnName("EndDate_Year").IsRequired();
            owned.Property(d => d.Month).HasColumnName("EndDate_Month");
            owned.Property(d => d.Day).HasColumnName("EndDate_Day");
            owned.Property(d => d.IsApproximate).HasColumnName("EndDate_IsApproximate").IsRequired();
        });

        builder.HasMany(m => m.StepparentRelationships)
            .WithOne(s => s.Marriage)
            .HasForeignKey(s => s.MarriageId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(m => m.StepparentRelationships).UsePropertyAccessMode(PropertyAccessMode.Field);

        // Ignore computed property — not stored
        builder.Ignore(m => m.IsOngoing);
    }
}
