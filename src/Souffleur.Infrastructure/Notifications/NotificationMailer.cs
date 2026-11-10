using System.Net;
using System.Net.Mail;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Souffleur.Domain.Notifications;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Notifications;

/// <summary>
/// Wie souffleur Mails verschickt. Steht in der .env des Hauses:
/// <c>Souffleur__Mail__Host</c>, <c>__Port</c>, <c>__User</c>,
/// <c>__Password</c>, <c>__From</c>, <c>__BaseUrl</c>.
/// </summary>
/// <remarks>
/// Ohne Host kein Versand -- und das ist der Normalfall fuer eine frische
/// Installation, kein Fehler. Die Glocke funktioniert trotzdem.
///
/// SMTP mit STARTTLS, also Port 587. Der eingebaute Versand von .NET kann
/// kein reines TLS auf Port 465; die meisten Anbieter koennen beides.
/// </remarks>
public class MailOptions
{
    public const string Section = "Souffleur:Mail";

    public string? Host { get; set; }

    public int Port { get; set; } = 587;

    public string? User { get; set; }

    public string? Password { get; set; }

    public string? From { get; set; }

    /// <summary>Die Adresse, unter der das Haus souffleur erreicht -- fuer die Links in der Mail.</summary>
    public string? BaseUrl { get; set; }

    /// <summary>Um wie viel Uhr die gesammelte Mail rausgeht, Ortszeit des Hauses.</summary>
    public int DigestHour { get; set; } = 7;

    public string TimeZone { get; set; } = "Europe/Berlin";

