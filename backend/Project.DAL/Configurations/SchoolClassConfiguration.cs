using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.DAL.Configurations
{
    public class SchoolClassConfiguration : IEntityTypeConfiguration<SchoolClass>
    {
        public void Configure(EntityTypeBuilder<SchoolClass> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(x => x.Capacity);

            builder.Property(x => x.Status)
                .HasDefaultValue(ClassStatus.Active);

            builder.HasIndex(x => new { x.GradeLevelId, x.AcademicYearId, x.Name })
                .IsUnique()
                .HasFilter("[IsDeleted] = 0");

            builder.HasOne(x => x.GradeLevel)
                .WithMany()
                .HasForeignKey(x => x.GradeLevelId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.AcademicYear)
                .WithMany()
                .HasForeignKey(x => x.AcademicYearId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasQueryFilter(x => !x.IsDeleted);
        }
    }
}
