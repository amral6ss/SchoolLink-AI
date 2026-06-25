using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Project.Domain.Entities;
using Project.Domain.Enums;

namespace Project.DAL.Configurations
{
    public class TimetableConfiguration : IEntityTypeConfiguration<Timetable>
    {
        public void Configure(EntityTypeBuilder<Timetable> builder)
        {
            builder.HasKey(x => x.Id);

            builder.HasOne(x => x.Class)
                .WithMany()
                .HasForeignKey(x => x.ClassId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.AcademicYear)
                .WithMany()
                .HasForeignKey(x => x.AcademicYearId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(x => x.SourceTimetable)
                .WithMany()
                .HasForeignKey(x => x.SourceTimetableId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(x => new { x.ClassId, x.AcademicYearId }, "IX_Timetable_DraftLifecycle")
                .IsUnique()
                .HasDatabaseName("UX_Timetable_Draft")
                .HasFilter($"[Status] = {(int)TimetableStatus.Draft} AND [IsDeleted] = 0");

            builder.HasIndex(x => new { x.ClassId, x.AcademicYearId }, "IX_Timetable_ActiveLifecycle")
                .IsUnique()
                .HasDatabaseName("UX_Timetable_Active")
                .HasFilter($"[Status] = {(int)TimetableStatus.Active} AND [IsDeleted] = 0");

            builder.HasQueryFilter(x => !x.IsDeleted);
        }
    }
}
