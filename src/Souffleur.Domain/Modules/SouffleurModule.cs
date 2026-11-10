namespace Souffleur.Domain.Modules;

/// <summary>
/// Die Module, aus denen souffleur besteht. Jedes Haus entscheidet, welche es
/// einschaltet -- siehe ADR-0004.
/// </summary>
/// <remarks>
/// Die Namen folgen der Oberflaeche: Was der Reiter oben heisst, heisst hier
/// genauso. Fachgebiete innerhalb eines Moduls -- Buchen, Belege, Bank -- sind
/// keine eigenen Module; sie gehen mit ihrem Modul an und aus.
/// </remarks>
public enum SouffleurModule
{
    /// <summary>Kennzahlen und was ansteht. Laesst sich nicht abschalten.</summary>
    Overview = 1,

    /// <summary>Buchhaltung, Belege, Bank, Budget, Inventar.</summary>
    Finance = 2,

    /// <summary>Dienstplan, Zeiterfassung, Kuenstlersozialabgabe.</summary>
    Personnel = 3,

    /// <summary>Verkaeufe und Auslastung aus dem Vorverkauf des Hauses.</summary>
    Ticketing = 4,

    /// <summary>Aufgaben und Zeitstrahl.</summary>
    Workspace = 5,

    /// <summary>Lesende Anbindung an die artwork-Datenbank des Hauses.</summary>
    Artwork = 6,

    /// <summary>
    /// Das Adressbuch des Hauses. Laesst sich nicht abschalten: Buchhaltung,
    /// Belege und Aufgaben haengen daran.
    /// </summary>
    Contacts = 7,

    /// <summary>
    /// Chat: Kanaele, Direktnachrichten und Unterhaltungen an Gegenstaenden.
    /// Eigenstaendig, nicht Teil der Aufgaben.
    /// </summary>
    Chat = 8,

    /// <summary>
    /// Termine, Serien und das Jahresraster. Fuehrt zusammen, was anderswo
    /// entsteht -- siehe ADR-0006.
    /// </summary>
    Calendar = 9,

    /// <summary>
    /// Die Ablage des Hauses: hochladen, beschriften, an Kontakte, Aufgaben,
    /// Termine und Buchungen haengen.
    /// </summary>
    Documents = 10,

    /// <summary>
    /// Tourneen und Gastspiele: die Produktion des Hauses bei anderen. Steht
    /// auf Kontakten, Kalender, Finanzen, Personal und Dokumenten.
    /// </summary>
    Touring = 11,

    /// <summary>
    /// Produktionen: das Stueck als Klammer ueber Aufgaben, Termine, Belege,
    /// Dokumente und Beteiligte (ADR-0011). Touring waehlt daraus, was reist.
    /// </summary>
    Production = 12,
}

public static class SouffleurModules
{
    /// <summary>
    /// Ohne die Uebersicht gaebe es keine Startseite -- sie bleibt an.
    /// </summary>
    /// <remarks>
    /// Zwei Module tragen die anderen und bleiben deshalb an: ohne die Uebersicht
    /// gaebe es keine Startseite, ohne die Kontakte keinen Lieferanten an der
    /// Rechnung und keinen Foerderer am Projekt.
    /// </remarks>
    public static bool CanBeDisabled(this SouffleurModule module) =>
        module is not (SouffleurModule.Overview or SouffleurModule.Contacts);

    /// <summary>
    /// Was ein frisch aufgesetztes Haus bekommt. Ticketing und artwork sind aus,
    /// weil beide einen Zugang zu einem Fremdsystem brauchen, den es noch nicht
    /// gibt -- ein leeres Modul ist schlechter als keines. Touring ist aus,
    /// weil es eine Arbeitsweise voraussetzt, die nicht jedes Haus hat: Ein
    /// ortsfestes Theater reist nicht.
    /// </summary>
    public static bool IsOnByDefault(this SouffleurModule module) => module switch
    {
        SouffleurModule.Ticketing => false,
        SouffleurModule.Artwork => false,
        SouffleurModule.Touring => false,
        _ => true,
    };

    /// <summary>
    /// Die Module in der Reihenfolge der Reiter. Die Zahlenwerte sind
    /// Entstehungsreihenfolge und stehen in der Datenbank; wo etwas in der
    /// Leiste steht, ist eine Frage der Arbeit: Die Produktion kommt vor der
    /// Tournee, die aus ihr reist.
    /// </summary>
    public static readonly SouffleurModule[] All =
    [
        SouffleurModule.Overview,
        SouffleurModule.Finance,
        SouffleurModule.Personnel,
        SouffleurModule.Ticketing,
        SouffleurModule.Workspace,
        SouffleurModule.Artwork,
        SouffleurModule.Contacts,
        SouffleurModule.Chat,
        SouffleurModule.Calendar,
        SouffleurModule.Documents,
        SouffleurModule.Production,
        SouffleurModule.Touring,
    ];

    /// <summary>Beschriftung fuer Oberflaeche und Fehlermeldungen.</summary>
    public static string Label(this SouffleurModule module) => module switch
    {
        SouffleurModule.Overview => "Übersicht",
        SouffleurModule.Finance => "Finanzen",
        SouffleurModule.Personnel => "Personal",
        SouffleurModule.Ticketing => "Ticketing",
        SouffleurModule.Workspace => "Aufgaben",
        SouffleurModule.Artwork => "Spielplan aus artwork",
        SouffleurModule.Contacts => "Kontakte",
        SouffleurModule.Chat => "Chat",
        SouffleurModule.Calendar => "Kalender",
        SouffleurModule.Documents => "Dokumente",
        SouffleurModule.Touring => "Touring",
        SouffleurModule.Production => "Produktionen",
        _ => module.ToString(),
    };
}
