using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SupportAtos.Domain.Entities;

namespace SupportAtos.Infrastructure.Persistence.Configurations;

public class SessaoAutenticacaoConfiguration : IEntityTypeConfiguration<SessaoAutenticacao>
{
    public void Configure(EntityTypeBuilder<SessaoAutenticacao> builder)
    {
        builder.ToTable("sessao_autenticacao");

        builder.HasKey(s => s.Id);

        builder.Property(s => s.Id)
            .HasColumnName("id");

        builder.Property(s => s.UsuarioId)
            .HasColumnName("usuario_id");

        builder.Property(s => s.RefreshTokenHash)
            .HasColumnName("refresh_token_hash")
            .HasColumnType("varchar(255)")
            .IsRequired();

        builder.Property(s => s.RefreshTokenExpiracao)
            .HasColumnName("refresh_token_expiracao")
            .HasColumnType("timestamp")
            .IsRequired();

        builder.Property(s => s.CriadoEm)
            .HasColumnName("criado_em")
            .HasColumnType("timestamp")
            .IsRequired();

        builder.Property(s => s.RevogadoEm)
            .HasColumnName("revogado_em")
            .HasColumnType("timestamp");

        builder.Property(s => s.SubstituidoPorRefreshTokenHash)
            .HasColumnName("substituido_por_refresh_token_hash")
            .HasColumnType("varchar(255)");

        builder.Property(s => s.EnderecoIp)
            .HasColumnName("endereco_ip")
            .HasColumnType("varchar(45)");

        builder.Property(s => s.UserAgent)
            .HasColumnName("user_agent")
            .HasColumnType("varchar(500)");

        builder.HasIndex(s => s.UsuarioId);

        builder.HasIndex(s => s.RefreshTokenHash)
            .IsUnique();

        builder.HasIndex(s => s.RevogadoEm);

        builder.HasOne(s => s.Usuario)
            .WithMany(u => u.SessoesAutenticacao)
            .HasForeignKey(s => s.UsuarioId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
