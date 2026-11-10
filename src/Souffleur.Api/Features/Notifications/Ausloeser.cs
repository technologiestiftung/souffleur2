using Souffleur.Domain.Notifications;
using Souffleur.Domain.Personnel;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Notifications;

namespace Souffleur.Api.Features.Notifications;

/// <summary>
/// Wann die Glocke klingelt. An einer Stelle, damit die Texte sich gleichen
/// und niemand suchen muss, warum etwas benachrichtigt -- oder nicht.
/// </summary>
/// <remarks>
/// Gemeldet wird nur, was sich geaendert hat. Wer eine Aufgabe umbenennt,
/// die schon Anna gehoert, schickt Anna keinen zweiten Hinweis.
/// </remarks>
public static class Ausloeser
{
    /// <summary>Aufgabe an mich oder meine Gruppe -- neu, umgehaengt oder als Beteiligter dazugekommen.</summary>
    public static async Task AufgabeAsync(
        NotificationService glocke,
        TaskItem aufgabe,
        Guid? vorherZustaendig,
        Guid? vorherGruppe,
        IReadOnlyCollection<Guid>? vorherBeteiligt = null,
        CancellationToken token = default)
    {
        string wer = await glocke.ActorNameAsync(token);
        string link = "/aufgaben";
        string? frist = aufgabe.DueDate is { } d ? $"fällig {d:dd.MM.yyyy}" : null;

        // Beteiligte: nur wer neu dazukam, und nicht der Zustaendige selbst --
        // der hoert es schon als Zustaendiger.
        List<Guid> neuBeteiligt = [.. aufgabe.Participants
            .Select(p => p.ContactId)
            .Where(id => id != aufgabe.AssigneeId && !(vorherBeteiligt ?? []).Contains(id))];
        if (neuBeteiligt.Count > 0)
        {
            await glocke.NotifyAsync(
                await glocke.UsersOfContactsAsync(neuBeteiligt, token),
                new NotificationDraft(NotificationKind.Task, $"{wer} hat dich an einer Aufgabe beteiligt: {aufgabe.Title}", frist, link),
                token);
        }

        if (aufgabe.AssigneeId is { } kontakt && kontakt != vorherZustaendig)
        {
            await glocke.NotifyAsync(
                await glocke.UsersOfContactsAsync([kontakt], token),
                new NotificationDraft(NotificationKind.Task, $"{wer} hat dir eine Aufgabe gegeben: {aufgabe.Title}", frist, link),
                token);
        }

        if (aufgabe.GroupId is { } gruppe && gruppe != vorherGruppe)
        {
            string name = aufgabe.Group?.Name ?? "deine Gruppe";
            await glocke.NotifyAsync(
                await glocke.UsersOfGroupsAsync([gruppe], token),
                new NotificationDraft(NotificationKind.Task, $"Neue Aufgabe für {name}: {aufgabe.Title}", frist, link),
                token);
        }
    }

    /// <summary>Eingeteilt oder ausgetragen.</summary>
    public static async Task DienstAsync(
        NotificationService glocke,
        IEnumerable<Guid> kontakte,
        bool eingeteilt,
        string termin,
        DateTimeOffset beginn,
        CancellationToken token = default)
    {
        string wer = await glocke.ActorNameAsync(token);
        string titel = eingeteilt
            ? $"{wer} hat dich eingeteilt: {termin}"
            : $"{wer} hat dich ausgetragen: {termin}";
        await glocke.NotifyAsync(
            await glocke.UsersOfContactsAsync(kontakte, token),
            new NotificationDraft(NotificationKind.Duty, titel, Wann(beginn), "/personal"),
            token);
    }

    /// <summary>Ein Termin, an dem man eingeteilt ist, hat sich verschoben.</summary>
    public static async Task VerschobenAsync(
        NotificationService glocke,
        IEnumerable<Guid> kontakte,
        string termin,
        DateTimeOffset neuerBeginn,
        CancellationToken token = default) =>
        await glocke.NotifyAsync(
            await glocke.UsersOfContactsAsync(kontakte, token),
            new NotificationDraft(NotificationKind.Duty, $"Verschoben: {termin}", $"jetzt {Wann(neuerBeginn)}", "/personal"),
            token);

    /// <summary>Ein Dokument fuer eine meiner Gruppen.</summary>
    public static async Task DokumentAsync(
        NotificationService glocke,
        string name,
        IEnumerable<(Guid Id, string Name)> gruppen,
        CancellationToken token = default)
    {
        List<(Guid Id, string Name)> liste = [.. gruppen];
        if (liste.Count == 0)
        {
            return;
        }

        string wer = await glocke.ActorNameAsync(token);
        await glocke.NotifyAsync(
            await glocke.UsersOfGroupsAsync(liste.Select(g => g.Id), token),
            new NotificationDraft(
                NotificationKind.Document,
                $"{wer} hat ein Dokument abgelegt: {name}",
                "für " + string.Join(", ", liste.Select(g => g.Name)),
                "/dokumente"),
            token);
    }

