using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Work;
using Souffleur.Domain.Notifications;
using Souffleur.Infrastructure.Notifications;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Die Glocke und die Mails dazu. Das Heikle ist nicht das Schicken,
/// sondern das Nicht-Schicken: nicht doppelt, nicht Gelesenes, nicht an sich
/// selbst, nicht ungefragt.
/// </summary>
public class NotificationTests : IAsyncLifetime
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    private readonly Guid anna = Guid.CreateVersion7();
    private readonly Guid ben = Guid.CreateVersion7();
    private readonly Uhr uhr = new(new DateTimeOffset(2026, 11, 3, 14, 0, 0, TimeSpan.Zero));
    private readonly Wer wer = new();
    private readonly Briefkasten post = new();

    private ServiceProvider services = null!;

    private static readonly MailOptions Mail = new()
    {
        Host = "smtp.example.org",
        From = "souffleur@haus.example",
        BaseUrl = "https://souffleur.haus.example",
        DigestHour = 7,
        TimeZone = "UTC",
    };

    public async Task InitializeAsync()
    {
        await connection.OpenAsync();
        services = new ServiceCollection()
            .AddSingleton<TimeProvider>(uhr)
            .AddSingleton<ICurrentUser>(wer)
            .AddSingleton<IMailSender>(post)
            .AddSingleton<INotificationPusher, NoPusher>()
            .AddDbContext<SouffleurDbContext>(o => o.UseSqlite(connection))
            .AddLogging()
            .AddScoped<NotificationService>()
            .BuildServiceProvider();

        using IServiceScope scope = services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<SouffleurDbContext>();
        await context.Database.EnsureCreatedAsync();
        context.Users.AddRange(
            new User { Id = anna, GivenName = "Anna", Surname = "Krause", UserName = "anna", Email = "anna@haus.example" },
            new User { Id = ben, GivenName = "Ben", Surname = "Schröder", UserName = "ben", Email = "ben@haus.example" });
        await context.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        await connection.DisposeAsync();
    }

    private async Task<T> MitAsync<T>(Func<NotificationService, SouffleurDbContext, Task<T>> tun)
    {
        using IServiceScope scope = services.CreateScope();
        return await tun(
            scope.ServiceProvider.GetRequiredService<NotificationService>(),
            scope.ServiceProvider.GetRequiredService<SouffleurDbContext>());
    }

    private Task MeldenAsync(NotificationKind art = NotificationKind.Task, params Guid[] an) =>
        MitAsync(async (s, _) =>
        {
            await s.NotifyAsync(an, new NotificationDraft(art, "Leiter prüfen", "bis Freitag", "/aufgaben"));
            return 0;
        });

    private Task EinstellenAsync(Guid fuer, NotificationKind art, EmailMode mode)
    {
        wer.Id = fuer;
        return MitAsync(async (s, _) =>
        {
            await s.SaveSettingsAsync(new Dictionary<NotificationKind, EmailMode> { [art] = mode });
            return 0;
        });
    }

    private Task DurchlaufAsync()
    {
        var mailer = new NotificationMailer(
            services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            uhr,
            NullLogger<NotificationMailer>.Instance);
        return mailer.DurchlaufAsync(Mail, CancellationToken.None);
    }

    [Fact]
    public async Task Wer_handelt_bekommt_keinen_Hinweis_auf_sich_selbst()
    {
        wer.Id = anna;
        await MeldenAsync(an: [anna, ben]);

        wer.Id = anna;
        (await MitAsync((s, _) => s.MineAsync())).Unread.Should().Be(0);
        wer.Id = ben;
        (await MitAsync((s, _) => s.MineAsync())).Unread.Should().Be(1);
    }

    [Fact]
    public async Task Gelesen_ist_es_nur_fuer_die_eine_Person()
    {
        wer.Id = null;
        await MeldenAsync(an: [anna, ben]);

        wer.Id = anna;
        var (annas, _) = await MitAsync((s, _) => s.MineAsync());
        await MitAsync(async (s, _) => { await s.MarkReadAsync(annas[0].Id); return 0; });

        (await MitAsync((s, _) => s.MineAsync())).Unread.Should().Be(0);
        wer.Id = ben;
        (await MitAsync((s, _) => s.MineAsync())).Unread.Should().Be(1);
    }

    [Fact]
    public async Task Ohne_Einstellung_geht_keine_Mail_raus()
    {
        wer.Id = null;
        await MeldenAsync(an: [anna]);

        await DurchlaufAsync();

        post.Briefe.Should().BeEmpty("die Vorgabe ist: nur die Glocke");
    }

    [Fact]
    public async Task Sofort_heisst_einmal_und_nicht_bei_jedem_Durchlauf()
    {
        await EinstellenAsync(anna, NotificationKind.Task, EmailMode.Immediate);
        wer.Id = null;
        await MeldenAsync(an: [anna]);

        await DurchlaufAsync();
        await DurchlaufAsync();

        post.Briefe.Should().ContainSingle();
        post.Briefe[0].An.Should().Be("anna@haus.example");
        post.Briefe[0].Betreff.Should().Be("souffleur: Leiter prüfen");
        post.Briefe[0].Text.Should().Contain("https://souffleur.haus.example/aufgaben");
    }

    [Fact]
    public async Task Was_in_der_Glocke_schon_gelesen_ist_kommt_nicht_mehr_per_Mail()
    {
        await EinstellenAsync(anna, NotificationKind.Task, EmailMode.Immediate);
        wer.Id = null;
        await MeldenAsync(an: [anna]);
        wer.Id = anna;
        await MitAsync(async (s, _) => { await s.MarkAllReadAsync(); return 0; });

        await DurchlaufAsync();

        post.Briefe.Should().BeEmpty();
    }

    [Fact]
    public async Task Taeglich_sammelt_bis_zur_Morgenstunde_und_schickt_dann_eine_Mail()
    {
        await EinstellenAsync(anna, NotificationKind.Task, EmailMode.Daily);
        wer.Id = null;
        await MeldenAsync(an: [anna]);
        await MeldenAsync(an: [anna]);

        await DurchlaufAsync();
        post.Briefe.Should().BeEmpty("es ist 14 Uhr");

        uhr.Jetzt = new DateTimeOffset(2026, 11, 4, 7, 5, 0, TimeSpan.Zero);
        await DurchlaufAsync();
        await DurchlaufAsync();

        post.Briefe.Should().ContainSingle().Which.Betreff.Should().Be("souffleur: 2 neue Benachrichtigungen");
    }

    [Fact]
    public async Task Wer_die_Mail_spaet_einschaltet_bekommt_keinen_Rueckstand()
    {
        wer.Id = null;
        await MeldenAsync(an: [anna]);
        uhr.Jetzt = uhr.Jetzt.AddDays(5);
        await EinstellenAsync(anna, NotificationKind.Task, EmailMode.Immediate);

        await DurchlaufAsync();

        post.Briefe.Should().BeEmpty();
    }

    [Fact]
    public async Task Die_Wahl_gilt_je_Art()
    {
        await EinstellenAsync(anna, NotificationKind.Mention, EmailMode.Immediate);
        wer.Id = null;
        await MeldenAsync(NotificationKind.Task, an: [anna]);
        await MeldenAsync(NotificationKind.Mention, an: [anna]);

        await DurchlaufAsync();

        post.Briefe.Should().ContainSingle();
    }

    [Fact]
    public async Task An_eine_Frist_morgen_wird_einmal_erinnert_auch_ohne_Mailserver()
    {
        await MitAsync(async (_, context) =>
        {
            var kontakt = new Contact
            {
                Kind = ContactKind.Person,
                Name = "Krause, Anna",
                UserId = anna,
                Roles = [new ContactRoleAssignment { Role = ContactRole.Staff }],
            };
            context.Contacts.Add(kontakt);
            context.TaskItems.Add(new TaskItem
            {
                Title = "Abrechnung",
                AssigneeId = kontakt.Id,
                DueDate = DateOnly.FromDateTime(uhr.Jetzt.Date).AddDays(1),
            });
            await context.SaveChangesAsync();
            return 0;
        });

        var ohneMail = new MailOptions { DigestHour = 7, TimeZone = "UTC" };
        var mailer = new NotificationMailer(
            services.GetRequiredService<IServiceScopeFactory>(),
            new ConfigurationBuilder().Build(),
            uhr,
            NullLogger<NotificationMailer>.Instance);

        await mailer.DurchlaufAsync(ohneMail, CancellationToken.None);
        await mailer.DurchlaufAsync(ohneMail, CancellationToken.None);

        wer.Id = anna;
        var (liste, _) = await MitAsync((s, _) => s.MineAsync());
        liste.Should().ContainSingle().Which.Title.Should().Be("Morgen fällig: Abrechnung");
    }

    private sealed class Uhr(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Jetzt { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Jetzt;
    }

    private sealed class Wer : ICurrentUser
    {
        public Guid? Id { get; set; }
    }

    private sealed class Briefkasten : IMailSender
    {
        public List<(string An, string Betreff, string Text)> Briefe { get; } = [];

        public MailSendMode Mode => MailSendMode.Smtp;

        public Task SendAsync(IReadOnlyList<string> to, string subject, string body, CancellationToken cancellationToken = default)
        {
            Briefe.Add((string.Join(", ", to), subject, body));
            return Task.CompletedTask;
        }
    }
}
