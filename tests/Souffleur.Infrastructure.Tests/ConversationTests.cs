using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Conversations;
using Souffleur.Domain.Identity;
using Souffleur.Infrastructure.Conversations;
using Souffleur.Infrastructure.Persistence;

namespace Souffleur.Infrastructure.Tests;

/// <summary>
/// Der Chat ist ein eigenes Modul, kein Anhängsel der Aufgaben. Entscheidend
/// ist, dass eine Unterhaltung an jedem Gegenstand hängen kann -- an einer
/// Buchung so gut wie an einer Aufgabe.
/// </summary>
public class ConversationTests : AccountingTestBase
{
    private readonly Guid anja = Guid.CreateVersion7();
    private readonly Guid wolf = Guid.CreateVersion7();

    private ConversationService As(Guid who) =>
        new(Context, TimeProvider.System, new FixedUser(who));

    private async Task AddUsersAsync()
    {
        Context.Users.AddRange(
            new User { Id = anja, UserName = "anja", Email = "anja@test.local", GivenName = "Anja", Surname = "Behrens" },
            new User { Id = wolf, UserName = "wolf", Email = "wolf@test.local", GivenName = "Wolfgang", Surname = "Roese" });
        await Context.SaveChangesAsync();
    }

    [Fact]
    public async Task Ein_Kanal_wird_angelegt()
    {
        await AddUsersAsync();

        Conversation channel = await As(anja).CreateChannelAsync("Buchhaltung", "Fragen zur Kontierung");

        channel.Kind.Should().Be(ConversationKind.Channel);
        channel.Name.Should().Be("Buchhaltung");
        channel.Participants.Should().ContainSingle().Which.UserId.Should().Be(anja);
    }

    [Fact]
    public async Task Ein_Kanal_ohne_Namen_wird_abgewiesen()
    {
        await AddUsersAsync();

        Func<Task> act = () => As(anja).CreateChannelAsync("   ");

        (await act.Should().ThrowAsync<ConversationException>())
            .Which.Problems.Should().Contain(p => p.Contains("Namen"));
    }

    [Fact]
    public async Task Eine_Unterhaltung_haengt_an_einer_Buchung()
    {
        await AddUsersAsync();
        Domain.Accounting.JournalEntry entry = await Journal.AddAsync(RentPayment());

        Conversation conversation = await As(anja)
            .ForSubjectAsync(ConversationSubject.JournalEntry, entry.Id);

        conversation.Kind.Should().Be(ConversationKind.Attached);
        conversation.IsAttached.Should().BeTrue();
        conversation.SubjectType.Should().Be(ConversationSubject.JournalEntry);
        conversation.SubjectId.Should().Be(entry.Id);
    }

    [Fact]
    public async Task Derselbe_Gegenstand_bekommt_keine_zweite_Unterhaltung()
    {
        await AddUsersAsync();
        Domain.Accounting.JournalEntry entry = await Journal.AddAsync(RentPayment());

        Conversation first = await As(anja).ForSubjectAsync(ConversationSubject.JournalEntry, entry.Id);
        Conversation second = await As(wolf).ForSubjectAsync(ConversationSubject.JournalEntry, entry.Id);

        second.Id.Should().Be(first.Id, "sonst entstehen zwei Fäden an derselben Buchung");
    }

    [Fact]
    public async Task Ein_Beleg_und_eine_Buchung_haben_getrennte_Unterhaltungen()
    {
        await AddUsersAsync();
        Guid sameId = Guid.CreateVersion7();

        Conversation atVoucher = await As(anja).ForSubjectAsync(ConversationSubject.Voucher, sameId);
        Conversation atEntry = await As(anja).ForSubjectAsync(ConversationSubject.JournalEntry, sameId);

        atEntry.Id.Should().NotBe(atVoucher.Id);
    }

    [Fact]
    public async Task Wer_schreibt_ist_dabei()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Technik");

        await As(wolf).PostAsync(channel.Id, "Scheinwerfer 7 ist wieder da.");

        Context.ChangeTracker.Clear();
        Conversation reloaded = await Context.Conversations
            .Include(c => c.Participants)
            .SingleAsync(c => c.Id == channel.Id);