    /// <summary>
    /// Ohne Mailserver trotzdem „senden“: Die Mail landet im Protokoll statt
    /// beim Empfaenger. Nur fuer die Entwicklung gedacht, damit der Ablauf
    /// auf einem Laptop ohne Postausgang vorfuehrbar ist.
    /// </summary>
    public bool LogOnly { get; set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(Host) && !string.IsNullOrWhiteSpace(From);
}

/// <summary>
/// Verschickt die Mails zu Benachrichtigungen -- sofort oder gesammelt am
/// Morgen, je wie es die Person eingestellt hat.
/// </summary>
/// <remarks>
/// Laeuft einmal die Minute. Doppelt versandt wird nichts, weil
/// <see cref="Notification.EmailedAt"/> die Sperre ist und nicht ein
/// Merkzettel im Speicher: Startet der Dienst um halb acht neu, findet er
/// die Morgenmail als verschickt vor.
///
/// Was in der Glocke schon gelesen ist, geht nicht mehr per Mail raus -- die
/// Person weiss es bereits. Und was aelter als zwei Tage ist, auch nicht:
/// Wer die Mail erst einschaltet, soll nicht den Rueckstand eines Monats
/// bekommen.
///
/// Am Rand raeumt er auf: Benachrichtigungen aelter als neunzig Tage gehen.
/// </remarks>
public class NotificationMailer(
    IServiceScopeFactory scopes,
    IConfiguration configuration,
    TimeProvider time,
    ILogger<NotificationMailer> logger) : BackgroundService
{
    private static readonly TimeSpan Takt = TimeSpan.FromMinutes(1);
    private static readonly TimeSpan HoechstensAlt = TimeSpan.FromDays(2);
    private static readonly TimeSpan Aufbewahrung = TimeSpan.FromDays(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        MailOptions options = configuration.GetSection(MailOptions.Section).Get<MailOptions>() ?? new MailOptions();
        if (!options.IsConfigured)
        {
            logger.LogInformation("Kein Mailserver eingetragen -- Benachrichtigungen nur in der Glocke.");
        }

        using var timer = new PeriodicTimer(Takt, time);
        do
        {
            try
            {
                await DurchlaufAsync(options, stoppingToken);
            }
            catch (Exception exception) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(exception, "Benachrichtigungsversand: Durchlauf gescheitert, nächster in einer Minute.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    /// <summary>Ein Durchlauf. Oeffentlich, damit ein Test ihn ohne Warten ausloesen kann.</summary>
    public async Task DurchlaufAsync(MailOptions options, CancellationToken cancellationToken)
    {
        using IServiceScope scope = scopes.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();
        var sender = scope.ServiceProvider.GetRequiredService<IMailSender>();
        DateTimeOffset jetzt = time.GetUtcNow();

        await context.Notifications
            .Where(n => n.CreatedAt < jetzt - Aufbewahrung)
            .ExecuteDeleteAsync(cancellationToken);

        await FristenAsync(scope.ServiceProvider, context, options, jetzt, cancellationToken);

        if (!options.IsConfigured)
        {
            return;
        }

        // Wer ueberhaupt Mails will: alle Einstellungen, die nicht „aus" sind.
        List<string> schluessel = [.. NotificationKinds.All.Select(k => k.PreferenceKey())];
        var wuensche = await context.UserPreferences
            .Where(p => schluessel.Contains(p.Key) && p.Value != nameof(EmailMode.Off))
            .Select(p => new { p.UserId, p.Key, p.Value })
            .ToListAsync(cancellationToken);

        if (wuensche.Count == 0)
        {
            return;
        }

        bool morgens = IstMorgenStunde(options, jetzt);

        foreach (var person in wuensche.GroupBy(w => w.UserId))
        {
            var sofort = Arten(person.Where(w => w.Value == nameof(EmailMode.Immediate)).Select(w => w.Key));
            var taeglich = Arten(person.Where(w => w.Value == nameof(EmailMode.Daily)).Select(w => w.Key));

            var user = await context.Users.AsNoTracking()
                .FirstOrDefaultAsync(u => u.Id == person.Key && u.IsActive, cancellationToken);
            if (user?.Email is not { Length: > 0 } adresse)
            {
                continue;
            }

            IQueryable<Notification> offen = context.Notifications.Where(n =>
                n.UserId == person.Key
                && n.EmailedAt == null
                && n.ReadAt == null
                && n.CreatedAt > jetzt - HoechstensAlt);

            if (sofort.Count > 0)
            {
                foreach (Notification n in await offen.Where(n => sofort.Contains(n.Kind)).ToListAsync(cancellationToken))
                {
                    await sender.SendAsync(adresse, $"souffleur: {n.Title}", Text([n], options), cancellationToken);
                    n.EmailedAt = jetzt;
                    await context.SaveChangesAsync(cancellationToken);
                }
            }

            if (morgens && taeglich.Count > 0)
            {
                List<Notification> sammlung = await offen
                    .Where(n => taeglich.Contains(n.Kind))
                    .OrderBy(n => n.CreatedAt)
                    .ToListAsync(cancellationToken);

                if (sammlung.Count > 0)
                {
                    string betreff = sammlung.Count == 1
                        ? $"souffleur: {sammlung[0].Title}"
                        : $"souffleur: {sammlung.Count} neue Benachrichtigungen";
                    await sender.SendAsync(adresse, betreff, Text(sammlung, options), cancellationToken);
                    foreach (Notification n in sammlung)
                    {
                        n.EmailedAt = jetzt;
                    }

                    await context.SaveChangesAsync(cancellationToken);
                }
            }
        }
    }

    /// <summary>
    /// Erinnert einmal am Tag an Aufgaben, die morgen faellig sind -- an wer
    /// zustaendig ist und an die Gruppe.
    /// </summary>
    /// <remarks>
    /// Einmal heisst: Ob die Erinnerung heute schon entstanden ist, steht in
    /// der Tabelle selbst, nicht in einem Merkzettel im Speicher. Ein
    /// Neustart erinnert deshalb nicht doppelt.
    ///
    /// Vor der Morgenmail, damit die Erinnerung in der Sammlung mitkommt.
    /// </remarks>
    private static async Task FristenAsync(
        IServiceProvider services,
        SouffleurDbContext context,
        MailOptions options,
        DateTimeOffset jetzt,
        CancellationToken cancellationToken)
    {
        TimeZoneInfo zone = Zone(options);
        DateTimeOffset ortszeit = TimeZoneInfo.ConvertTime(jetzt, zone);
        if (ortszeit.Hour < Math.Max(0, options.DigestHour - 1))
        {
            return;
        }

        DateOnly morgen = DateOnly.FromDateTime(ortszeit.Date).AddDays(1);
        DateTimeOffset heuteBeginn = new DateTimeOffset(ortszeit.Date, ortszeit.Offset).ToUniversalTime();

        var faellig = await context.TaskItems.AsNoTracking()
            .Where(t => t.DueDate == morgen
                        && t.State != Domain.Work.TaskState.Done
                        && t.State != Domain.Work.TaskState.Dropped)
            .Select(t => new { t.Id, t.Title, t.AssigneeId, t.GroupId })
            .ToListAsync(cancellationToken);

        if (faellig.Count == 0)
        {
            return;
        }

        var glocke = services.GetRequiredService<NotificationService>();
        foreach (var aufgabe in faellig)
        {
            string titel = $"Morgen fällig: {aufgabe.Title}";

            List<Guid> wer = [];
            if (aufgabe.AssigneeId is { } kontakt)
            {
                wer.AddRange(await glocke.UsersOfContactsAsync([kontakt], cancellationToken));
            }

            if (aufgabe.GroupId is { } gruppe)
            {
                wer.AddRange(await glocke.UsersOfGroupsAsync([gruppe], cancellationToken));
            }

            // Wer heute schon erinnert wurde, nicht noch einmal.
            List<Guid> schonErinnert = await context.Notifications
                .Where(n => n.Title == titel && n.CreatedAt >= heuteBeginn)
                .Select(n => n.UserId)
                .ToListAsync(cancellationToken);

            await glocke.NotifyAsync(
                wer.Except(schonErinnert),
                new NotificationDraft(NotificationKind.Task, titel, Link: "/aufgaben"),
                cancellationToken);
        }
    }

    private static TimeZoneInfo Zone(MailOptions options)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone);
        }
        catch (TimeZoneNotFoundException)
        {
            return TimeZoneInfo.Utc;
        }
    }

    private static List<NotificationKind> Arten(IEnumerable<string> schluessel) =>
        [.. NotificationKinds.All.Where(k => schluessel.Contains(k.PreferenceKey()))];

    /// <summary>Die volle Stunde der Morgenmail, in der Zeitzone des Hauses.</summary>
    private static bool IstMorgenStunde(MailOptions options, DateTimeOffset jetzt) =>
        TimeZoneInfo.ConvertTime(jetzt, Zone(options)).Hour == options.DigestHour;

    /// <summary>Schlichter Text, keine HTML-Mail: Sie kommt ueberall an und sieht ueberall gleich aus.</summary>
    private static string Text(IReadOnlyList<Notification> liste, MailOptions options)
    {
        var text = new StringBuilder();
        foreach (Notification n in liste)
        {
            text.AppendLine(n.Title);
            if (!string.IsNullOrWhiteSpace(n.Body))
            {
                text.AppendLine("  " + n.Body);
            }

            if (n.Link is { } link && options.BaseUrl is { Length: > 0 } basis)
            {
                text.AppendLine("  " + basis.TrimEnd('/') + link);
            }

            text.AppendLine();
        }

        text.AppendLine("—");
        text.AppendLine("Welche Mails du bekommst, stellst du in souffleur unter „Benachrichtigungen“ ein.");
        return text.ToString();
    }
}

/// <summary>Verschickt eine Mail. Eigene Schnittstelle, damit Tests nichts versenden.</summary>
public interface IMailSender
{
    /// <summary>Ob und wie Mails das Haus verlassen.</summary>
    MailSendMode Mode { get; }

