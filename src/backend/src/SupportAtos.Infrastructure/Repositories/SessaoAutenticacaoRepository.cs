using Microsoft.EntityFrameworkCore;
using SupportAtos.Application.Ports;
using SupportAtos.Domain.Entities;
using SupportAtos.Infrastructure.Persistence;

namespace SupportAtos.Infrastructure.Repositories;

/// <summary>
/// Repositório de sessões de autenticação (refresh tokens) usando Entity Framework Core.
/// </summary>
public class SessaoAutenticacaoRepository : ISessaoAutenticacaoRepository
{
    private readonly SupportAtosDbContext _context;

    public SessaoAutenticacaoRepository(SupportAtosDbContext context)
    {
        _context = context;
    }

    public async Task<T> ExecutarSerializadoAsync<T>(string refreshTokenHash, Func<Task<T>> operacao, CancellationToken cancellationToken = default)
    {
        await using var transacao = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted, cancellationToken);
        var usuarioId = await _context.SessoesAutenticacao.AsNoTracking()
            .Where(s => s.RefreshTokenHash == refreshTokenHash)
            .Select(s => (int?)s.UsuarioId).SingleOrDefaultAsync(cancellationToken);
        if (usuarioId.HasValue)
        {
            // Namespace fixo + usuário: bloqueio compartilhado entre processos neste banco.
            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(74101, {usuarioId.Value})", cancellationToken);
            // Atualiza sessões rastreadas após esperar por outra transação.
            foreach (var entry in _context.ChangeTracker.Entries<SessaoAutenticacao>().ToList())
                await entry.ReloadAsync(cancellationToken);
        }
        var resultado = await operacao();
        await transacao.CommitAsync(cancellationToken);
        return resultado;
    }

    /// <summary>Busca uma sessão pelo hash do refresh token.</summary>
    public Task<SessaoAutenticacao?> GetByRefreshTokenHashAsync(string refreshTokenHash, CancellationToken cancellationToken = default)
        => _context.SessoesAutenticacao
            .FirstOrDefaultAsync(s => s.RefreshTokenHash == refreshTokenHash, cancellationToken);

    /// <summary>Adiciona uma nova sessão.</summary>
    public async Task AddAsync(SessaoAutenticacao sessao, CancellationToken cancellationToken = default)
    {
        await _context.SessoesAutenticacao.AddAsync(sessao, cancellationToken);
    }

    /// <summary>Busca todas as sessões de um usuário (para revogação em caso de reuso de token).</summary>
    public async Task<IReadOnlyList<SessaoAutenticacao>> GetByUsuarioIdAsync(int usuarioId, CancellationToken cancellationToken = default)
        => await _context.SessoesAutenticacao
            .Where(s => s.UsuarioId == usuarioId)
            .ToListAsync(cancellationToken);
}
