using FamilyTree.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FamilyTree.Infrastructure.Persistence.Configurations;

internal sealed class PersonConfiguration : IEntityTypeConfiguration<Person>
{
    public void Configure(EntityTypeBuilder<Person> builder)
    {
        builder.HasKey(p => p.Id);

        builder.Property(p => p.FirstName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.LastName).HasMaxLength(200).IsRequired();
        builder.Property(p => p.BirthSurname).HasMaxLength(200);
        builder.Property(p => p.BirthPlace).HasMaxLength(500);
        builder.Property(p => p.DeathPlace).HasMaxLength(500);
        builder.Property(p => p.PhotoPath).HasMaxLength(1000);
        builder.Property(p => p.Notes).HasMaxLength(5000);
        builder.Property(p => p.Gender).IsRequired();

        builder.OwnsOne(p => p.BirthDate, ConfigurePartialDate("BirthDate"));
        builder.OwnsOne(p => p.DeathDate, ConfigurePartialDate("DeathDate"));

        builder.HasMany(p => p.BiologicalParentLinks)
            .WithOne(l => l.Child)
            .HasForeignKey(l => l.ChildId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.BiologicalChildLinks)
            .WithOne(l => l.Parent)
            .HasForeignKey(l => l.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.AdoptiveParentLinks)
            .WithOne(l => l.Child)
            .HasForeignKey(l => l.ChildId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.AdoptiveChildLinks)
            .WithOne(l => l.Parent)
            .HasForeignKey(l => l.ParentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.MarriagesAsSpouse1)
            .WithOne(m => m.Spouse1)
            .HasForeignKey(m => m.Spouse1Id)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.MarriagesAsSpouse2)
            .WithOne(m => m.Spouse2)
            .HasForeignKey(m => m.Spouse2Id)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasMany(p => p.StepparentLinks)
            .WithOne(s => s.Stepparent)
            .HasForeignKey(s => s.StepparentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(p => p.StepchildLinks)
            .WithOne(s => s.Stepchild)
            .HasForeignKey(s => s.StepchildId)
            .OnDelete(DeleteBehavior.Cascade);

        // Back the IReadOnlyList navigation properties with the private List fields
        builder.Navigation(p => p.BiologicalParentLinks).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.BiologicalChildLinks).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.AdoptiveParentLinks).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.AdoptiveChildLinks).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.MarriagesAsSpouse1).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.MarriagesAsSpouse2).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.StepparentLinks).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(p => p.StepchildLinks).UsePropertyAccessMode(PropertyAccessMode.Field);
    }

    private static Action<OwnedNavigationBuilder<Person, Domain.ValueObjects.PartialDate>> ConfigurePartialDate(string prefix) =>
        owned =>
        {
            owned.Property(d => d.Year).HasColumnName($"{prefix}_Year").IsRequired();
            owned.Property(d => d.Month).HasColumnName($"{prefix}_Month");
            owned.Property(d => d.Day).HasColumnName($"{prefix}_Day");
            owned.Property(d => d.IsApproximate).HasColumnName($"{prefix}_IsApproximate").IsRequired();
        };
}
