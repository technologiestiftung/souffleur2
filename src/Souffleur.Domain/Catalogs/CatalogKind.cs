namespace Souffleur.Domain.Catalogs;

/// <summary>
/// Die Listen, aus denen in souffleur ausgewählt wird. Jede gehört dem Haus:
/// Der Administrator legt die Einträge an, benennt sie um und sortiert sie --
/// siehe ADR-0005.
/// </summary>
/// <remarks>
/// Eine neue Liste ist ein Wert mehr in diesem Enum. Kein neues Modell, keine
/// neue Tabelle, keine neue Oberfläche -- die Verwaltung bedient alle gleich.
///
/// Was hier *nicht* steht, steht bewusst nicht hier: Die steuerlichen Sphären
/// und die Kontenrahmen stehen im Gesetz, die Rollen im Rechtemodell. Ein Haus,
/// das sie erfinden könnte, bekäme einen Abschluss, den das Finanzamt nicht
/// annimmt, oder eine Rolle, die niemand prüft.
/// </remarks>
public enum CatalogKind
{
    /// <summary>Probe, Vorstellung, Gastspiel, Frist -- was ein Termin ist.</summary>
    EventCategory = 1,

    /// <summary>Wonach das Haus seine Aufgaben sortiert.</summary>
    TaskCategory = 2,

    /// <summary>
    /// Kostenarten für die Auswertung. Nicht zu verwechseln mit dem Sachkonto:
    /// Das Konto bucht, die Kostenart fasst zusammen, was das Haus zusammen
    /// sehen will.
    /// </summary>
    CostType = 3,

    /// <summary>Freie Schlagworte am Kontakt, neben den festen Rollen.</summary>
    ContactTag = 4,

    /// <summary>
    /// Wonach das Haus seine Ressourcen sortiert: Bühne, Probebühne, Werkstatt,
    /// Fahrzeug. Die Ressource selbst ist kein Katalogeintrag -- sie trägt
    /// Verhalten (siehe <c>Resource</c>), ihre Art trägt nur einen Namen.
    /// </summary>
    ResourceKind = 5,

    /// <summary>
    /// In welcher Funktion jemand bei einem Termin dabei ist: Regie,
    /// Inspizienz, Abenddienst, Technik. Nicht zu verwechseln mit der Rolle
    /// im Adressbuch -- die sagt, in welcher Eigenschaft jemand mit dem Haus
    /// zu tun hat, diese sagt, was er an diesem Abend tut.
    /// </summary>
    ParticipationRole = 6,

    // 7 war „Produktionen": ein Name, den Tourneen teilten. Seit ADR-0011 ist
    // die Produktion ein eigener Gegenstand mit Stand, Budget und Beteiligten
    // (Tabelle Productions); die Migration hat die Eintraege hinuebergetragen.
    // Die Nummer bleibt frei, damit alte Datenbanken sie nicht neu deuten.
}

public static class CatalogKinds
{
    public static readonly CatalogKind[] All = Enum.GetValues<CatalogKind>();

    /// <summary>Überschrift in der Verwaltung.</summary>
    public static string Label(this CatalogKind kind) => kind switch
    {
        CatalogKind.EventCategory => "Terminkategorien",
        CatalogKind.TaskCategory => "Aufgabenkategorien",
        CatalogKind.CostType => "Kostenarten",
        CatalogKind.ContactTag => "Schlagworte an Kontakten",
        CatalogKind.ResourceKind => "Arten von Ressourcen",
        CatalogKind.ParticipationRole => "Funktionen am Termin",
        _ => kind.ToString(),
    };

    /// <summary>Was die Liste steuert -- steht unter der Überschrift.</summary>
    public static string Description(this CatalogKind kind) => kind switch
    {
        CatalogKind.EventCategory =>
            "Wofür ein Termin steht. Die Farbe erscheint im Jahresraster des Kalenders.",
        CatalogKind.TaskCategory =>
            "Wonach die Aufgaben des Hauses sortiert werden.",
        CatalogKind.CostType =>
            "Zusammenfassung für die Auswertung, quer zu den Sachkonten. Untergliederung möglich.",
        CatalogKind.ContactTag =>
            "Freie Schlagworte neben den festen Rollen im Adressbuch.",
        CatalogKind.ResourceKind =>
            "Wonach Bühnen, Räume, Werkstätten und Fahrzeuge sortiert werden. Die Farbe erscheint an der Ressource.",
        CatalogKind.ParticipationRole =>
            "Was jemand an einem Termin tut: Regie, Inspizienz, Abenddienst. Nicht die Rolle im Adressbuch.",
        _ => string.Empty,
    };

    /// <summary>
    /// Ob die Liste eine Untergliederung kennt. Nur wo sie fachlich etwas
    /// bedeutet -- eine Terminkategorie unter einer anderen wäre Unfug.
    /// </summary>
    public static bool AllowsHierarchy(this CatalogKind kind) =>
        kind is CatalogKind.CostType;

    /// <summary>
    /// Ob die Einträge eine Farbe tragen. Nur wo sie irgendwo erscheint; sonst
    /// steht ein Feld in der Verwaltung, das nichts bewirkt.
    /// </summary>
    public static bool UsesColor(this CatalogKind kind) =>
        kind is CatalogKind.EventCategory or CatalogKind.TaskCategory
            or CatalogKind.ContactTag or CatalogKind.ResourceKind;
}
