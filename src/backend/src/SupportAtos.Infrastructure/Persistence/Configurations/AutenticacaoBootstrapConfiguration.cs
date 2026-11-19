using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportAtos.Domain.Entities;

namespace SupportAtos.Infrastructure.Persistence.Configurations;

public class AutenticacaoBootstrapConfiguration : IEntityTypeConfiguration<AutenticacaoBootstrap>
{
    public void Configure(EntityTypeBuilder<AutenticacaoBootstrap> builder)
    {
        builder.ToTable("autenticacao_bootstrap", t => t.HasCheckConstraint("ck_bootstrap_unico", "id = 1"));
        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(b => b.ExecutadoEm).HasColumnName("executado_em").HasColumnType("timestamp");
    }
}
