namespace Souffleur.Infrastructure.Accounting.Datev;

/// <summary>
/// Die Angaben, die das Steuerbuero vorgibt. Ohne sie laesst sich kein Stapel
/// erzeugen, den DATEV annimmt -- geraten werden darf hier nichts.
/// </summary>
public class DatevOptions
{
    public const string Section = "Souffleur:Datev";

    /// <summary>
    /// Beraternummer des Steuerbueros, 1001 bis 9999999. Kommt vom Steuerbuero.
    /// </summary>
    public int ConsultantNumber { get; set; }

    /// <summary>
    /// Mandantennummer des Hauses beim Steuerbuero, 1 bis 99999.
    /// </summary>
    public int ClientNumber { get; set; }

    /// <summary>
    /// Laenge der Sachkontonummern, ueblich 4. Muss zur Kontenliste des
    /// Steuerbueros passen, sonst ordnet DATEV die Konten falsch zu.
    /// </summary>
    public int AccountLength { get; set; } = 4;

    /// <summary>Kuerzel des Bearbeiters, hoechstens zwei Zeichen.</summary>
    public string? Initials { get; set; }

    public bool IsComplete =>
        ConsultantNumber is >= 1001 and <= 9999999
        && ClientNumber is >= 1 and <= 99999
        && AccountLength is >= 4 and <= 9;

    public IReadOnlyList<string> Problems()
    {
        List<string> problems = [];

        if (ConsultantNumber is < 1001 or > 9999999)
        {
            problems.Add("Die Beraternummer fehlt oder liegt außerhalb von 1001 bis 9999999. Sie kommt vom Steuerbüro.");
        }

        if (ClientNumber is < 1 or > 99999)
        {
            problems.Add("Die Mandantennummer fehlt oder liegt außerhalb von 1 bis 99999. Sie kommt vom Steuerbüro.");
        }

        if (AccountLength is < 4 or > 9)
        {
            problems.Add("Die Sachkontenlänge muss zwischen 4 und 9 liegen.");
        }

        return problems;
    }
}
