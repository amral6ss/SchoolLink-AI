using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Project.Domain.Entities;

namespace Project.DAL.Configurations
{
    public class StudentEnrollmentConfiguration : IEntityTypeConfiguration<StudentEnrollment>
    {
        public void Configure(EntityTypeBuilder<StudentEnrollment> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.TransferReason)
                .HasMaxLength(500);

            builder.Property(x => x.EnrolledAt)
                .HasColumnType("date");

            builder.Property(x => x.LeftAt)
                .HasColumnType("date");

            builder.HasIndex(x => new { x.StudentId, x.AcademicYearId })
                .IsUnique()
                .HasFilter("[LeftAt] IS NULL AND [IsDeleted] = 0");

            builder.HasOne(x => x.Student)
                .WithMany(x => x.Enrollments)
                .HasForeignKey(x => x.StudentId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.Class)
                .WithMany(x => x.Enrollments)
                .HasForeignKey(x => x.ClassId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.AcademicYear)
                .WithMany()
                .HasForeignKey(x => x.AcademicYearId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasQueryFilter(x => !x.IsDeleted);
        }
    }
}
