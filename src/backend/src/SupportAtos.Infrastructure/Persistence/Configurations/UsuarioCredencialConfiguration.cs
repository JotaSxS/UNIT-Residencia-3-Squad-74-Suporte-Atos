using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportAtos.Domain.Entities;

namespace SupportAtos.Infrastructure.Persistence.Configurations;

public class UsuarioCredencialConfiguration : IEntityTypeConfiguration<UsuarioCredencial>
{
    public void Configure(EntityTypeBuilder<UsuarioCredencial> builder)
    {
        builder.ToTable("usuario_credencial");

        builder.HasKey(c => c.Id);

        builder.Property(c => c.Id)
            .HasColumnName("id");

        builder.Property(c => c.UsuarioId)
            .HasColumnName("usuario_id");

        builder.Property(c => c.SenhaHash)
            .HasColumnName("senha_hash")
            .HasColumnType("varchar(255)")
            .IsRequired();

        builder.Property(c => c.DataCriacao)
            .HasColumnName("data_criacao")
            .HasColumnType("timestamp")
            .IsRequired();

        builder.Property(c => c.DataUltimaAlteracao)
            .HasColumnName("data_ultima_alteracao")
            .HasColumnType("timestamp");

        builder.Property(c => c.Ativo)
            .HasColumnName("ativo")
            .HasDefaultValue(true)
            .IsRequired();

        builder.Property(c => c.TentativasFalhas)
            .HasColumnName("tentativas_falhas")
            .HasDefaultValue(0)
            .IsRequired();

        builder.Property(c => c.BloqueadoAte)
            .HasColumnName("bloqueado_ate")
            .HasColumnType("timestamp");

        builder.HasIndex(c => c.UsuarioId)
            .IsUnique();

        builder.Property(c => c.TokenAtivacaoHash).HasColumnName("token_ativacao_hash").HasColumnType("varchar(64)");
        builder.Property(c => c.TokenAtivacaoExpiraEm).HasColumnName("token_ativacao_expira_em").HasColumnType("timestamp");
        builder.Property(c => c.AtivacaoConsumidaEm).HasColumnName("ativacao_consumida_em").HasColumnType("timestamp");
        builder.HasIndex(c => c.TokenAtivacaoHash).IsUnique().HasFilter("token_ativacao_hash IS NOT NULL");

        builder.HasOne(c => c.Usuario)
            .WithOne(u => u.Credencial)
            .HasForeignKey<UsuarioCredencial>(c => c.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
