using Microsoft.EntityFrameworkCore;
using SupportAtos.Application.Ports;
using SupportAtos.Domain.Entities;
using SupportAtos.Infrastructure.Persistence;

namespace SupportAtos.Infrastructure.Repositories;

public class UsuarioRepository : IUsuarioRepository
{
    private readonly SupportAtosDbContext _context;

    public UsuarioRepository(SupportAtosDbContext context)
    {
        _context = context;
    }

    public Task<Usuario?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
        => _context.Usuarios.Include(u => u.Credencial).FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<Usuario?> GetComGrupoEmpresaPorIdAsync(int id, CancellationToken cancellationToken = default)
        => _context.Usuarios
            .Include(u => u.GrupoEmpresa)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public async Task<IReadOnlyList<Usuario>> ListarPorGrupoEmpresaAsync(int grupoEmpresaId, CancellationToken cancellationToken = default)
        => await _context.Usuarios
            .Include(u => u.GrupoEmpresa)
            .Where(u => u.GrupoEmpresaId == grupoEmpresaId)
            .OrderBy(u => u.Nome)
            .ToListAsync(cancellationToken);

    public Task<Usuario?> GetByEmailAsync(string email, CancellationToken cancellationToken = default)
        => _context.Usuarios.Include(u => u.Credencial).FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<Usuario?> GetByCodigoExternoAsync(string codigoExterno, CancellationToken cancellationToken = default)
        => _context.Usuarios.FirstOrDefaultAsync(u => u.CodigoExterno == codigoExterno, cancellationToken);

    public Task<bool> EmailExisteAsync(string email, CancellationToken cancellationToken = default)
        => _context.Usuarios.AnyAsync(u => u.Email == email, cancellationToken);

    public async Task IncrementarTentativasFalhasAsync(int usuarioId, CancellationToken cancellationToken = default)
    {
        // UPDATE atômico no banco: evita perda de incrementos em requisições simultâneas.
        await _context.UsuarioCredenciais
            .Where(c => c.UsuarioId == usuarioId)
            .ExecuteUpdateAsync(c => c.SetProperty(c => c.TentativasFalhas, c => c.TentativasFalhas + 1), cancellationToken);
    }

    public async Task IncrementarTentativasEFalharSeLimiteAsync(
        int usuarioId,
        int maxTentativas,
        int bloqueioMinutos,
        CancellationToken cancellationToken = default)
    {
        // Operação atômica no PostgreSQL: incrementa tentativas_falhas e, se atingir
        // o limite, define bloqueado_ate — tudo em um único UPDATE, sem race conditions.
        var bloqueadoAte = DateTime.UtcNow.AddMinutes(bloqueioMinutos);
        await _context.UsuarioCredenciais
            .Where(c => c.UsuarioId == usuarioId)
            .ExecuteUpdateAsync(c => c
                .SetProperty(c => c.TentativasFalhas, c => c.TentativasFalhas + 1)
                .SetProperty(c => c.BloqueadoAte, c => c.TentativasFalhas + 1 >= maxTentativas ? bloqueadoAte : c.BloqueadoAte),
                cancellationToken);
    }

    public async Task<IReadOnlyList<Usuario>> ListarFuncionariosAsync(CancellationToken cancellationToken = default)
        => await _context.Usuarios.AsNoTracking()
            .Include(u => u.Credencial)
            .Where(u => u.Papel == Domain.Enums.PapelUsuario.Admin
                || u.Papel == Domain.Enums.PapelUsuario.Supervisor || u.Papel == Domain.Enums.PapelUsuario.Agente)
            .OrderBy(u => u.Nome).ThenBy(u => u.Id)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<Usuario>> ListarAgentesAsync(CancellationToken cancellationToken = default)
        => await _context.Usuarios
            .Where(u => u.Papel == Domain.Enums.PapelUsuario.Agente
                || u.Papel == Domain.Enums.PapelUsuario.Supervisor
                || u.Papel == Domain.Enums.PapelUsuario.Admin)
            .OrderBy(u => u.Id)
            .ToListAsync(cancellationToken);

    public async Task AddAsync(Usuario usuario, CancellationToken cancellationToken = default)
        => await _context.Usuarios.AddAsync(usuario, cancellationToken);
}