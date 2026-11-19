namespace SupportAtos.Domain.Entities;

/// <summary>Marcador persistente: excluir um administrador não reabre o bootstrap.</summary>
public class AutenticacaoBootstrap
{
    public int Id { get; private set; } = 1;
    public DateTime ExecutadoEm { get; private set; } = DateTime.UtcNow;
}
