namespace Souffleur.Domain.Common;

/// <summary>
/// Markiert Daten, die nach dem Festschreiben nicht mehr geaendert oder geloescht
/// werden duerfen -- Buchungen, Journaleintraege, Belege.
/// </summary>
/// <remarks>
/// Umsetzung der GoBD-Unveraenderbarkeit (§ 146 Abs. 4 AO). Der DbContext verweigert
/// <c>Modified</c> und <c>Deleted</c> fuer diese Entitaeten; eine Korrektur erfolgt
/// ausschliesslich durch einen Storno-Datensatz, der den urspruenglichen aufhebt.
/// Durchgesetzt wird das in der Anwendung, nicht in SQLite -- die Datenbank kennt
/// keine solche Sperre.
/// </remarks>
public interface IImmutable
{
    /// <summary>
    /// Zeitpunkt der Festschreibung. Solange <c>null</c>, ist der Datensatz ein
    /// Entwurf und darf geaendert werden.
    /// </summary>
    DateTimeOffset? CommittedAt { get; }
}
