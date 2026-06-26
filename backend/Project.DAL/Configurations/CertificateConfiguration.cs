using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Project.Domain.Entities;

namespace Project.DAL.Configurations
{
    public class CertificateConfiguration : IEntityTypeConfiguration<Certificate>
    {
        public void Configure(EntityTypeBuilder<Certificate> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.GradeLevel)
                .IsRequired()
                .HasMaxLength(100);

            builder.Property(x => x.Term)
                .HasMaxLength(100);

            builder.Property(x => x.ExamRole)
                .HasMaxLength(100);

            builder.Property(x => x.Year)
                .HasMaxLength(20);

            builder.HasMany(x => x.Subjects)
                .WithOne(s => s.Certificate)
                .HasForeignKey(s => s.CertificateId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasQueryFilter(x => !x.IsDeleted);
        }
    }
}