    Task SendAsync(IReadOnlyList<string> to, string subject, string body, CancellationToken cancellationToken = default);
}

public enum MailSendMode
{
    /// <summary>Kein Mailserver eingetragen: Senden ist nicht moeglich.</summary>
    Off,

    /// <summary>Ueber den Mailserver des Hauses.</summary>
    Smtp,

    /// <summary>
    /// Nur ins Protokoll -- fuer die Entwicklung, wo es keinen Mailserver
    /// gibt, der Ablauf aber trotzdem vorfuehrbar sein soll.
    /// </summary>
    Log,
}

public static class MailSenderExtensions
{
    public static Task SendAsync(this IMailSender sender, string to, string subject, string body, CancellationToken cancellationToken = default) =>
        sender.SendAsync([to], subject, body, cancellationToken);
}

public class SmtpMailSender(IConfiguration configuration, ILogger<SmtpMailSender> logger) : IMailSender
{
    private MailOptions Options =>
        configuration.GetSection(MailOptions.Section).Get<MailOptions>() ?? new MailOptions();

    public MailSendMode Mode => Options switch
    {
        { IsConfigured: true } => MailSendMode.Smtp,
        { LogOnly: true } => MailSendMode.Log,
        _ => MailSendMode.Off,
    };

    public async Task SendAsync(IReadOnlyList<string> to, string subject, string body, CancellationToken cancellationToken = default)
    {
        MailOptions options = Options;
        if (!options.IsConfigured)
        {
            if (options.LogOnly)
            {
                logger.LogInformation("Mail nicht verschickt (kein Mailserver): an {An}, Betreff „{Betreff}“", string.Join(", ", to), subject);
            }

            return;
        }

        using var client = new SmtpClient(options.Host, options.Port) { EnableSsl = true };
        if (!string.IsNullOrEmpty(options.User))
        {
            client.Credentials = new NetworkCredential(options.User, options.Password);
        }

        using var message = new MailMessage
        {
            From = new MailAddress(options.From!),
            Subject = subject,
            Body = body,
            BodyEncoding = Encoding.UTF8,
            SubjectEncoding = Encoding.UTF8,
        };
        foreach (string address in to)
        {
            message.To.Add(address);
        }

        await client.SendMailAsync(message, cancellationToken);
    }
}
