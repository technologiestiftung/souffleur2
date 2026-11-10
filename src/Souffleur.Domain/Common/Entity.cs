namespace Souffleur.Domain.Common;

/// <summary>
/// Basis aller persistierten Entitaeten. Die Zeitstempel setzt der DbContext,
/// nicht der Aufrufer.
/// </summary>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? ModifiedAt { get; set; }

    public Guid? CreatedBy { get; set; }

    public Guid? ModifiedBy { get; set; }
}