        reloaded.Participants.Select(p => p.UserId).Should().Contain(wolf);
        reloaded.LastMessageAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Eine_leere_Nachricht_wird_nicht_gesendet()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Technik");

        Func<Task> act = () => As(anja).PostAsync(channel.Id, "   ");

        await act.Should().ThrowAsync<ConversationException>();
    }

    [Fact]
    public async Task In_einer_archivierten_Unterhaltung_wird_nicht_mehr_geschrieben()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Spielzeit 2025");
        await As(anja).ArchiveAsync(channel.Id);

        Func<Task> act = () => As(anja).PostAsync(channel.Id, "Noch was");

        (await act.Should().ThrowAsync<ConversationException>())
            .Which.Problems.Should().Contain(p => p.Contains("archiviert"));
    }

    [Fact]
    public async Task Eine_zurueckgezogene_Nachricht_hinterlaesst_ihre_Zeile()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Technik");
        Message message = await As(anja).PostAsync(channel.Id, "Das war voreilig.");

        await As(anja).RetractAsync(message.Id);

        Context.ChangeTracker.Clear();
        Message reloaded = await Context.Messages.SingleAsync(m => m.Id == message.Id);
        reloaded.IsRetracted.Should().BeTrue();
        reloaded.Text.Should().BeEmpty();

        // Die Zeile bleibt: Sonst wäre ein Gespräch nachträglich umschreibbar.
        (await Context.Messages.CountAsync(m => m.ConversationId == channel.Id)).Should().Be(1);
    }

    [Fact]
    public async Task Zurueckziehen_kann_nur_wer_geschrieben_hat()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Technik");
        Message message = await As(anja).PostAsync(channel.Id, "Meins.");

        Func<Task> act = () => As(wolf).RetractAsync(message.Id);

        await act.Should().ThrowAsync<ConversationException>();
    }

    [Fact]
    public async Task Eine_Nachricht_laesst_sich_aendern_und_traegt_es()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Technik");
        Message message = await As(anja).PostAsync(channel.Id, "Probe um 10.");

        Message edited = await As(anja).EditAsync(message.Id, "Probe um 11.");

        edited.Text.Should().Be("Probe um 11.");
        edited.EditedAt.Should().NotBeNull("wer schon gelesen hat, soll sehen, dass sich etwas geaendert hat");
    }

    [Fact]
    public async Task Aendern_kann_nur_wer_geschrieben_hat_und_nicht_ins_Leere()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Technik");
        Message message = await As(anja).PostAsync(channel.Id, "Meins.");

        await As(wolf).Invoking(c => c.EditAsync(message.Id, "Seins."))
            .Should().ThrowAsync<ConversationException>();
        await As(anja).Invoking(c => c.EditAsync(message.Id, "   "))
            .Should().ThrowAsync<ConversationException>();

        Context.ChangeTracker.Clear();
        (await Context.Messages.SingleAsync(m => m.Id == message.Id)).Text.Should().Be("Meins.");
    }

    [Fact]
    public async Task Geantwortet_wird_nur_innerhalb_derselben_Unterhaltung()
    {
        await AddUsersAsync();
        Conversation geheim = await As(anja).CreateChannelAsync("Vorstand", isPrivate: true);
        Message vertraulich = await As(anja).PostAsync(geheim.Id, "Vertraulich.");
        Conversation offen = await As(wolf).CreateChannelAsync("Foyer");

        Message antwort = await As(wolf).PostAsync(offen.Id, "Erstens.");
        (await As(wolf).PostAsync(offen.Id, "Zweitens.", inReplyToId: antwort.Id))
            .InReplyToId.Should().Be(antwort.Id);

        await As(wolf).Invoking(c => c.PostAsync(offen.Id, "Zitat", inReplyToId: vertraulich.Id))
            .Should().ThrowAsync<ConversationException>();
    }

    [Fact]
    public async Task Ungelesenes_zaehlt_nur_fremde_Nachrichten()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Technik");

        await As(anja).PostAsync(channel.Id, "Eins von mir");
        await As(wolf).PostAsync(channel.Id, "Eins von Wolf");
        await As(wolf).PostAsync(channel.Id, "Noch eins von Wolf");

        IReadOnlyDictionary<Guid, int> unread = await As(anja).UnreadAsync();

        unread[channel.Id].Should().Be(2, "die eigene Nachricht zählt nicht");
    }

    [Fact]
    public async Task Nach_dem_Lesen_ist_nichts_mehr_offen()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Technik");
        await As(wolf).PostAsync(channel.Id, "Hallo");

        await As(anja).MarkReadAsync(channel.Id);

        (await As(anja).UnreadAsync()).Should().NotContainKey(channel.Id);
    }

    [Fact]
    public async Task Ein_geschlossener_Kanal_ist_fuer_Unbeteiligte_unsichtbar()
    {
        await AddUsersAsync();
        await As(anja).CreateChannelAsync("Leitungsrunde", isPrivate: true);
        await As(anja).CreateChannelAsync("Alle");

        (await As(anja).Visible().CountAsync()).Should().Be(2);
        (await As(wolf).Visible().CountAsync()).Should().Be(1, "den geschlossenen sieht Wolf nicht");
    }

    [Fact]
    public async Task Eine_Direktnachricht_entsteht_nur_einmal()
    {
        await AddUsersAsync();

        Conversation first = await As(anja).DirectAsync(wolf);
        Conversation again = await As(wolf).DirectAsync(anja);

        again.Id.Should().Be(first.Id);
        first.IsPrivate.Should().BeTrue();
    }

    [Fact]
    public async Task Mit_sich_selbst_gibt_es_keine_Direktnachricht()
    {
        await AddUsersAsync();

        Func<Task> act = () => As(anja).DirectAsync(anja);

        await act.Should().ThrowAsync<ConversationException>();
    }

    [Fact]
    public async Task Eine_Gruppe_wird_beim_Aufnehmen_aufgeloest()
    {
        await AddUsersAsync();
        Guid technik = Guid.CreateVersion7();
        Context.Groups.Add(new Group { Id = technik, Name = "Technik" });
        Context.GroupMembers.Add(new GroupMember { GroupId = technik, UserId = wolf });
        await Context.SaveChangesAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Umbau", isPrivate: true);

        IReadOnlyList<Guid> added = await As(anja).AddParticipantsAsync(channel.Id, null, [technik]);

        added.Should().Equal(wolf);
        (await As(wolf).Visible().CountAsync()).Should().Be(1, "jetzt sieht Wolf den geschlossenen Kanal");

        // Noch einmal aufnehmen aendert nichts.
        (await As(anja).AddParticipantsAsync(channel.Id, [wolf], null)).Should().BeEmpty();
    }

    [Fact]
    public async Task Ein_Kanal_fuer_eine_Gruppe_folgt_ihren_Mitgliedern()
    {
        await AddUsersAsync();
        Guid technik = Guid.CreateVersion7();
        Context.Groups.Add(new Group { Id = technik, Name = "Technik" });
        await Context.SaveChangesAsync();

        Conversation channel = await As(anja).CreateChannelAsync(
            "Technik", isPrivate: true, groupId: technik);
        (await As(wolf).Visible().CountAsync()).Should().Be(0, "Wolf ist noch nicht in der Gruppe");

        // Wolf kommt in die Gruppe -- und ist damit im Kanal.
        Context.GroupMembers.Add(new GroupMember { GroupId = technik, UserId = wolf });
        await Context.SaveChangesAsync();
        await GroupChannels.SyncAsync(Context, technik, DateTimeOffset.UtcNow);
        (await As(wolf).Visible().CountAsync(c => c.Id == channel.Id)).Should().Be(1);

        // Er geht aus der Gruppe -- und sieht den geschlossenen Kanal nicht mehr.
        await Context.GroupMembers.Where(m => m.GroupId == technik).ExecuteDeleteAsync();
        await GroupChannels.SyncAsync(Context, technik, DateTimeOffset.UtcNow);
        Context.ChangeTracker.Clear();
        (await As(wolf).Visible().CountAsync(c => c.Id == channel.Id)).Should().Be(0);
    }

    [Fact]
    public async Task In_einem_Gruppenkanal_nimmt_man_niemanden_von_Hand_auf()
    {
        await AddUsersAsync();
        Guid technik = Guid.CreateVersion7();
        Context.Groups.Add(new Group { Id = technik, Name = "Technik" });
        await Context.SaveChangesAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Technik", groupId: technik);

        await As(anja).Invoking(c => c.AddParticipantsAsync(channel.Id, [wolf], null))
            .Should().ThrowAsync<ConversationException>();
    }

    [Fact]
    public async Task Wer_geht_sieht_den_geschlossenen_Kanal_nicht_mehr()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Leitung", isPrivate: true, participants: [wolf]);

        await As(wolf).LeaveAsync(channel.Id);

        (await As(wolf).Visible().CountAsync()).Should().Be(0);
        (await As(anja).ParticipantsAsync(channel.Id)).Select(p => p.UserId).Should().Equal(anja);
    }

    [Fact]
    public async Task Ein_geschlossener_Kanal_ohne_Teilnehmer_wird_archiviert()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Allein", isPrivate: true);

        await As(anja).LeaveAsync(channel.Id);

        Context.ChangeTracker.Clear();
        (await Context.Conversations.SingleAsync(c => c.Id == channel.Id)).IsArchived
            .Should().BeTrue("sonst stuende er unsichtbar herum");
    }

    [Fact]
    public async Task Eine_Direktnachricht_bleibt_zu_zweit()
    {
        await AddUsersAsync();
        Guid dritte = Guid.CreateVersion7();
        Context.Users.Add(new User { Id = dritte, UserName = "c", Email = "c@test.local", GivenName = "Clara", Surname = "Weber" });
        await Context.SaveChangesAsync();
        Conversation direct = await As(anja).DirectAsync(wolf);

        Func<Task> act = () => As(anja).AddParticipantsAsync(direct.Id, [dritte], null);

        await act.Should().ThrowAsync<ConversationException>();
    }

    [Fact]
    public async Task Archiviert_und_wieder_zurueck()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Spielzeit");
        await As(anja).ArchiveAsync(channel.Id);

        await As(anja).UnarchiveAsync(channel.Id);

        Message message = await As(anja).PostAsync(channel.Id, "Geht wieder.");
        message.Text.Should().Be("Geht wieder.");
    }

    [Fact]
    public async Task Loeschen_darf_wer_angelegt_hat_oder_die_Verwaltung()
    {
        await AddUsersAsync();
        Conversation channel = await As(anja).CreateChannelAsync("Versehen");
        await As(wolf).PostAsync(channel.Id, "Hallo?");
        // Der Testkontext kennt keine angemeldete Person; im Betrieb setzt das
        // der DbContext beim Anlegen -- und laesst es danach nicht mehr aendern,
        // deshalb hier an ihm vorbei.
        await Context.Database.ExecuteSqlAsync(
            $"UPDATE Conversations SET CreatedBy = {anja} WHERE Id = {channel.Id}");
        Context.ChangeTracker.Clear();

        Func<Task> fremd = () => As(wolf).DeleteAsync(channel.Id, mayManage: false);
        await fremd.Should().ThrowAsync<ConversationException>();

        await As(anja).DeleteAsync(channel.Id, mayManage: false);

        Context.ChangeTracker.Clear();
        (await Context.Conversations.AnyAsync(c => c.Id == channel.Id)).Should().BeFalse();
        (await Context.Messages.AnyAsync(m => m.ConversationId == channel.Id)).Should().BeFalse("die Nachrichten gehen mit");
    }

    [Fact]
    public async Task Was_an_einem_Gegenstand_haengt_wird_nicht_geloescht()
    {
        await AddUsersAsync();
        Domain.Accounting.JournalEntry entry = await Journal.AddAsync(RentPayment());
        Conversation attached = await As(anja).ForSubjectAsync(ConversationSubject.JournalEntry, entry.Id);

        Func<Task> act = () => As(anja).DeleteAsync(attached.Id, mayManage: true);

        (await act.Should().ThrowAsync<ConversationException>())
            .Which.Problems.Should().Contain(p => p.Contains("Gegenstand"));
    }

    private sealed class FixedUser(Guid id) : ICurrentUser
    {
        public Guid? Id { get; } = id;
    }
}