    /// <summary>Erwaehnt im Chat.</summary>
    public static async Task ErwaehntAsync(
        NotificationService glocke,
        IEnumerable<Guid> personen,
        IEnumerable<Guid> gruppen,
        string wo,
        string text,
        Guid conversationId,
        CancellationToken token = default)
    {
        string wer = await glocke.ActorNameAsync(token);
        List<Guid> alle = [.. personen, .. await glocke.UsersOfGroupsAsync(gruppen, token)];
        await glocke.NotifyAsync(
            alle,
            new NotificationDraft(
                NotificationKind.Mention,
                $"{wer} hat dich in {wo} erwähnt",
                text,
                $"/uebersicht?chat={conversationId}"),
            token);
    }

    /// <summary>
    /// Ein Antrag ist gestellt oder eine Krankheit gemeldet -- an alle, die
    /// entscheiden. Der Name steht im Titel und nicht „du": Wer entscheidet,
    /// bekommt viele davon.
    /// </summary>
    public static async Task AbwesenheitGemeldetAsync(
        NotificationService glocke,
        IEnumerable<Guid> entscheidende,
        Absence abwesenheit,
        CancellationToken token = default)
    {
        string name = abwesenheit.Contact?.Name ?? "Jemand";
        string titel = abwesenheit.Kind.IsSickness()
            ? $"{name}: {abwesenheit.Kind.Label()} gemeldet"
            : $"{name} beantragt {abwesenheit.Kind.Label()}";

        await glocke.NotifyAsync(
            [.. entscheidende],
            new NotificationDraft(NotificationKind.Absence, titel, Zeitraum(abwesenheit), AbwesenheitLink),
            token);
    }

    /// <summary>Genehmigt oder abgelehnt -- an die Person, um die es geht.</summary>
    public static async Task AbwesenheitEntschiedenAsync(
        NotificationService glocke,
        Absence abwesenheit,
        CancellationToken token = default)
    {
        string wer = await glocke.ActorNameAsync(token);
        string was = abwesenheit.Status == AbsenceStatus.Approved ? "genehmigt" : "abgelehnt";
        string text = abwesenheit.DecisionNote is { } grund
            ? $"{Zeitraum(abwesenheit)} · {grund}"
            : Zeitraum(abwesenheit);

        await glocke.NotifyAsync(
            await glocke.UsersOfContactsAsync([abwesenheit.ContactId], token),
            new NotificationDraft(
                NotificationKind.Absence,
                $"{wer} hat {abwesenheit.Kind.Label()} {was}",
                text,
                AbwesenheitLink),
            token);
    }

    /// <summary>
    /// Etwas, das schon galt, ist aufgehoben. Wer es nicht selbst getan hat,
    /// soll es erfahren: die Person, wenn jemand anderes aufhebt, und wer
    /// entscheidet, wenn die Person selbst zurueckzieht.
    /// </summary>
    public static async Task AbwesenheitAufgehobenAsync(
        NotificationService glocke,
        IEnumerable<Guid> empfaenger,
        Absence abwesenheit,
        CancellationToken token = default)
    {
        string wer = await glocke.ActorNameAsync(token);
        string name = abwesenheit.Contact?.Name ?? "jemand";

        await glocke.NotifyAsync(
            [.. empfaenger],
            new NotificationDraft(
                NotificationKind.Absence,
                $"{wer} hat {abwesenheit.Kind.Label()} von {name} aufgehoben",
                abwesenheit.DecisionNote is { } grund ? $"{Zeitraum(abwesenheit)} · {grund}" : Zeitraum(abwesenheit),
                AbwesenheitLink),
            token);
    }

    private const string AbwesenheitLink = "/personal?ansicht=abwesenheiten";

    private static string Zeitraum(Absence abwesenheit) =>
        abwesenheit.StartDate == abwesenheit.EndDate
            ? $"{abwesenheit.StartDate:dd.MM.yyyy}{(abwesenheit.IsHalfDay ? ", halber Tag" : "")}"
            : $"{abwesenheit.StartDate:dd.MM.}–{abwesenheit.EndDate:dd.MM.yyyy}";

    private static string Wann(DateTimeOffset beginn)
    {
        TimeZoneInfo zone;
        try
        {
            zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        }
        catch (TimeZoneNotFoundException)
        {
            zone = TimeZoneInfo.Utc;
        }

        return TimeZoneInfo.ConvertTime(beginn, zone).ToString("ddd, dd.MM. 'um' HH:mm", new System.Globalization.CultureInfo("de-DE"));
    }
}
