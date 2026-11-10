using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Banking;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Conversations;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Productions;
using Souffleur.Domain.Resources;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Documents;

namespace Souffleur.Api.Features.Search;

/// <summary>Was die Quellen gemeinsam brauchen: Datum und Zeit, wie das Haus sie liest.</summary>
internal static class Anzeige
{
    private static readonly TimeZoneInfo Berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
    private static readonly CultureInfo De = CultureInfo.GetCultureInfo("de-DE");

    public static string Tag(DateOnly datum) => datum.ToString("dd.MM.yyyy", De);

    public static string Tag(DateTimeOffset zeitpunkt) =>
        TimeZoneInfo.ConvertTime(zeitpunkt, Berlin).ToString("dd.MM.yyyy", De);

    public static string Zeitpunkt(DateTimeOffset zeitpunkt) =>
        TimeZoneInfo.ConvertTime(zeitpunkt, Berlin).ToString("dd.MM.yyyy HH:mm", De);

    /// <summary>Der Kalendertag in Berlin, so wie der Kalender ihn in der Adresse erwartet.</summary>
    public static string IsoTag(DateTimeOffset zeitpunkt) =>
        TimeZoneInfo.ConvertTime(zeitpunkt, Berlin).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    public static string Betrag(decimal betrag) => betrag.ToString("N2", De) + " €";

    /// <summary>Lange Texte -- Beschreibungen, Nachrichten -- auf eine Zeile gekuerzt.</summary>
    public static string Kurz(string text, int laenge = 90)
    {
        string eine = string.Join(' ', text.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return eine.Length <= laenge ? eine : eine[..(laenge - 1)].TrimEnd() + "…";
    }

    public static string? Verbinde(params string?[] teile)
    {
        string zusammen = string.Join(" · ", teile.Where(t => !string.IsNullOrWhiteSpace(t)));
        return zusammen.Length == 0 ? null : zusammen;
    }
}

/// <summary>
/// Das Adressbuch. Laesst sich nicht abschalten, also immer dabei. Inaktive
/// Kontakte bleiben aussen vor -- wer sie sucht, findet sie im Adressbuch
/// unter „inaktive zeigen".
/// </summary>
public sealed class KontakteQuelle : ISuchquelle
{
    public string Key => "kontakte";
    public string Label => "Kontakte";
    public string Icon => "kontakte";

    public bool Offen(Suchlauf lauf) => true;

    public async Task<IReadOnlyList<Treffer>> SuchenAsync(Suchlauf lauf, CancellationToken token)
    {
        IQueryable<Contact> kontakte = lauf.Context.Contacts.AsNoTracking().Where(c => c.IsActive);

        List<Contact> funde = await lauf.Begriff
            .Eingrenzen(kontakte, c =>
                c.Name + " " + (c.GivenName ?? "") + " " + (c.Email ?? "") + " " + (c.Phone ?? "")
                + " " + (c.City ?? "") + " " + (c.Position ?? "") + " " + (c.Notes ?? ""))
            .Include(c => c.Roles)
            .OrderBy(c => c.Name)
            .Take(Suchlauf.HoechstensJeBereich)
            .ToListAsync(token);

        return
        [
            .. funde.Select(c => new Treffer(
                c.Id,
                c.Name,
                Anzeige.Verbinde(
                    string.Join(", ", c.Roles.Select(r => r.Role.Label())),
                    c.Position,
                    c.City,
                    c.Email),
                $"/kontakte?kontakt={c.Id}")),
        ];
    }
}

/// <summary>
/// Aufgaben, offene wie erledigte. Eine erledigte traegt in ihrem Link den
/// Schalter, der sie in der Liste sichtbar macht -- sonst spraenge man zu
/// einer Seite, auf der sie nicht steht.
/// </summary>
public sealed class AufgabenQuelle : ISuchquelle
{
    public string Key => "aufgaben";
    public string Label => "Aufgaben";
    public string Icon => "aufgaben";

    public bool Offen(Suchlauf lauf) => lauf.ModulAn(SouffleurModule.Workspace);

