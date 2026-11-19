using Microsoft.EntityFrameworkCore;
using SupportAtos.Application.Exceptions;
using SupportAtos.Application.Ports;
using SupportAtos.Domain.Entities;
using SupportAtos.Domain.Enums;
using SupportAtos.Infrastructure.Persistence;

namespace SupportAtos.Infrastructure.Repositories;

public class FuncionarioAutenticacaoRepository(SupportAtosDbContext context) : IFuncionarioAutenticacaoRepository
{
    public async Task<T> ExecutarCadastroAsync<T>(Func<Task<T>> operacao, CancellationToken ct = default)
    {
        await using var transacao = await context.Database.BeginTransactionAsync(System.Data.IsolationLevel.ReadCommitted, ct);
        try
        {
            var result = await operacao();
            await transacao.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException ex) when (new NpgsqlUniqueViolationDetector().IsUniqueViolation(ex))
        {
            throw new ConflictException("Já existe um usuário ou convite com esses dados.");
        }
    }

    public Task<T> ExecutarBootstrapAsync<T>(Func<Task<T>> operacao, CancellationToken ct = default)
        => ExecutarCadastroAsync(async () =>
        {
            await context.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(74102, 0)", ct);
            if (await context.Set<AutenticacaoBootstrap>().AnyAsync(ct)
                || await context.Usuarios.AnyAsync(u => u.Papel == PapelUsuario.Admin, ct))
                throw new ConflictException("Bootstrap indisponível: já foi executado ou existe administrador.");
            var result = await operacao();
            context.Add(new AutenticacaoBootstrap());
            await context.SaveChangesAsync(ct);
            return result;
        }, ct);

    public Task<T> ExecutarAtivacaoAsync<T>(string hash, Func<Task<T>> operacao, CancellationToken ct = default)
        => ExecutarCadastroAsync(async () =>
        {
            var id = await context.UsuarioCredenciais.AsNoTracking().Where(c => c.TokenAtivacaoHash == hash)
                .Select(c => (int?)c.UsuarioId).SingleOrDefaultAsync(ct);
            if (id.HasValue)
            {
                await context.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock(74101, {id.Value})", ct);
                // Bloqueia também a linha de usuário: alteração concorrente de papel/código externo
                // não pode transformar um cliente/usuário de portal em funcionário durante a ativação.
                await context.Database.ExecuteSqlInterpolatedAsync($"SELECT id FROM usuario WHERE id = {id.Value} FOR UPDATE", ct);
                foreach (var entry in context.ChangeTracker.Entries<UsuarioCredencial>().ToList())
                    await entry.ReloadAsync(ct);
                foreach (var entry in context.ChangeTracker.Entries<Usuario>().ToList())
                    await entry.ReloadAsync(ct);
            }
            return await operacao();
        }, ct);

    public Task<UsuarioCredencial?> BuscarConviteAsync(string hash, CancellationToken ct = default)
        => context.UsuarioCredenciais.SingleOrDefaultAsync(c => c.TokenAtivacaoHash == hash, ct);

    public async Task AdicionarCredencialAsync(UsuarioCredencial credencial, CancellationToken ct = default)
        => await context.UsuarioCredenciais.AddAsync(credencial, ct);
}
