using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Project.Domain.Entities;

namespace Project.DAL.Configurations
{
    public class CertificateSubjectConfiguration : IEntityTypeConfiguration<CertificateSubject>
    {
        public void Configure(EntityTypeBuilder<CertificateSubject> builder)
        {
            builder.HasKey(x => x.Id);

            builder.Property(x => x.SubjectName)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(x => x.MaxScore)
                .IsRequired();

            builder.Property(x => x.MinScore)
                .IsRequired();

            builder.Property(x => x.SortOrder)
                .IsRequired();

            builder.HasQueryFilter(x => !x.IsDeleted);
        }
    }
}