    public async Task<IReadOnlyList<Treffer>> SuchenAsync(Suchlauf lauf, CancellationToken token)
    {
        IQueryable<TaskItem> aufgaben = lauf.Context.TaskItems.AsNoTracking();

        List<TaskItem> funde = await lauf.Begriff
            .Eingrenzen(aufgaben, t =>
                t.Title + " " + (t.Description ?? "")
                + " " + (t.Assignee != null ? t.Assignee.Name : "")
                + " " + (t.Group != null ? t.Group.Name : ""))
            .Include(t => t.Assignee)
            .Include(t => t.Group)
            // Offene zuerst, darin die naechste Frist vorn.
            .OrderBy(t => t.State == TaskState.Done || t.State == TaskState.Dropped)
            .ThenBy(t => t.DueDate == null)
            .ThenBy(t => t.DueDate)
            .Take(Suchlauf.HoechstensJeBereich)
            .ToListAsync(token);

        return
        [
            .. funde.Select(t => new Treffer(
                t.Id,
                t.Title,
                Anzeige.Verbinde(
                    t.State.Label(),
                    t.DueDate is { } frist ? "bis " + Anzeige.Tag(frist) : null,
                    t.Assignee?.Name ?? t.Group?.Name),
                t.IsOpen ? $"/aufgaben?aufgabe={t.Id}" : $"/aufgaben?aufgabe={t.Id}&erledigte=1")),
        ];
    }
}

/// <summary>
/// Die Produktionen: Titel und Untertitel, dazu wer mitwirkt. Der Treffer
/// fuehrt auf die aufgeklappte Produktion -- dort haengt alles andere dran.
/// </summary>
public sealed class ProduktionenQuelle : ISuchquelle
{
    public string Key => "produktionen";
    public string Label => "Produktionen";
    public string Icon => "produktion";

    public bool Offen(Suchlauf lauf) => lauf.ModulAn(SouffleurModule.Production);

    public async Task<IReadOnlyList<Treffer>> SuchenAsync(Suchlauf lauf, CancellationToken token)
    {
        IQueryable<Production> produktionen = lauf.Context.Productions.AsNoTracking();

        List<Production> funde = await lauf.Begriff
            .Eingrenzen(produktionen, p => p.Title + " " + (p.Subtitle ?? "") + " " + (p.Notes ?? ""),
                muster => p => p.Roles.Any(r =>
                    EF.Functions.Like(r.Name, muster, Suchbegriff.Escape)
                    || (r.Contact != null && EF.Functions.Like(r.Contact.Name, muster, Suchbegriff.Escape))))
            // Was laeuft, zuerst; Abgespieltes hinten.
            .OrderBy(p => p.Status == ProductionStatus.Archived)
            .ThenBy(p => p.PremiereDate == null)
            .ThenBy(p => p.PremiereDate)
            .Take(Suchlauf.HoechstensJeBereich)
            .ToListAsync(token);

        return
        [
            .. funde.Select(p => new Treffer(
                p.Id,
                p.Title,
                Anzeige.Verbinde(
                    p.Subtitle,
                    p.Status.Label(),
                    p.PremiereDate is { } premiere ? "Premiere " + Anzeige.Tag(premiere) : null),
                $"/produktionen?produktion={p.Id}")),
        ];
    }
}

/// <summary>
/// Termine und Serien. Der Link fuehrt auf den Tag im Kalender, nicht auf den
/// Datensatz: Dort steht der Termin in seinem Umfeld, und das ist meist das,
/// was man wissen wollte.
/// </summary>
public sealed class KalenderQuelle : ISuchquelle
{
    public string Key => "kalender";
    public string Label => "Kalender";
    public string Icon => "kalender";

    public bool Offen(Suchlauf lauf) => lauf.ModulAn(SouffleurModule.Calendar);

