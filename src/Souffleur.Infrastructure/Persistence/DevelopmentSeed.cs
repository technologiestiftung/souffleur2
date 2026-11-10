using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Bookmarks;
using Souffleur.Domain.Banking;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Mail;
using Souffleur.Domain.Organizations;
using Souffleur.Domain.Personnel;
using Souffleur.Domain.Productions;
using Souffleur.Domain.Resources;
using Souffleur.Domain.Work;
using Souffleur.Domain.Conversations;
using Souffleur.Domain.Identity;
using Microsoft.AspNetCore.Identity;
using Souffleur.Infrastructure.Accounting;

namespace Souffleur.Infrastructure.Persistence;

/// <summary>
/// Füllt eine leere Datenbank mit Beispieldaten, damit sich die Oberfläche
/// ansehen lässt. Läuft nur in der Entwicklung.
/// </summary>
/// <remarks>
/// Die Konten sind SKR03-Nummern, wie sie ein Theater braucht. Sie ersetzen
/// **nicht** den Kontenrahmen des Steuerbueros -- fuer den Betrieb wird die
/// Kontenliste eingelesen (siehe docs/datev-export.md).
/// </remarks>
public static class DevelopmentSeed
{
    public static async Task RunAsync(
        SouffleurDbContext context,
        JournalService journal,
        ILogger logger,
        HouseProfile? profile = null,
        CancellationToken cancellationToken = default)
    {
        if (await context.Accounts.AnyAsync(cancellationToken))
        {
            return;
        }

        logger.LogInformation("Entwicklungsdaten werden angelegt.");

        context.Organizations.Add(new Organization
        {
            Name = profile?.Name ?? "Theater am Kanal",
            LegalForm = profile?.LegalForm ?? "GmbH",
            Street = profile?.Street ?? "Kantstraße 12a",
            PostalCode = profile?.PostalCode ?? "10623",
            City = profile?.City ?? "Berlin",
            ChartOfAccounts = ChartOfAccounts.Skr03,
            IsNonProfit = false,
        });

        Account bank = Add(context, "1200", "Bank", AccountType.Asset);
        Account cash = Add(context, "1000", "Kasse", AccountType.Asset);
        Account rent = Add(context, "4210", "Miete", AccountType.Expense);
        Account fees = Add(context, "4310", "Gagen und Honorare", AccountType.Expense);
        Account energy = Add(context, "4240", "Strom, Gas, Wasser", AccountType.Expense);
        Account material = Add(context, "4985", "Werkstattmaterial", AccountType.Expense);
        Account printing = Add(context, "4600", "Werbung und Drucksachen", AccountType.Expense);
        Account insurance = Add(context, "4360", "Versicherungen", AccountType.Expense);
        Account tickets = Add(context, "8300", "Erlöse Eintritt", AccountType.Revenue);
        Account grants = Add(context, "8200", "Zuschüsse", AccountType.Revenue);

        CostCenter admin = AddCostCenter(context, "100", "Verwaltung");
        CostCenter sales = AddCostCenter(context, "200", "Vertrieb und Marketing");
        CostCenter artistic = AddCostCenter(context, "300", "Künstlerischer Betrieb");
        CostCenter workshop = AddCostCenter(context, "400", "Werkstatt");

        context.Contacts.AddRange(
            Supplier("Vattenfall Europe Sales", "Berlin", 14),
            Supplier("Druckerei Conrad", "Berlin", 30),
            Supplier("Allianz Versicherung", "Berlin", 30),
            new Contact
            {
                Kind = ContactKind.Organization,
                Name = "Senatsverwaltung für Kultur und Gesellschaftlichen Zusammenhalt",
                Email = "foerderung@senatsverwaltung.example",
                City = "Berlin",
                Origin = "Zuwendungsbescheid",
                Roles = [new ContactRoleAssignment { Role = ContactRole.Funder }],
            },
            new Contact
            {
                Kind = ContactKind.Person,
                Name = "Behrens, Anja",
                GivenName = "Anja",
                Surname = "Behrens",
                Position = "Regie",
                Email = "anja.behrens@example.org",
                Origin = "Vertrag",
                Roles = [new ContactRoleAssignment { Role = ContactRole.Artist }],
                BankDetails = new ContactBankDetails { IsSubjectToArtistsSocialFund = true },
            },
            new Contact
            {
                Kind = ContactKind.Organization,
                Name = "Bühnenbedarf Schmidt GmbH",
                Email = "rechnung@buehnenbedarf.example",
                City = "Berlin",
                Origin = "Rechnung",
                Roles = [new ContactRoleAssignment { Role = ContactRole.Supplier }],
            },
            new Contact
            {
                Kind = ContactKind.Person,
                Name = "Weber, Clara",
                GivenName = "Clara",
                Surname = "Weber",
                Position = "Ensemble",
                Origin = "Vertrag",
                Roles = [new ContactRoleAssignment { Role = ContactRole.Artist }],
                BankDetails = new ContactBankDetails { IsSubjectToArtistsSocialFund = true },
            },
            new Contact
            {
                // Bewusst ohne Kennzeichen: Die Auswertung soll zeigen, dass
                // hier eine Entscheidung aussteht, statt sie zu treffen.
                Kind = ContactKind.Person,
                Name = "Marquardt, Jens",
                GivenName = "Jens",
                Surname = "Marquardt",
                Position = "Gastregie",
                Email = "jens.marquardt@example.org",
                Origin = "Vertrag",
                Roles = [new ContactRoleAssignment { Role = ContactRole.Artist }],
            },
            new Contact
            {
                Kind = ContactKind.Person,
                Name = "Krause, Anna",
                GivenName = "Anna",
                Surname = "Krause",
                Position = "Technische Leitung",
                Origin = "Anstellung",
                Roles = [new ContactRoleAssignment { Role = ContactRole.Staff }],
            },
            new Contact
            {
                Kind = ContactKind.Person,
                Name = "Schröder, Ben",
                GivenName = "Ben",
                Surname = "Schröder",
                Position = "Bühne",
                Origin = "Anstellung",
                Roles = [new ContactRoleAssignment { Role = ContactRole.Staff }],
            },
            new Contact
            {
                Kind = ContactKind.Person,
                Name = "Fischer, David",
                GivenName = "David",
                Surname = "Fischer",
                Position = "Abenddienst",
                Origin = "Anstellung",
                Roles = [new ContactRoleAssignment { Role = ContactRole.Staff }],
            });

        await context.SaveChangesAsync(cancellationToken);

        (Account debit, Account credit, decimal amount, int month, int day, string text, CostCenter? cc)[] bookings =
        [
            (rent, bank, 1500.00m, 1, 2, "Miete Januar, Hauptbühne", admin),
            (energy, bank, 418.90m, 1, 15, "Abschlag Strom Januar", admin),
            (bank, tickets, 2840.00m, 1, 22, "Kartenerlöse 20.01.", sales),
            (fees, bank, 6200.00m, 1, 31, "Gagen Ensemble Januar", artistic),
            (rent, bank, 1500.00m, 2, 2, "Miete Februar, Hauptbühne", admin),
            (material, bank, 612.40m, 2, 11, "Kostümstoffe, Werkstatt", workshop),
            (bank, grants, 25000.00m, 2, 14, "Zuwendung Senatsverwaltung, 1. Rate", null),
            (printing, bank, 1890.00m, 2, 18, "Druck Spielzeitheft", sales),
            (insurance, bank, 1045.00m, 2, 20, "Versicherung, Quartalsbeitrag", admin),
            (bank, tickets, 3187.50m, 2, 25, "Kartenerlöse 24.02.", sales),
            (energy, bank, 420.17m, 2, 27, "Stromrechnung Februar", admin),
            (fees, bank, 8400.00m, 2, 28, "Gage Ensemble, Premiere", artistic),
            (rent, bank, 1500.00m, 3, 1, "Miete März, Hauptbühne", admin),
            (cash, tickets, 340.00m, 3, 6, "Abendkasse 05.03.", sales),
        ];

        foreach (var (debit, credit, amount, month, day, text, costCenter) in bookings)
        {
            var entry = new JournalEntry
            {
                FiscalYear = 2026,
                Date = new DateOnly(2026, month, day),
                Description = text,
                Lines =
                [
                    new JournalLine { AccountId = debit.Id, Debit = amount, CostCenterId = costCenter?.Id },
                    new JournalLine { AccountId = credit.Id, Credit = amount, CostCenterId = costCenter?.Id },
                ],
            };

            await journal.AddAsync(entry, cancellationToken);

            // Alles bis Ende Februar ist übergeben und damit festgeschrieben;
            // der März steht noch als Entwurf, damit beides zu sehen ist.
            if (month < 3)
            {
                await journal.CommitAsync(entry.Id, cancellationToken);
            }
        }

        // Das Konto des Hauses. Umsätze liefert erst ein eingelesener Auszug --
        // die erfindet niemand, sonst stimmt die Buchhaltung mit nichts überein.
        Guid? bankAccount = await context.Accounts
            .Where(a => a.Number == "1200")
            .Select(a => (Guid?)a.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Die IBAN ist nach ISO 13616 gültig, gehört aber niemandem: Die
        // Bankleitzahl 99999999 ist nicht vergeben. Eine Muster-IBAN aus dem
        // Netz trägt dagegen fast immer die Leitzahl einer echten Bank -- und
        // dann steht im Vorführbestand ein Konto, das es geben könnte.
        context.BankAccounts.Add(new BankAccount
        {
            Name = "Geschäftskonto",
            Iban = "DE85999999990001234567",
            Bic = "TESTDEBBXXX",
            BankName = "Testbank Berlin",
            AccountId = bankAccount,
        });

        // Zwei Belege, die noch auf ihre Kontierung warten.
        Guid? lieferant = await context.Contacts
            .Where(c => c.Name == "Bühnenbedarf Schmidt GmbH")
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        // Zwei Gagen, damit die Künstlersozialabgabe etwas zu rechnen hat.
        // Eine an jemanden, der gekennzeichnet ist, eine an jemanden, der es
        // nicht ist -- genau der Fall, den die Auswertung sichtbar machen soll.
        Guid? ensemble = await context.Contacts
            .Where(c => c.Name == "Weber, Clara")
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        Guid? gastregie = await context.Contacts
            .Where(c => c.Name == "Marquardt, Jens")
            .Select(c => (Guid?)c.Id)
            .FirstOrDefaultAsync(cancellationToken);

        context.Vouchers.AddRange(
            new Voucher
            {
                Number = 1,
                FiscalYear = 2026,
                Date = new DateOnly(2026, 3, 9),
                GrossAmount = 247.80m,
                Description = "Beleuchtungsmittel, Nachbestellung",
                ContactId = lieferant,
                ExternalNumber = "RE-2026-0412",
                DueDate = new DateOnly(2026, 4, 8),
                Direction = VoucherDirection.Incoming,
                Source = VoucherSource.Photo,
            },
            new Voucher
            {
                Number = 2,
                FiscalYear = 2026,
                Date = new DateOnly(2026, 3, 11),
                GrossAmount = 89.00m,
                Description = "Fahrtkosten Gastregie",
                Direction = VoucherDirection.Incoming,
                Source = VoucherSource.Upload,
            },
            new Voucher
            {
                Number = 3,
                FiscalYear = 2026,
                Date = new DateOnly(2026, 3, 31),
                GrossAmount = 2400.00m,
                Description = "Gage März, Ensemble",
                ContactId = ensemble,
                Direction = VoucherDirection.Incoming,
                Source = VoucherSource.PaperOnly,
            },
            new Voucher
            {
                Number = 4,
                FiscalYear = 2026,
                Date = new DateOnly(2026, 3, 31),
                GrossAmount = 1800.00m,
                Description = "Honorar Gastregie",
                ContactId = gastregie,
                Direction = VoucherDirection.Incoming,
                Source = VoucherSource.PaperOnly,
            });

        await context.SaveChangesAsync(cancellationToken);

        await SeedChatAsync(context, cancellationToken);
        await SeedMailAsync(context, cancellationToken);

        if (profile is not null)
        {
            await SeedHouseCalendarAsync(context, profile, cancellationToken);
        }
        else
        {
            await SeedCalendarAsync(context, cancellationToken);
        }

        await SeedTasksAsync(context, cancellationToken);
        await SeedTimesAsync(context, cancellationToken);
        await SeedAbsencesAsync(context, cancellationToken);
        await SeedDutyTemplatesAsync(context, cancellationToken);
        await SeedProductionsAsync(context, profile, cancellationToken);

        // Dauerhaft vermerken, dass hier Erfundenes steht. Die Oberfläche
        // zeigt es an -- im Kalender eines Telefons sieht man einem Termin
        // nicht an, dass er aus einem Vorführbestand stammt.
        if (await context.Organizations.FirstOrDefaultAsync(cancellationToken) is { } house)
        {
            house.HasDemoData = true;
            await context.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation("Entwicklungsdaten angelegt: {Count} Buchungen.", bookings.Length);
    }

    /// <summary>
    /// Eine Spielzeit, wie sie im Jahresraster aussieht: Proben in Serie,
    /// Vorstellungen, ein mehrtaegiges Gastspiel, Fristen und eine
    /// Werkstattwoche.
    /// </summary>
    /// <remarks>
    /// Alle Termine liegen relativ zu heute. Feste Daten veralteten, und ein
    /// leeres Raster zeigt nicht, wofuer es gebaut wurde.
    ///
    /// Zwei Abweichungen sind absichtlich dabei -- eine abgesagte und eine
    /// verschobene Probe. Beide Faelle sollen sichtbar sein, ohne dass jemand
    /// sie erst herstellen muss.
    /// </remarks>
    /// <summary>
    /// Aufgaben -- darunter die beiden Fristen, die früher Termine waren.
    /// </summary>
    /// <remarks>
    /// Eine Frist ist keine Verabredung, sondern etwas, das jemand tun muss.
    /// Sie gehört deshalb zur Aufgabe; im Kalender erscheint sie, ohne dort
    /// zu liegen (ADR-0006). Der Vorführbestand zeigt genau das.
    /// </remarks>
    private static async Task SeedTasksAsync(
        SouffleurDbContext context, CancellationToken cancellationToken)
    {
        if (await context.TaskItems.AnyAsync(cancellationToken))
        {
            return;
        }

        Dictionary<string, Guid> category = await context.CatalogEntries
            .Where(e => e.Catalog == CatalogKind.TaskCategory)
            .ToDictionaryAsync(e => e.Key, e => e.Id, cancellationToken);

        Dictionary<string, Guid> person = await context.Contacts
            .Where(c => c.Kind == ContactKind.Person)
            .ToDictionaryAsync(c => c.Name, c => c.Id, cancellationToken);

        Guid? Cat(string key) => category.TryGetValue(key, out Guid id) ? id : null;
        Guid? Who(string name) => person.TryGetValue(name, out Guid id) ? id : null;

        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow, berlin).DateTime);
        DateOnly monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        var premiere = new TaskItem
        {
            Title = "Premiere Hamlet vorbereiten",
            Description = "Alles, was bis zum Premierenabend stehen muss.",
            CategoryId = Cat("organisation"),
            AssigneeId = Who("Behrens, Anja"),
            StartDate = monday.AddDays(28),
            DueDate = monday.AddDays(46),
            State = TaskState.InProgress,
            SortOrder = 10,
        };

        context.TaskItems.Add(premiere);
        await context.SaveChangesAsync(cancellationToken);

        context.TaskItems.AddRange(
            new TaskItem
            {
                Title = "Förderantrag Hauptstadtkulturfonds",
                Description = "Antrag samt Finanzierungsplan einreichen.",
                CategoryId = Cat("verwaltung"),
                AssigneeId = Who("Behrens, Anja"),
                DueDate = monday.AddDays(21),
                SortOrder = 20,
            },
            new TaskItem
            {
                Title = "Verwendungsnachweis einreichen",
                Description = "Für die Spielzeit davor, bei der Senatsverwaltung.",
                CategoryId = Cat("verwaltung"),
                DueDate = monday.AddDays(112),
                SortOrder = 30,
            },
            new TaskItem
            {
                Title = "Bühnenbild Hamlet abnehmen",
                CategoryId = Cat("technik"),
                AssigneeId = Who("Schröder, Ben"),
                StartDate = monday.AddDays(35),
                DueDate = monday.AddDays(40),
                State = TaskState.InProgress,
                SortOrder = 40,
            },
            new TaskItem
            {
                Title = "Programmhefte in den Druck",
                Description = "Wartet auf die Freigabe der Fotos.",
                CategoryId = Cat("kommunikation"),
                DueDate = monday.AddDays(30),
                State = TaskState.Waiting,
                SortOrder = 50,
            },
            new TaskItem
            {
                Title = "Gastspielvertrag Bremen gegenzeichnen",
                CategoryId = Cat("verwaltung"),
                DueDate = monday.AddDays(55),
                State = TaskState.Waiting,
                SortOrder = 60,
            },
            new TaskItem
            {
                Title = "Probenplan für die Wiederaufnahme aufstellen",
                CategoryId = Cat("organisation"),
                AssigneeId = Who("Behrens, Anja"),
                SortOrder = 70,
            },
            new TaskItem
            {
                Title = "Spielzeitheft verschickt",
                CategoryId = Cat("kommunikation"),
                State = TaskState.Done,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-9),
                SortOrder = 80,
            },

            // Die Checkliste unter der Premiere.
            new TaskItem
            {
                Title = "Pressetext verschicken",
                CategoryId = Cat("kommunikation"),
                ParentId = premiere.Id,
                DueDate = monday.AddDays(39),
                SortOrder = 10,
            },
            new TaskItem
            {
                Title = "Einlasspersonal einteilen",
                CategoryId = Cat("organisation"),
                AssigneeId = Who("Fischer, David"),
                ParentId = premiere.Id,
                DueDate = monday.AddDays(44),
                SortOrder = 20,
            },
            new TaskItem
            {
                Title = "Sektempfang bestellen",
                CategoryId = Cat("organisation"),
                ParentId = premiere.Id,
                DueDate = monday.AddDays(44),
                State = TaskState.Done,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-2),
                SortOrder = 30,
            });

        await context.SaveChangesAsync(cancellationToken);

        // Zwei Reihenfolgen, wie sie im Haus gelten. Die erste geht
        // absichtlich nicht auf: Der Pressetext braucht Fotos vom fertigen
        // Bühnenbild, ist aber auf den 4.12. geplant -- das Bühnenbild wird
        // erst am 5.12. abgenommen. Genau diese Verdrehung soll der
        // Zeitstrahl im Vorführbestand zeigen, statt nur den heilen Fall.
        Dictionary<string, Guid> task = await context.TaskItems
            .ToDictionaryAsync(t => t.Title, t => t.Id, cancellationToken);

        void DependsOn(string waiting, string earlier)
        {
            if (task.TryGetValue(waiting, out Guid a) && task.TryGetValue(earlier, out Guid b))
            {
                context.TaskDependencies.Add(new TaskDependency { TaskId = a, DependsOnId = b });
            }
        }

        DependsOn("Pressetext verschicken", "Bühnenbild Hamlet abnehmen");
        DependsOn("Einlasspersonal einteilen", "Programmhefte in den Druck");

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Die Produktionen -- und was von den anderen Seeds zu ihnen gehoert.
    /// </summary>
    /// <remarks>
    /// Kommt zuletzt, weil es nur zusammenfuehrt: Jede Aufgabe, jeder Termin
    /// und jede Serie, die den Titel der Produktion traegt, bekommt ihren
    /// Verweis; die Belege des Gastregisseurs gehoeren zu Hamlet. So zeigt
    /// die Uebersicht einer Produktion im Vorfuehrbestand sofort, wofuer sie
    /// da ist. Aus einem Hausprofil wird jede Produktion mit ihrem ersten
    /// Abend als Premiere angelegt.
    /// </remarks>
    private static async Task SeedProductionsAsync(
        SouffleurDbContext context, HouseProfile? house, CancellationToken cancellationToken)
    {
        if (await context.Productions.AnyAsync(cancellationToken))
        {
            return;
        }

        Dictionary<string, Guid> functionId = await context.CatalogEntries
            .Where(e => e.Catalog == CatalogKind.ParticipationRole)
            .ToDictionaryAsync(e => e.Key, e => e.Id, cancellationToken);

        Dictionary<string, Guid> person = await context.Contacts
            .Where(c => c.Kind == ContactKind.Person)
            .ToDictionaryAsync(c => c.Name, c => c.Id, cancellationToken);

        int order = 0;

        // Eine Rolle: besetzt, wenn die Person im Adressbuch steht, sonst offen --
        // eine offene Rolle gehoert genauso auf den Zettel.
        ProductionRole Role(string name, string function, string? who = null, string? note = null) => new()
        {
            Name = name,
            FunctionId = functionId.TryGetValue(function, out Guid f) ? f : null,
            ContactId = who is not null && person.TryGetValue(who, out Guid id) ? id : null,
            Note = note,
            SortOrder = order += 10,
        };

        ProductionBudgetLine Line(string title, string category, decimal amount, string? note = null) => new()
        {
            Title = title, Category = category, Amount = amount, Note = note, SortOrder = order += 10,
        };

        ProductionNeed Need(string title, string? quantity = null, bool done = false) => new()
        {
            Kind = ProductionNeedKind.Material, Title = title, Quantity = quantity, IsDone = done, SortOrder = order += 10,
        };

        ProductionNeed Paper(string title, bool done = false, string? note = null) => new()
        {
            Kind = ProductionNeedKind.Document, Title = title, IsDone = done, Note = note, SortOrder = order += 10,
        };

        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow, berlin).DateTime);
        DateOnly monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        List<Production> productions = [];

        if (house is null)
        {
            // Die Premiere liegt am selben Tag wie der Termin „Premiere Hamlet"
            // im eingebauten Kalender (monday + 46).
            productions.Add(new Production
            {
                Title = "Hamlet",
                Subtitle = "William Shakespeare",
                Status = ProductionStatus.Rehearsing,
                PremiereDate = monday.AddDays(46),
                Budget = 18_000m,
                Description = "Neuinszenierung im Großen Saal in der Übersetzung von Heiner Müller. "
                    + "Sieben Spielende, ein Bühnenraum aus Vorhängen, Live-Musik vom Band. "
                    + "Zwei Stunden ohne Pause, ab 14 Jahren.",
                Research = "Textgrundlage: Müller-Übersetzung (Suhrkamp, Rechte über den Verlag, Anfrage läuft). "
                    + "Vergleichsinszenierungen: Ostermeier (Schaubühne 2008), Thalheimer (Schauspielhaus Bochum 2012). "
                    + "Dramaturgische Frage: Wie viel Fortinbras bleibt?",
                Notes = "Gastregie, Bühnenbild aus der eigenen Werkstatt. Technikabnahme in der Vorwoche der Premiere.",
                Roles =
                [
                    Role("Regie", "regie", "Behrens, Anja"),
                    Role("Regieassistenz", "regie", "Marquardt, Jens"),
                    Role("Hamlet", "ensemble", "Weber, Clara"),
                    Role("Ophelia", "ensemble", note: "Vorsprechen am Freitag"),
                    Role("Claudius", "ensemble"),
                    Role("Gertrud", "ensemble", "Schröder, Ben"),
                    Role("Horatio", "ensemble"),
                    Role("Bühne und Kostüm", "technik"),
                    Role("Licht", "technik", "Schröder, Ben"),
                ],
                BudgetLines =
                [
                    Line("Gagen Ensemble", "Gagen", 7_500m, "Probenpauschale, dann je Vorstellung"),
                    Line("Honorar Gastregie", "Gagen", 4_000m),
                    Line("Bühnenbild", "Ausstattung", 2_800m, "Material, Werkstatt im Haus"),
                    Line("Kostüme", "Ausstattung", 1_200m),
                    Line("Licht und Ton", "Technik", 900m, "Leihgeräte"),
                    Line("Plakat, Programmheft, Anzeigen", "Werbung", 1_100m),
                    Line("Aufführungsrechte", "Rechte", 500m),
                ],
                Needs =
                [
                    Need("Vorhänge, schwarz, Bühnenhöhe", "6 Bahnen", done: true),
                    Need("Totenschädel (Requisite)", "1", done: true),
                    Need("Degen, Bühnenwaffen", "2 Paar"),
                    Need("Nebelmaschine", "1, Leihgerät"),
                    Need("Kostüme Hofgesellschaft", "7"),
                    Paper("Aufführungsvertrag Verlag", note: "Anfrage läuft"),
                    Paper("Honorarvertrag Gastregie", done: true),
                    Paper("Strichfassung", done: true),
                    Paper("Pressetext und Fotos"),
                    Paper("Gefährdungsbeurteilung Bühnenwaffen"),
                ],
            });

            productions.Add(new Production
            {
                Title = "Der Besuch der alten Dame",
                Subtitle = "Friedrich Dürrenmatt",
                Status = ProductionStatus.Running,
                PremiereDate = monday.AddDays(-28),
                Budget = 12_500m,
                Description = "Wiederaufnahme der Inszenierung aus der letzten Spielzeit, im Spielplan bis Saisonende.",
                Roles =
                [
                    Role("Claire Zachanassian", "ensemble", "Weber, Clara"),
                    Role("Alfred Ill", "ensemble"),
                    Role("Abendspielleitung", "regie", "Marquardt, Jens"),
                ],
                BudgetLines =
                [
                    Line("Gagen Ensemble", "Gagen", 9_000m),
                    Line("Werbung Wiederaufnahme", "Werbung", 600m),
                ],
            });
        }
        else
        {
            foreach (HouseProduction described in house.Productions)
            {
                List<DateOnly> days = [.. described.Performances.Where(p => !p.IsCancelled).Select(p => p.Date)];
                DateOnly? first = days.Count == 0 ? null : days.Min();
                DateOnly? last = days.Count == 0 ? null : described.Performances.Max(p => p.Until ?? p.Date);

                productions.Add(new Production
                {
                    Title = described.Title,
                    Status = last is { } ende && ende < today ? ProductionStatus.Archived : ProductionStatus.Running,
                    PremiereDate = first,
                    ClosingDate = last,
                });
            }
        }

        // Dieselbe Produktion zweimal im Profil -- einmal reicht.
        productions = [.. productions.GroupBy(p => p.Title).Select(g => g.First())];
        context.Productions.AddRange(productions);
        await context.SaveChangesAsync(cancellationToken);

        foreach (Production production in productions)
        {
            string title = production.Title;

            foreach (TaskItem task in await context.TaskItems
                .Where(t => t.Title.Contains(title)).ToListAsync(cancellationToken))
            {
                task.ProductionId = production.Id;
            }

            foreach (CalendarEvent occurrence in await context.CalendarEvents
                .Where(e => e.Title.Contains(title)).ToListAsync(cancellationToken))
            {
                occurrence.ProductionId = production.Id;
            }

            foreach (EventSeries series in await context.EventSeries
                .Where(s => s.Title.Contains(title)).ToListAsync(cancellationToken))
            {
                series.ProductionId = production.Id;
            }
        }

        if (house is null && productions.FirstOrDefault(p => p.Title == "Hamlet") is { } hamlet)
        {
            // Die Linksammlung haengt als Lesezeichen an der Produktion -- wie
            // an jedem anderen Gegenstand.
            context.Bookmarks.AddRange(
                new Bookmark { Target = BookmarkTarget.Production, TargetId = hamlet.Id, Url = "https://www.suhrkamp.de/", Label = "Text bei Suhrkamp (Rechte)", SortOrder = 10 },
                new Bookmark { Target = BookmarkTarget.Production, TargetId = hamlet.Id, Url = "https://www.folger.edu/explore/shakespeares-works/hamlet/", Label = "Shakespeare im Original (Folger)", SortOrder = 20 });

            // Die Checkliste unter der Premiere gehoert mit dazu, auch wenn
            // „Sektempfang bestellen" den Namen nicht traegt.
            List<Guid> parents = await context.TaskItems
                .Where(t => t.ProductionId == hamlet.Id)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
            foreach (TaskItem child in await context.TaskItems
                .Where(t => t.ParentId != null && parents.Contains(t.ParentId.Value)).ToListAsync(cancellationToken))
            {
                child.ProductionId = hamlet.Id;
            }

            foreach (Voucher voucher in await context.Vouchers
                .Where(v => v.Description.Contains("Gastregie")).ToListAsync(cancellationToken))
            {
                voucher.ProductionId = hamlet.Id;
            }
        }

        await context.SaveChangesAsync(cancellationToken);

        if (house is null && productions.FirstOrDefault(p => p.Title == "Hamlet") is { } lebendig)
        {
            // Der Herzschlag liest, wann etwas angelegt wurde. Frisch geseedet
            // laege alles in dieser Woche -- ein einzelner Ausschlag, der nichts
            // zeigt. Darum die Aufgaben von Hamlet ueber die letzten Wochen
            // verteilen; die Pruefung schreibt CreatedAt sonst nicht um, daher
            // direkt in die Tabelle.
            List<Guid> aufgaben = await context.TaskItems
                .Where(t => t.ProductionId == lebendig.Id)
                .OrderBy(t => t.SortOrder)
                .Select(t => t.Id)
                .ToListAsync(cancellationToken);
            int[] wochenZurueck = [11, 9, 8, 8, 6, 5, 5, 4, 3, 2, 1, 1, 0];
            for (int i = 0; i < aufgaben.Count; i++)
            {
                DateTimeOffset damals = DateTimeOffset.UtcNow.AddDays(-7 * wochenZurueck[i % wochenZurueck.Length] - (i % 5));
                string stempel = damals.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffffffZ");
                Guid id = aufgaben[i];
                await context.Database.ExecuteSqlInterpolatedAsync(
                    $"UPDATE \"TaskItems\" SET \"CreatedAt\" = {stempel} WHERE \"Id\" = {id}", cancellationToken);
            }
        }
    }

    /// <summary>
    /// Spielstaetten und Spielplan aus dem Hausprofil.
    /// </summary>
    /// <remarks>
    /// Anders als der eingebaute Bestand liegen diese Termine auf festen Daten,
    /// nicht relativ zu heute: Sie stammen aus einem veroeffentlichten Spielplan
    /// und sollen genau dort stehen, wo das Haus sie erwartet.
    /// </remarks>
    private static async Task SeedHouseCalendarAsync(
        SouffleurDbContext context, HouseProfile house, CancellationToken cancellationToken)
    {
        if (await context.CalendarEvents.AnyAsync(cancellationToken))
        {
            return;
        }

        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

        Dictionary<string, Guid> resourceKind = await context.CatalogEntries
            .Where(e => e.Catalog == CatalogKind.ResourceKind)
            .ToDictionaryAsync(e => e.Key, e => e.Id, cancellationToken);

        Dictionary<string, Guid> category = await context.CatalogEntries
            .Where(e => e.Catalog == CatalogKind.EventCategory)
            .ToDictionaryAsync(e => e.Key, e => e.Id, cancellationToken);

        Dictionary<string, Resource> byShortName = [];
        int order = 0;

        foreach (HouseResource described in house.Resources)
        {
            var resource = new Resource
            {
                Name = described.Name,
                ShortName = described.ShortName,
                KindId = resourceKind.TryGetValue(described.Kind, out Guid kindId) ? kindId : null,
                Capacity = described.Capacity,
                IsExclusive = described.IsExclusive,
                SortOrder = order += 10,
            };

            byShortName[described.ShortName] = resource;
            context.Resources.Add(resource);
        }

        foreach (HouseProduction production in house.Productions)
        {
            Guid? categoryId = category.TryGetValue(production.Category, out Guid found)
                ? found
                : null;

            Resource? stage = production.Resource is { } key
                && byShortName.TryGetValue(key, out Resource? match) ? match : null;

            foreach (HousePerformance performance in production.Performances)
            {
                // Ein Eintrag mit "until" laeuft am Stueck ueber mehrere Tage --
                // eine Installation, eine Ausstellung. Das ist etwas anderes als
                // eine taegliche Wiederholung: Der Raum ist durchgehend belegt,
                // auch nachts, und es gibt keine Anfangszeit.
                DateOnly last = performance.Until ?? performance.Date;
                bool runsForDays = last > performance.Date;

                TimeOnly start = performance.Time ?? new TimeOnly(20, 0);

                DateTimeOffset startsAt = runsForDays
                    ? EventSeries.ToInstant(performance.Date, TimeOnly.MinValue, berlin)
                    : EventSeries.ToInstant(performance.Date, start, berlin);

                DateTimeOffset endsAt = runsForDays
                    ? EventSeries.ToInstant(last.AddDays(1), TimeOnly.MinValue, berlin)
                    : startsAt.AddMinutes(production.DurationMinutes);

                var occurrence = new CalendarEvent
                {
                    Title = production.Title,
                    Description = performance.Note,
                    Location = performance.Location,
                    CategoryId = categoryId,
                    StartsAt = startsAt,
                    EndsAt = endsAt,
                    IsAllDay = runsForDays,
                    Status = performance.IsCancelled
                        ? EventStatus.Cancelled
                        : EventStatus.Confirmed,
                };

                // Ein Gastspiel belegt die eigene Buehne nicht -- es findet
                // woanders statt. Deshalb nur buchen, wenn kein fremder
                // Spielort angegeben ist.
                if (stage is not null && performance.Location is null)
                {
                    occurrence.Bookings.Add(new ResourceBooking { ResourceId = stage.Id });
                }

                context.CalendarEvents.Add(occurrence);
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedCalendarAsync(
        SouffleurDbContext context, CancellationToken cancellationToken)
    {
        if (await context.EventSeries.AnyAsync(cancellationToken))
        {
            return;
        }

        Dictionary<string, Guid> category = await context.CatalogEntries
            .Where(e => e.Catalog == CatalogKind.EventCategory)
            .ToDictionaryAsync(e => e.Key, e => e.Id, cancellationToken);

        Guid? Of(string key) => category.TryGetValue(key, out Guid id) ? id : null;

        TimeZoneInfo berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
        DateOnly today = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(
            DateTimeOffset.UtcNow, berlin).DateTime);

        // Montag der laufenden Woche als Anker -- so liegen die Wochentage der
        // Serien immer gleich zum heutigen Tag.
        DateOnly monday = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));

        var rehearsals = new EventSeries
        {
            Title = "Probe Hamlet",
            Location = "Probebühne",
            CategoryId = Of("probe"),
            StartDate = monday.AddDays(-14),
            StartTime = new TimeOnly(19, 30),
            DurationMinutes = 180,
            Frequency = RecurrenceFrequency.Weekly,
            ByWeekday = Weekdays.Monday | Weekdays.Wednesday | Weekdays.Friday,
            Until = monday.AddDays(42),
        };

        var performances = new EventSeries
        {
            Title = "Der Besuch der alten Dame",
            Location = "Großer Saal",
            CategoryId = Of("vorstellung"),
            StartDate = monday.AddDays(-28),
            StartTime = new TimeOnly(20, 0),
            DurationMinutes = 150,
            Frequency = RecurrenceFrequency.Weekly,
            ByWeekday = Weekdays.Thursday | Weekdays.Friday | Weekdays.Saturday,
        };

        var meeting = new EventSeries
        {
            Title = "Dienstbesprechung",
            Location = "Büro",
            CategoryId = Of("sitzung"),
            StartDate = monday.AddDays(-56),
            StartTime = new TimeOnly(10, 0),
            DurationMinutes = 60,
            Frequency = RecurrenceFrequency.Weekly,
            ByWeekday = Weekdays.Monday,
        };

        var board = new EventSeries
        {
            Title = "Gesellschafterversammlung",
            Location = "Büro",
            CategoryId = Of("sitzung"),
            StartDate = monday.AddDays(-90),
            StartTime = new TimeOnly(18, 0),
            DurationMinutes = 120,
            Frequency = RecurrenceFrequency.Monthly,
            ByWeekday = Weekdays.Friday,
            BySetPosition = 3,
        };

        // Die Ressourcen des Hauses. Mitgeliefert werden sie nirgends -- welche
        // Bühnen ein Haus hat, weiß nur das Haus. Hier stehen sie, damit die
        // Belegungsprüfung im Vorführbetrieb etwas zu prüfen hat.
        Dictionary<string, Guid> kind = await context.CatalogEntries
            .Where(e => e.Catalog == CatalogKind.ResourceKind)
            .ToDictionaryAsync(e => e.Key, e => e.Id, cancellationToken);

        Guid? Kind(string key) => kind.TryGetValue(key, out Guid id) ? id : null;

        var stage = new Resource
        {
            Name = "Großer Saal",
            ShortName = "GS",
            KindId = Kind("buehne"),
            Capacity = 199,
        };
        var rehearsalRoom = new Resource
        {
            Name = "Probebühne 1",
            ShortName = "PB1",
            KindId = Kind("probebuehne"),
        };
        var rehearsalRoom2 = new Resource
        {
            Name = "Probebühne 2",
            ShortName = "PB2",
            KindId = Kind("probebuehne"),
        };
        var workshop = new Resource
        {
            Name = "Werkstatt",
            ShortName = "WS",
            KindId = Kind("werkstatt"),
        };
        var office = new Resource
        {
            Name = "Büro",
            ShortName = "Bü",
            KindId = Kind("raum"),
            Capacity = 12,
        };
        var foyer = new Resource
        {
            Name = "Foyer",
            ShortName = "Fo",
            KindId = Kind("raum"),
            Capacity = 80,
        };
        var lights = new Resource
        {
            Name = "Lichtpark",
            ShortName = "LP",
            KindId = Kind("technik"),

            // Scheinwerfer gibt es mehrfach -- zwei Termine dürfen ihn teilen.
            // Ein Saal nicht; deshalb ist das keine Eigenschaft der Art,
            // sondern der einzelnen Ressource.
            IsExclusive = false,
        };

        context.Resources.AddRange(
            stage, rehearsalRoom, rehearsalRoom2, workshop, office, foyer, lights);

        int order = 0;
        foreach (Resource resource in new[]
                 { stage, rehearsalRoom, rehearsalRoom2, workshop, office, foyer, lights })
        {
            resource.SortOrder = order += 10;
        }

        // Wer dabei ist. Aus dem Adressbuch -- es gibt nur eines.
        Dictionary<string, Guid> person = await context.Contacts
            .Where(c => c.Kind == ContactKind.Person)
            .ToDictionaryAsync(c => c.Name, c => c.Id, cancellationToken);

        Dictionary<string, Guid> role = await context.CatalogEntries
            .Where(e => e.Catalog == CatalogKind.ParticipationRole)
            .ToDictionaryAsync(e => e.Key, e => e.Id, cancellationToken);

        EventParticipant? With(string name, string function,
            ParticipationStatus status = ParticipationStatus.Accepted) =>
            person.TryGetValue(name, out Guid id)
                ? new EventParticipant
                {
                    ContactId = id,
                    FunctionId = role.TryGetValue(function, out Guid f) ? f : null,
                    Status = status,
                }
                : null;

        void Assign(List<EventParticipant> target, params EventParticipant?[] people) =>
            target.AddRange(people.OfType<EventParticipant>());

        // Die Einteilungen sind so gewaehlt, dass sich niemand selbst im Weg
        // steht: Wer abends probt, spielt nicht gleichzeitig.
        Assign(rehearsals.Participants,
            With("Behrens, Anja", "regie"),
            With("Weber, Clara", "ensemble"),
            With("Schröder, Ben", "technik"));

        Assign(performances.Participants,
            With("Krause, Anna", "technik"),
            With("Fischer, David", "abenddienst"));

        Assign(meeting.Participants,
            With("Behrens, Anja", "regie"),
            With("Krause, Anna", "technik"));

        rehearsals.Bookings.Add(new ResourceBooking { ResourceId = rehearsalRoom.Id });
        performances.Bookings.Add(new ResourceBooking { ResourceId = stage.Id });
        performances.Bookings.Add(new ResourceBooking { ResourceId = lights.Id });
        meeting.Bookings.Add(new ResourceBooking { ResourceId = office.Id });
        board.Bookings.Add(new ResourceBooking { ResourceId = office.Id });

        context.EventSeries.AddRange(rehearsals, performances, meeting, board);

        DateTimeOffset At(DateOnly date, int hour, int minute = 0) =>
            EventSeries.ToInstant(date, new TimeOnly(hour, minute), berlin);

        context.CalendarEvents.AddRange(
            // Ein Zusatztermin, kein Serienvorkommen: An ihm laesst sich
            // zeigen, dass eine offene Stelle direkt aus dem Dienstplan zu
            // besetzen ist. Bei einer Serie geht das nicht -- dort haengt die
            // Einteilung an der Serie, nicht am einzelnen Abend.
            new CalendarEvent
            {
                Title = "Der Besuch der alten Dame — Zusatztermin",
                Location = "Großer Saal",
                CategoryId = Of("vorstellung"),
                Bookings = [new ResourceBooking { ResourceId = stage.Id }],
                Participants =
                [
                    .. new[] { With("Krause, Anna", "technik") }.OfType<EventParticipant>(),
                ],
                // Dienstag: An dem Tag probt niemand (Mo/Mi/Fr) und es spielt
                // niemand (Do/Fr/Sa) -- so steht sich bei der Vorfuehrung
                // niemand selbst im Weg.
                StartsAt = At(monday.AddDays(8), 20, 0),
                EndsAt = At(monday.AddDays(8), 22, 30),
            },
            new CalendarEvent
            {
                Title = "Generalprobe Hamlet",
                Location = "Großer Saal",
                CategoryId = Of("generalprobe"),
                Bookings = [new ResourceBooking { ResourceId = stage.Id }],
                Participants =
                [
                    .. new[]
                    {
                        With("Behrens, Anja", "regie"),
                        With("Weber, Clara", "ensemble"),
                        With("Schröder, Ben", "technik"),
                    }.OfType<EventParticipant>(),
                ],
                StartsAt = At(monday.AddDays(44), 18, 0),
                EndsAt = At(monday.AddDays(44), 22, 0),
            },
            new CalendarEvent
            {
                Title = "Premiere Hamlet",
                Location = "Großer Saal",
                CategoryId = Of("premiere"),
                Bookings =
                [
                    new ResourceBooking { ResourceId = stage.Id },
                    new ResourceBooking { ResourceId = lights.Id },
                ],
                Participants =
                [
                    .. new[]
                    {
                        With("Behrens, Anja", "regie"),
                        With("Weber, Clara", "ensemble"),
                        With("Schröder, Ben", "technik"),
                        With("Fischer, David", "einlass", ParticipationStatus.Assigned),
                    }.OfType<EventParticipant>(),
                ],
                StartsAt = At(monday.AddDays(46), 19, 0),
                EndsAt = At(monday.AddDays(46), 22, 0),
            },
            new CalendarEvent
            {
                Title = "Gastspiel Theater Bremen",
                Location = "Bremen, Schwankhalle",
                CategoryId = Of("gastspiel"),
                StartsAt = At(monday.AddDays(70), 8, 0),
                EndsAt = At(monday.AddDays(73), 23, 0),
            },
            new CalendarEvent
            {
                Title = "Werkstattwoche -- Bühne gesperrt",
                Location = "Großer Saal",
                CategoryId = Of("sperrtermin"),
                Bookings =
                [
                    new ResourceBooking { ResourceId = stage.Id },
                    new ResourceBooking { ResourceId = workshop.Id },
                ],
                IsAllDay = true,
                StartsAt = At(monday.AddDays(84), 0, 0),
                EndsAt = At(monday.AddDays(88), 23, 59),
            },
            new CalendarEvent
            {
                Title = "Spielzeiteröffnung",
                Location = "Foyer",
                CategoryId = Of("veranstaltung"),
                Bookings = [new ResourceBooking { ResourceId = foyer.Id }],
                Participants =
                [
                    .. new[] { With("Fischer, David", "einlass") }.OfType<EventParticipant>(),
                ],
                StartsAt = At(monday.AddDays(126), 19, 0),
                EndsAt = At(monday.AddDays(126), 23, 0),
            });

        await context.SaveChangesAsync(cancellationToken);

        // Zwei Abweichungen von der Probenserie: eine abgesagt, eine verschoben.
        DateOnly cancelledDay = monday.AddDays(2);
        DateOnly movedDay = monday.AddDays(7);

        context.CalendarEvents.AddRange(
            new CalendarEvent
            {
                SeriesId = rehearsals.Id,
                OccurrenceStart = At(cancelledDay, 19, 30),
                Title = rehearsals.Title,
                Location = rehearsals.Location,
                CategoryId = rehearsals.CategoryId,
                Status = EventStatus.Cancelled,
                Description = "Hauptdarsteller erkrankt.",
                StartsAt = At(cancelledDay, 19, 30),
                EndsAt = At(cancelledDay, 22, 30),
            },
            new CalendarEvent
            {
                SeriesId = rehearsals.Id,
                OccurrenceStart = At(movedDay, 19, 30),
                Title = rehearsals.Title,
                Location = "Probebühne 2",
                CategoryId = rehearsals.CategoryId,
                Description = "Verlegt: Großer Saal belegt.",
                StartsAt = At(movedDay, 14, 0),
                EndsAt = At(movedDay, 17, 0),

                // Mit dem Raum wechselt die Belegung: Probebühne 1 wird an
                // diesem Montag wieder frei. Die Einteilung geht mit -- es ist
                // dieselbe Probe, nur woanders.
                Bookings = [new ResourceBooking { ResourceId = rehearsalRoom2.Id }],
                Participants =
                [
                    .. new[]
                    {
                        With("Behrens, Anja", "regie"),
                        With("Weber, Clara", "ensemble"),
                        With("Schröder, Ben", "technik"),
                    }.OfType<EventParticipant>(),
                ],
            });

        // Der Saal ist an diesen Abenden anders belegt -- durch die Premiere
        // und durch die Werkstattwoche. Im Spielplan eines Hauses steht an
        // solchen Tagen nicht einfach nichts: Die Vorstellung entfällt, und
        // zwar nachvollziehbar.
        foreach ((int day, string reason) in new[]
                 {
                     (46, "Entfällt: Premiere Hamlet im Großen Saal."),
                     (87, "Entfällt: Werkstattwoche, Bühne gesperrt."),
                     (88, "Entfällt: Werkstattwoche, Bühne gesperrt."),
                 })
        {
            DateOnly date = monday.AddDays(day);
            context.CalendarEvents.Add(new CalendarEvent
            {
                SeriesId = performances.Id,
                OccurrenceStart = At(date, 20, 0),
                Title = performances.Title,
                Location = performances.Location,
                CategoryId = performances.CategoryId,
                Status = EventStatus.Cancelled,
                Description = reason,
                StartsAt = At(date, 20, 0),
                EndsAt = At(date, 22, 30),
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Ein Bedarf fuer Vorstellung und Premiere.
    /// </summary>
    /// <remarks>
    /// Damit der Dienstplan zeigt, wozu er da ist: Die Vorstellungen im
    /// Spielplan sind nur teilweise besetzt, und erst der Bedarf macht daraus
    /// eine sichtbare Luecke statt einer leeren Zeile.
    /// </remarks>
    private static async Task SeedDutyTemplatesAsync(
        SouffleurDbContext context,
        CancellationToken cancellationToken)
    {
        Dictionary<string, Guid> catalog = await context.CatalogEntries
            .Where(e => e.Catalog == CatalogKind.EventCategory
                || e.Catalog == CatalogKind.ParticipationRole)
            .ToDictionaryAsync(e => e.Catalog + ":" + e.Key, e => e.Id, cancellationToken);

        Guid? Find(CatalogKind kind, string key) =>
            catalog.TryGetValue(kind + ":" + key, out Guid id) ? id : null;

        (string category, (string function, int count)[] needs)[] templates =
        [
            ("vorstellung", [("inspizienz", 1), ("technik", 2), ("abenddienst", 1)]),
            ("premiere", [("inspizienz", 1), ("technik", 3), ("abenddienst", 2)]),
        ];

        foreach ((string category, (string function, int count)[] needs) in templates)
        {
            if (Find(CatalogKind.EventCategory, category) is not { } categoryId)
            {
                continue;
            }

            List<DutyRequirement> requirements =
            [
                .. needs
                    .Select(need => (Id: Find(CatalogKind.ParticipationRole, need.function), need.count))
                    .Where(need => need.Id is not null)
                    .Select(need => new DutyRequirement
                    {
                        FunctionId = need.Id!.Value,
                        Count = need.count,
                    }),
            ];

            if (requirements.Count == 0)
            {
                continue;
            }

            context.DutyTemplates.Add(new DutyTemplate
            {
                CategoryId = categoryId,
                Requirements = requirements,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Verbindet das erste Konto mit einem Kontakt und legt ein paar gestempelte
    /// Zeiten an.
    /// </summary>
    /// <remarks>
    /// Ohne die Verbindung zeigt das Modul Personal nur einen Hinweis: Wer sich
    /// anmeldet, ist dann niemand im Adressbuch und kann nicht stempeln. Genau
    /// dieser Schritt fehlt im Haus am Anfang auch -- hier ist er vorweggenommen,
    /// damit die Vorführung nicht daran hängenbleibt.
    /// </remarks>
    private static async Task SeedTimesAsync(
        SouffleurDbContext context,
        CancellationToken cancellationToken)
    {
        User? admin = await context.Users.FirstOrDefaultAsync(cancellationToken);
        if (admin is null)
        {
            return;
        }

        Contact? person = await context.Contacts
            .FirstOrDefaultAsync(c => c.Name == "Krause, Anna", cancellationToken);

        if (person is null)
        {
            return;
        }

        person.UserId = admin.Id;

        CatalogEntry? technik = await context.CatalogEntries
            .FirstOrDefaultAsync(
                e => e.Catalog == CatalogKind.ParticipationRole && e.Key == "technik",
                cancellationToken);

        // Drei abgeschlossene Tage in der vergangenen Woche, einer davon mit
        // einer Spanne, die das Arbeitszeitgesetz anmerkt -- der Fall soll in
        // der Vorführung vorkommen und nicht versteckt sein.
        DateTimeOffset today = DateTimeOffset.UtcNow.Date;

        (int daysAgo, int startHour, int hours, int pause)[] days =
        [
            (6, 9, 8, 30),
            (5, 10, 7, 30),
            (4, 14, 11, 30),
        ];

        foreach ((int daysAgo, int startHour, int hours, int pause) in days)
        {
            DateTimeOffset start = today.AddDays(-daysAgo).AddHours(startHour);

            context.TimeEntries.Add(new TimeEntry
            {
                ContactId = person.Id,
                StartedAt = start,
                EndedAt = start.AddHours(hours),
                BreakMinutes = pause,
                FunctionId = technik?.Id,
                Source = TimeEntrySource.Clock,
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Vertragsdaten fuer die Leute des Hauses und von jeder Sorte Abwesenheit
    /// eine: ein offener Antrag, damit es etwas zu entscheiden gibt, ein
    /// genehmigter Urlaub, eine Krankmeldung ohne Bescheinigung -- die soll in
    /// der Liste fuer die Lohnbuchhaltung auftauchen.
    /// </summary>
    /// <remarks>
    /// Ueber die Rolle Mitarbeit und nicht ueber Namen: Mit einem Hausprofil
    /// heissen die Leute anders, und die Vorfuehrung soll trotzdem etwas zeigen.
    ///
    /// Die Zeiten liegen weit weg vom Heute: Eine Abwesenheit meldet sich beim
    /// Einteilen, und die Browsertests teilen dieselben Leute in den naechsten
    /// Wochen ein. Die Krankheit liegt deshalb in der Vorwoche, die Urlaube
    /// ab der achten Woche.
    /// </remarks>
    private static async Task SeedAbsencesAsync(
        SouffleurDbContext context,
        CancellationToken cancellationToken)
    {
        List<Contact> staff = await context.Contacts
            .Where(c => c.Kind == ContactKind.Person && c.Roles.Any(r => r.Role == ContactRole.Staff))
            .OrderBy(c => c.UserId == null)
            .ThenBy(c => c.Name)
            .Take(4)
            .ToListAsync(cancellationToken);

        if (staff.Count == 0)
        {
            return;
        }

        var yearStart = new DateOnly(DateTime.UtcNow.Year, 1, 1);
        DateOnly today = DateOnly.FromDateTime(DateTime.UtcNow);

        for (int i = 0; i < staff.Count; i++)
        {
            // Eine Teilzeitkraft dabei, damit die Spalte nicht ueberall gleich ist.
            bool teilzeit = i == staff.Count - 1 && staff.Count > 1;
            context.Employments.Add(new Employment
            {
                ContactId = staff[i].Id,
                ValidFrom = yearStart.AddYears(-2),
                WeeklyHours = teilzeit ? 24 : 40,
                WorkDays = teilzeit
                    ? WorkWeekdays.Wednesday | WorkWeekdays.Thursday | WorkWeekdays.Friday
                    : WorkWeekdays.MondayToFriday,
                AnnualLeaveDays = teilzeit ? 18 : 30,
            });
        }

        context.HouseHolidays.AddRange(
            new HouseHoliday { Month = 12, Day = 24, Name = "Heiligabend" },
            new HouseHoliday { Month = 12, Day = 31, Name = "Silvester" });

        DateOnly Monday(int weeksAhead)
        {
            DateOnly day = today.AddDays(7 * weeksAhead);
            return day.AddDays(-(((int)day.DayOfWeek + 6) % 7));
        }

        context.Absences.Add(new Absence
        {
            ContactId = staff[0].Id,
            Kind = AbsenceKind.Vacation,
            Status = AbsenceStatus.Approved,
            StartDate = Monday(8),
            EndDate = Monday(8).AddDays(4),
            DecidedAt = DateTimeOffset.UtcNow,
        });

        if (staff.Count > 1)
        {
            context.Absences.Add(new Absence
            {
                ContactId = staff[1].Id,
                Kind = AbsenceKind.Vacation,
                Status = AbsenceStatus.Requested,
                StartDate = Monday(10),
                EndDate = Monday(11).AddDays(4),
                DeputyId = staff[0].Id,
                Note = "Familienbesuch",
            });
        }

        if (staff.Count > 2)
        {
            context.Absences.Add(new Absence
            {
                ContactId = staff[2].Id,
                Kind = AbsenceKind.Sick,
                Status = AbsenceStatus.Reported,
                StartDate = Monday(-1),
                EndDate = Monday(-1).AddDays(2),
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Ein Kanal und ein Gespräch an einer Buchung -- damit beide Arten zu
    /// sehen sind, die der Chat kennt.
    /// </summary>
    /// <summary>
    /// Ein paar Mails im Postfach des Hauses, damit die Kommunikationsleiste
    /// beim Vorfuehren nicht leer ist: zwei angekommene, eine geschriebene.
    /// </summary>
    private static async Task SeedMailAsync(SouffleurDbContext context, CancellationToken cancellationToken)
    {
        User? admin = await context.Users.FirstOrDefaultAsync(cancellationToken);
        string haus = (await context.Organizations.Select(o => o.Name).FirstOrDefaultAsync(cancellationToken)) ?? "das Theater";
        const string postfach = "buero@theater.example";
        Dictionary<string, Contact> kontakte = await context.Contacts
            .Where(c => c.Email != null)
            .ToDictionaryAsync(c => c.Email!, cancellationToken);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        Contact? lieferant = kontakte.GetValueOrDefault("rechnung@buehnenbedarf.example");
        Contact? gastregie = kontakte.GetValueOrDefault("jens.marquardt@example.org");
        Contact? foerderer = kontakte.GetValueOrDefault("foerderung@senatsverwaltung.example");

        context.EmailMessages.AddRange(
            new EmailMessage
            {
                Direction = MailDirection.Incoming,
                FromAddress = "jens.marquardt@example.org",
                FromName = "Jens Marquardt",
                ContactId = gastregie?.Id,
                Subject = "Probenplan Woche 3",
                Body = "Hallo zusammen,\n\nkönnten wir die Hauptprobe am Donnerstag auf 18 Uhr vorziehen? Clara hat abends einen Termin, und ich würde gern den zweiten Akt noch einmal komplett durchgehen.\n\nViele Grüße\nJens",
                SentAt = now.AddHours(-3),
                MessageId = "<seed-probenplan@example.org>",
                Recipients = [new EmailRecipient { Address = postfach, Name = "Büro" }],
            },
            new EmailMessage
            {
                Direction = MailDirection.Incoming,
                FromAddress = "rechnung@buehnenbedarf.example",
                FromName = "Bühnenbedarf Schmidt GmbH",
                ContactId = lieferant?.Id,
                Subject = "Rechnung 2026-1187 -- Molton und Bühnenband",
                Body = "Sehr geehrte Damen und Herren,\n\nanbei unsere Rechnung 2026-1187 über 486,20 € für die Lieferung vom 28.10. Zahlbar innerhalb von 14 Tagen.\n\nMit freundlichen Grüßen\nBühnenbedarf Schmidt GmbH",
                SentAt = now.AddDays(-1).AddHours(-2),
                ReadAt = now.AddDays(-1),
                MessageId = "<seed-rechnung-1187@buehnenbedarf.example>",
                Recipients = [new EmailRecipient { Address = postfach, Name = "Büro" }],
            },
            new EmailMessage
            {
                Direction = MailDirection.Outgoing,
                FromAddress = postfach,
                SenderUserId = admin?.Id,
                Subject = "Verwendungsnachweis 2025 -- Fristverlängerung",
                Body = "Sehr geehrte Frau Hoffmann,\n\nfür den Verwendungsnachweis 2025 bitten wir um eine Fristverlängerung bis zum 15. Dezember. Die Abrechnung der Herbstproduktion liegt uns erst Ende November vollständig vor.\n\nMit freundlichen Grüßen\n" + haus,
                SentAt = now.AddDays(-2),
                Recipients =
                [
                    new EmailRecipient
                    {
                        Address = "foerderung@senatsverwaltung.example",
                        Name = foerderer?.Name ?? "Senatsverwaltung",
                        ContactId = foerderer?.Id,
                    },
                ],
            });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedChatAsync(SouffleurDbContext context, CancellationToken cancellationToken)
    {
        User? admin = await context.Users.FirstOrDefaultAsync(cancellationToken);
        if (admin is null)
        {
            return;
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;

        var channel = new Conversation
        {
            Kind = ConversationKind.Channel,
            Name = "Buchhaltung",
            Topic = "Fragen zur Kontierung",
            LastMessageAt = now,
            Participants = [new ConversationParticipant { UserId = admin.Id, JoinedAt = now, ReadUpTo = now }],
            Messages =
            [
                new Message { AuthorId = admin.Id, Text = "Die Stromrechnung hat zwei Zeiträume drin. Soll ich die splitten?" },
                new Message { AuthorId = admin.Id, Text = "Erledigt — auf Januar und Februar aufgeteilt." },
            ],
        };

        context.Conversations.Add(channel);

        // Ein Gespräch an einer Buchung: genau das, wofür der Chat
        // modulübergreifend gedacht ist.
        JournalEntry? rent = await context.JournalEntries
            .OrderByDescending(e => e.Number)
            .FirstOrDefaultAsync(cancellationToken);

        if (rent is not null)
        {
            context.Conversations.Add(new Conversation
            {
                Kind = ConversationKind.Attached,
                SubjectType = ConversationSubject.JournalEntry,
                SubjectId = rent.Id,
                LastMessageAt = now,
                Participants = [new ConversationParticipant { UserId = admin.Id, JoinedAt = now, ReadUpTo = now }],
                Messages = [new Message { AuthorId = admin.Id, Text = "Beleg dazu liegt noch beim Hausmeister." }],
            });
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static Contact Supplier(string name, string city, int paymentTermDays) => new()
    {
        Kind = ContactKind.Organization,
        Name = name,
        City = city,
        Origin = "Eingangsrechnung",
        Roles = [new ContactRoleAssignment { Role = ContactRole.Supplier }],
        BankDetails = new ContactBankDetails { PaymentTermDays = paymentTermDays },
    };

    private static Account Add(SouffleurDbContext context, string number, string name, AccountType type)
    {
        var account = new Account { Number = number, Name = name, Type = type };
        context.Accounts.Add(account);
        return account;
    }

    private static CostCenter AddCostCenter(SouffleurDbContext context, string number, string name)
    {
        var costCenter = new CostCenter { Number = number, Name = name };
        context.CostCenters.Add(costCenter);
        return costCenter;
    }
}
