using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Souffleur.Domain.Common;

namespace Souffleur.Infrastructure.Persistence;

/// <summary>
/// Setzt die Audit-Felder und verweigert Aenderungen an festgeschriebenen Datensaetzen.
/// Liegt ausserhalb des DbContext, damit beide -- Anwendung und Test -- dieselbe
/// Implementierung verwenden.
/// </summary>
public static class AuditBehaviour
{
    public static void Apply(ChangeTracker changeTracker, DateTimeOffset now, Guid? userId)
    {
        foreach (EntityEntry entry in changeTracker.Entries())
        {
            if (entry.Entity is IImmutable
                && entry.State is EntityState.Modified or EntityState.Deleted
                && CommittedBefore(entry) is { } committedAt
                && !OnlyOrganisational(entry))
            {
                throw new ImmutableRecordException(
                    entry.Entity.GetType().Name,
                    committedAt,
                    entry.State);
            }

            if (entry.Entity is not Entity auditable)
            {
                continue;
            }

            switch (entry.State)
            {
                case EntityState.Added:
                    auditable.CreatedAt = now;
                    auditable.CreatedBy = userId;
                    break;
                case EntityState.Modified:
                    auditable.ModifiedAt = now;
                    auditable.ModifiedBy = userId;
                    // Wer einen Datensatz aendert, darf seine Herkunft nicht umschreiben.
                    entry.Property(nameof(Entity.CreatedAt)).IsModified = false;
                    entry.Property(nameof(Entity.CreatedBy)).IsModified = false;
                    break;
            }
        }
    }

    /// <summary>
    /// Der Festschreibungszeitpunkt, wie er vor dieser Aenderung in der Datenbank
    /// stand -- <c>null</c> fuer einen Entwurf.
    /// </summary>
    /// <remarks>
    /// Entscheidend ist der *alte* Wert, nicht der neue: Das Festschreiben selbst
    /// setzt <c>CommittedAt</c> und waere sonst die erste Aenderung, die an der
    /// eigenen Sperre scheitert. Ein Entwurf darf alles, eine festgeschriebene
    /// Buchung nichts mehr.
    /// </remarks>
    /// <summary>
    /// Was sich an einem festgeschriebenen Datensatz trotzdem aendern darf.
    /// </summary>
    /// <remarks>
    /// Die Festschreibung schuetzt die Belegangaben: Datum, Betrag, Lieferant,
    /// Text. Zu welcher Produktion ein Beleg gehoert, ist keine davon -- es
    /// ist die Ordnung des Hauses, und die darf nachgetragen werden, wenn der
    /// Beleg laengst gebucht ist. Alles andere bleibt gesperrt.
    /// </remarks>
    private static readonly HashSet<string> Organisational =
        [nameof(Souffleur.Domain.Accounting.Voucher.ProductionId), nameof(Entity.ModifiedAt), nameof(Entity.ModifiedBy)];

    private static bool OnlyOrganisational(EntityEntry entry) =>
        entry.State == EntityState.Modified
        && entry.Properties.Where(p => p.IsModified).All(p => Organisational.Contains(p.Metadata.Name));

    private static DateTimeOffset? CommittedBefore(EntityEntry entry)
    {
        PropertyEntry? property = entry.Properties
            .FirstOrDefault(p => p.Metadata.Name == nameof(IImmutable.CommittedAt));

        return property?.OriginalValue as DateTimeOffset?;
    }
}