    public async Task<IReadOnlyList<Treffer>> SuchenAsync(Suchlauf lauf, CancellationToken token)
    {
        // Einzeltermine und Ausnahmen einer Serie; die Serie selbst daneben,
        // damit „Dienstbesprechung" nicht nur in einer verschobenen Woche auftaucht.
        List<CalendarEvent> termine = await lauf.Begriff
            .Eingrenzen(lauf.Context.CalendarEvents.AsNoTracking(), e =>
                e.Title + " " + (e.Description ?? "") + " " + (e.Location ?? ""))
            .OrderByDescending(e => e.StartsAt)
            .Take(Suchlauf.HoechstensJeBereich)
            .ToListAsync(token);

        List<EventSeries> serien = await lauf.Begriff
            .Eingrenzen(lauf.Context.EventSeries.AsNoTracking(), s =>
                s.Title + " " + (s.Description ?? "") + " " + (s.Location ?? ""))
            .OrderByDescending(s => s.StartDate)
            .Take(Suchlauf.HoechstensJeBereich)
            .ToListAsync(token);

        // Was noch kommt, vor dem, was war -- und das Juengste zuerst.
        DateTimeOffset jetzt = DateTimeOffset.UtcNow;

        IEnumerable<Treffer> ausTerminen = termine
            .OrderBy(e => e.StartsAt < jetzt)
            .ThenBy(e => e.StartsAt < jetzt ? jetzt - e.StartsAt : e.StartsAt - jetzt)
            .Select(e => new Treffer(
                e.Id,
                e.Title,
                Anzeige.Verbinde(
                    e.IsAllDay ? Anzeige.Tag(e.StartsAt) : Anzeige.Zeitpunkt(e.StartsAt),
                    e.Location,
                    e.Status == EventStatus.Confirmed ? null : e.Status.Label()),
                $"/kalender?tag={Anzeige.IsoTag(e.StartsAt)}&termin={e.Id}"));

        IEnumerable<Treffer> ausSerien = serien.Select(s => new Treffer(
            s.Id,
            s.Title,
            Anzeige.Verbinde("Serie ab " + Anzeige.Tag(s.StartDate), s.Location),
            $"/kalender?tag={s.StartDate:yyyy-MM-dd}&serie={s.Id}"));

        return [.. ausTerminen.Concat(ausSerien).Take(Suchlauf.HoechstensJeBereich)];
    }
}

/// <summary>
/// Die Ablage. Geht durch <see cref="DocumentService.Visible"/>, damit die
/// Suche nicht zeigt, was die Liste verbirgt.
/// </summary>
public sealed class DokumenteQuelle : ISuchquelle
{
    public string Key => "dokumente";
    public string Label => "Dokumente";
    public string Icon => "dokumente";

    public bool Offen(Suchlauf lauf) =>
        lauf.ModulAn(SouffleurModule.Documents) && lauf.Darf(Permission.DocumentsRead);

    public async Task<IReadOnlyList<Treffer>> SuchenAsync(Suchlauf lauf, CancellationToken token)
    {
        var viewer = new DocumentViewer(lauf.UserId, lauf.Darf(Permission.DocumentsManage));
        IQueryable<Document> sichtbar = lauf.Dienst<DocumentService>().Visible(viewer).AsNoTracking();

        // Die Schlagworte haengen als Zeilen am Dokument und passen nicht in
        // den Heuhaufen -- deshalb als zweite Bedingung je Wort.
        List<Document> funde = await lauf.Begriff
            .Eingrenzen(
                sichtbar,
                d => d.Name + " " + d.OriginalName + " " + (d.Note ?? ""),
                muster => d => d.Tags.Any(t => EF.Functions.Like(t.Tag, muster, Suchbegriff.Escape)))
            .Include(d => d.Tags)
            .OrderByDescending(d => d.CreatedAt)
            .Take(Suchlauf.HoechstensJeBereich)
            .ToListAsync(token);

        return
        [
            .. funde.Select(d => new Treffer(
                d.Id,
                d.Name,
                Anzeige.Verbinde(
                    Anzeige.Tag(d.CreatedAt),
                    d.Tags.Count > 0 ? string.Join(", ", d.Tags.Select(t => t.Tag)) : null,
                    d.IsRestricted ? "eingeschränkt" : null),
                $"/dokumente?dokument={d.Id}")),
        ];
    }
}

/// <summary>
/// Buchungen, Belege und Bankumsaetze -- ein Bereich, weil es ein Reiter ist.
/// Nur fuer die, die Buchungen lesen duerfen; alle anderen sehen den Bereich
/// gar nicht, wie bei den Endpunkten der Finanzen selbst.
/// </summary>
public sealed class FinanzenQuelle : ISuchquelle
{
    public string Key => "finanzen";
    public string Label => "Finanzen";
    public string Icon => "finanzen";

    public bool Offen(Suchlauf lauf) =>
        lauf.ModulAn(SouffleurModule.Finance) && lauf.Darf(Permission.FinanceRead);

