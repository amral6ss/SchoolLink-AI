using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.DAL.Configurations
{
    public class StudentConfiguration : IEntityTypeConfiguration<Student>
    {
        public void Configure(EntityTypeBuilder<Student> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.FullName)
                .IsRequired()
                .HasMaxLength(150);

            builder.Property(x => x.NationalId)
                .HasMaxLength(14);

            builder.ToTable(t => t.HasCheckConstraint(
                "CK_Students_NationalId_14Digits",
                "[NationalId] IS NULL OR (LEN([NationalId]) = 14 AND [NationalId] NOT LIKE '%[^0-9]%')"));

            builder.Property(x => x.BirthDate)
                .HasColumnType("date");

            builder.Property(x => x.LifecycleStatus)
                .HasDefaultValue(StudentLifecycleStatus.Active);

            builder.HasIndex(x => x.NationalId)
                .IsUnique()
                .HasFilter("[NationalId] IS NOT NULL");

            builder.HasOne(x => x.User)
                .WithMany()
                .HasForeignKey(x => x.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasQueryFilter(x => !x.IsDeleted);
        }
    }
}