    public async Task<IReadOnlyList<Treffer>> SuchenAsync(Suchlauf lauf, CancellationToken token)
    {
        // Drei kleine Abfragen statt einer Vereinigung: Die Tabellen haben
        // nichts gemeinsam, und je Art reichen wenige Zeilen.
        const int je = 4;

        List<Voucher> belege = await lauf.Begriff
            .Eingrenzen(lauf.Context.Vouchers.AsNoTracking(), v =>
                v.Description + " " + (v.ExternalNumber ?? "") + " " + v.Number.ToString()
                + " " + (v.Contact != null ? v.Contact.Name : ""))
            .Include(v => v.Contact)
            .OrderByDescending(v => v.Date)
            .Take(je)
            .ToListAsync(token);

        List<JournalEntry> buchungen = await lauf.Begriff
            .Eingrenzen(lauf.Context.JournalEntries.AsNoTracking(), j =>
                j.Description + " " + (j.DocumentNumber ?? "") + " " + j.Number.ToString())
            .OrderByDescending(j => j.Date)
            .Take(je)
            .ToListAsync(token);

        List<BankTransaction> umsaetze = await lauf.Begriff
            .Eingrenzen(lauf.Context.BankTransactions.AsNoTracking(), u =>
                (u.Purpose ?? "") + " " + (u.CounterpartyName ?? "") + " " + (u.CounterpartyIban ?? "")
                + " " + (u.Note ?? ""))
            .OrderByDescending(u => u.BookedOn)
            .Take(je)
            .ToListAsync(token);

        IEnumerable<Treffer> ausBelegen = belege.Select(v => new Treffer(
            v.Id,
            v.Description,
            Anzeige.Verbinde(
                "Beleg " + v.Number,
                Anzeige.Tag(v.Date),
                Anzeige.Betrag(v.GrossAmount),
                v.Contact?.Name),
            $"/finanzen/belege?beleg={v.Id}&jahr={v.FiscalYear}"));

        IEnumerable<Treffer> ausBuchungen = buchungen.Select(j => new Treffer(
            j.Id,
            j.Description,
            Anzeige.Verbinde(
                "Buchung " + j.Number,
                Anzeige.Tag(j.Date),
                j.DocumentNumber is { } nr ? "Beleg " + nr : null),
            $"/finanzen?buchung={j.Id}"));

        IEnumerable<Treffer> ausUmsaetzen = umsaetze.Select(u => new Treffer(
            u.Id,
            u.CounterpartyName ?? u.Purpose ?? "Umsatz",
            Anzeige.Verbinde(
                "Bankumsatz",
                Anzeige.Tag(u.BookedOn),
                Anzeige.Betrag(u.Amount),
                u.Purpose is { } zweck && u.CounterpartyName is not null ? Anzeige.Kurz(zweck, 50) : null),
            $"/finanzen/bank?umsatz={u.Id}&konto={u.BankAccountId}"));

        return [.. ausBelegen.Concat(ausBuchungen).Concat(ausUmsaetzen)];
    }
}

/// <summary>
/// Unterhaltungen und Nachrichten -- nur die, an denen diese Person
/// teilhaben darf: offene Kanaele und alles, worin sie Mitglied ist.
/// Zurueckgezogene Nachrichten sind weg, auch fuer die Suche.
/// </summary>
public sealed class ChatQuelle : ISuchquelle
{
    public string Key => "chat";
    public string Label => "Chat";
    public string Icon => "chat";

    public bool Offen(Suchlauf lauf) => lauf.ModulAn(SouffleurModule.Chat);

    public async Task<IReadOnlyList<Treffer>> SuchenAsync(Suchlauf lauf, CancellationToken token)
    {
        Guid ich = lauf.UserId ?? Guid.Empty;

        // Dieselbe Regel wie ConversationService.Visible -- dort steht sie an
        // einer Instanz haengend, hier brauchen wir sie als Abfrage.
        IQueryable<Conversation> sichtbar = lauf.Context.Conversations.AsNoTracking()
            .Where(c => !c.IsPrivate || c.Participants.Any(p => p.UserId == ich && p.LeftAt == null));

        List<Conversation> unterhaltungen = await lauf.Begriff
            .Eingrenzen(sichtbar, c => (c.Name ?? "") + " " + (c.Topic ?? ""))
            .OrderByDescending(c => c.LastMessageAt)
            .Take(3)
            .ToListAsync(token);

        IQueryable<Message> nachrichten = lauf.Context.Messages.AsNoTracking()
            .Where(m => m.RetractedAt == null)
            .Where(m => !m.Conversation.IsPrivate
                        || m.Conversation.Participants.Any(p => p.UserId == ich && p.LeftAt == null));

        List<Message> funde = await lauf.Begriff
            .Eingrenzen(nachrichten, m => m.Text)
            .Include(m => m.Author)
            .Include(m => m.Conversation)
            .OrderByDescending(m => m.CreatedAt)
            .Take(Suchlauf.HoechstensJeBereich)
            .ToListAsync(token);

        IEnumerable<Treffer> ausUnterhaltungen = unterhaltungen.Select(c => new Treffer(
            c.Id,
            c.Name ?? "Direktnachricht",
            Anzeige.Verbinde("Unterhaltung", c.Topic),
            $"/uebersicht?chat={c.Id}"));

        IEnumerable<Treffer> ausNachrichten = funde.Select(m => new Treffer(
            m.Id,
            Anzeige.Kurz(m.Text),
            Anzeige.Verbinde(
                m.Conversation.Name ?? "Direktnachricht",
                m.Author.GivenName + " " + m.Author.Surname,
                Anzeige.Zeitpunkt(m.CreatedAt)),
            $"/uebersicht?chat={m.ConversationId}"));

        return [.. ausUnterhaltungen.Concat(ausNachrichten).Take(Suchlauf.HoechstensJeBereich)];
    }
}

/// <summary>
/// Was das Haus einrichtet: Ressourcen, die Listen, und -- fuer die
/// Verwaltung -- Gruppen und Konten. Alles fuehrt in die Einstellungen.
/// </summary>
public sealed class HausQuelle : ISuchquelle
{
    public string Key => "haus";
    public string Label => "Einstellungen";
    public string Icon => "einstellungen";

    public bool Offen(Suchlauf lauf) => true;

    public async Task<IReadOnlyList<Treffer>> SuchenAsync(Suchlauf lauf, CancellationToken token)
    {
        const int je = 4;

        List<Resource> ressourcen = await lauf.Begriff
            .Eingrenzen(lauf.Context.Resources.AsNoTracking().Where(r => r.IsActive), r =>
                r.Name + " " + (r.ShortName ?? "") + " " + (r.Description ?? ""))
            .Include(r => r.Kind)
            .OrderBy(r => r.SortOrder)
            .Take(je)
            .ToListAsync(token);

        List<CatalogEntry> eintraege = await lauf.Begriff
            .Eingrenzen(lauf.Context.CatalogEntries.AsNoTracking().Where(e => e.IsActive), e =>
                e.Label + " " + (e.Code ?? "") + " " + (e.Description ?? ""))
            .OrderBy(e => e.Catalog)
            .ThenBy(e => e.SortOrder)
            .Take(je)
            .ToListAsync(token);

        var treffer = new List<Treffer>();

        treffer.AddRange(ressourcen.Select(r => new Treffer(
            r.Id,
            r.Name,
            Anzeige.Verbinde("Ressource", r.Kind?.Label, r.Capacity is { } plaetze ? plaetze + " Plätze" : null),
            $"/einstellungen/ressourcen?ressource={r.Id}")));

        treffer.AddRange(eintraege.Select(e => new Treffer(
            e.Id,
            e.Label,
            Anzeige.Verbinde("Liste", e.Catalog.Label(), e.Code),
            $"/einstellungen/listen/{e.Catalog}")));

        // Konten und Gruppen sieht nur, wer sie verwalten darf -- eine Liste
        // aller Anmeldeadressen ist nichts fuer jeden Angemeldeten.
        if (lauf.Darf(Permission.AccountsManage))
        {
            List<Group> gruppen = await lauf.Begriff
                .Eingrenzen(lauf.Context.Groups.AsNoTracking().Where(g => g.IsActive), g =>
                    g.Name + " " + (g.Note ?? ""))
                .OrderBy(g => g.Name)
                .Take(je)
                .ToListAsync(token);

            List<User> konten = await lauf.Begriff
                .Eingrenzen(lauf.Context.Users.AsNoTracking().Where(u => u.IsActive), u =>
                    u.GivenName + " " + u.Surname + " " + (u.Email ?? ""))
                .OrderBy(u => u.Surname)
                .ThenBy(u => u.GivenName)
                .Take(je)
                .ToListAsync(token);

            treffer.AddRange(gruppen.Select(g => new Treffer(
                g.Id, g.Name, Anzeige.Verbinde("Gruppe", g.Note), "/einstellungen/zugaenge")));

            treffer.AddRange(konten.Select(u => new Treffer(
                u.Id,
                u.Surname + ", " + u.GivenName,
                Anzeige.Verbinde("Konto", u.Email),
                "/einstellungen/zugaenge")));
        }

        return treffer;
    }
}
