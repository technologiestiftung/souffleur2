using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Souffleur.Domain.Identity;
using Souffleur.Domain.Modules;
using Souffleur.Domain.Accounting;
using Souffleur.Domain.Bookmarks;
using Souffleur.Domain.Banking;
using Souffleur.Domain.Calendar;
using Souffleur.Domain.Catalogs;
using Souffleur.Domain.Contacts;
using Souffleur.Domain.Documents;
using Souffleur.Domain.Notifications;
using Souffleur.Domain.Conversations;
using Souffleur.Domain.Mail;
using Souffleur.Domain.Organizations;
using Souffleur.Domain.Personnel;
using Souffleur.Domain.Productions;
using Souffleur.Domain.Resources;
using Souffleur.Domain.Ticketing;
using Souffleur.Domain.Touring;
using Souffleur.Domain.Work;
using Souffleur.Infrastructure.Persistence.Configurations;

namespace Souffleur.Infrastructure.Persistence;

public class SouffleurDbContext(
    DbContextOptions<SouffleurDbContext> options,
    TimeProvider timeProvider,
    ICurrentUser currentUser)
    : IdentityDbContext<User, Role, Guid>(options)
{
    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<BankAccount> BankAccounts => Set<BankAccount>();

    public DbSet<BankTransaction> BankTransactions => Set<BankTransaction>();

    public DbSet<CostCenter> CostCenters => Set<CostCenter>();

    public DbSet<CatalogEntry> CatalogEntries => Set<CatalogEntry>();

    public DbSet<CalendarEvent> CalendarEvents => Set<CalendarEvent>();

    public DbSet<EventSeries> EventSeries => Set<EventSeries>();

    public DbSet<CalendarSubscription> CalendarSubscriptions => Set<CalendarSubscription>();

    public DbSet<EventParticipant> EventParticipants => Set<EventParticipant>();

    public DbSet<Resource> Resources => Set<Resource>();

    public DbSet<ResourceBooking> ResourceBookings => Set<ResourceBooking>();

    public DbSet<TaskItem> TaskItems => Set<TaskItem>();

    public DbSet<TaskDependency> TaskDependencies => Set<TaskDependency>();

    public DbSet<TaskContact> TaskContacts => Set<TaskContact>();

    public DbSet<Bookmark> Bookmarks => Set<Bookmark>();

    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<ContactRoleAssignment> ContactRoles => Set<ContactRoleAssignment>();

    public DbSet<ContactBankDetails> ContactBankDetails => Set<ContactBankDetails>();

    public DbSet<JournalEntry> JournalEntries => Set<JournalEntry>();

    public DbSet<JournalLine> JournalLines => Set<JournalLine>();

    public DbSet<Voucher> Vouchers => Set<Voucher>();

    public DbSet<VoucherFile> VoucherFiles => Set<VoucherFile>();

    public DbSet<TimeEntry> TimeEntries => Set<TimeEntry>();

    public DbSet<ArtistsSocialFundRate> ArtistsSocialFundRates =>
        Set<ArtistsSocialFundRate>();

    public DbSet<DutyTemplate> DutyTemplates => Set<DutyTemplate>();

    public DbSet<Absence> Absences => Set<Absence>();

    public DbSet<Employment> Employments => Set<Employment>();

    public DbSet<LeaveAdjustment> LeaveAdjustments => Set<LeaveAdjustment>();

    public DbSet<HouseHoliday> HouseHolidays => Set<HouseHoliday>();

    public DbSet<DutyRequirement> DutyRequirements => Set<DutyRequirement>();

    public DbSet<Notification> Notifications => Set<Notification>();

    public DbSet<Document> Documents => Set<Document>();

    public DbSet<DocumentTag> DocumentTags => Set<DocumentTag>();

    public DbSet<DocumentLink> DocumentLinks => Set<DocumentLink>();

    public DbSet<DocumentGroup> DocumentGroups => Set<DocumentGroup>();

    public DbSet<DocumentFolder> DocumentFolders => Set<DocumentFolder>();

    public DbSet<DocumentFolderGroup> DocumentFolderGroups => Set<DocumentFolderGroup>();

    public DbSet<Group> Groups => Set<Group>();

    public DbSet<GroupMember> GroupMembers => Set<GroupMember>();

    public DbSet<GroupRole> GroupRoles => Set<GroupRole>();

    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();

    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    public DbSet<ModuleSetting> ModuleSettings => Set<ModuleSetting>();

    public DbSet<TicketingSettings> TicketingSettings => Set<TicketingSettings>();

    public DbSet<Production> Productions => Set<Production>();

    public DbSet<ProductionRole> ProductionRoles => Set<ProductionRole>();

    public DbSet<ProductionBudgetLine> ProductionBudgetLines => Set<ProductionBudgetLine>();

    public DbSet<ProductionNeed> ProductionNeeds => Set<ProductionNeed>();

    public DbSet<Tour> Tours => Set<Tour>();

    public DbSet<TourStop> TourStops => Set<TourStop>();

    public DbSet<TourPerformance> TourPerformances => Set<TourPerformance>();

    public DbSet<TourMember> TourMembers => Set<TourMember>();

    public DbSet<DatevExport> DatevExports => Set<DatevExport>();

    public DbSet<Conversation> Conversations => Set<Conversation>();

    public DbSet<Message> Messages => Set<Message>();

    public DbSet<ConversationParticipant> ConversationParticipants => Set<ConversationParticipant>();

    public DbSet<MessageMention> MessageMentions => Set<MessageMention>();

    public DbSet<EmailMessage> EmailMessages => Set<EmailMessage>();

    public DbSet<EmailRecipient> EmailRecipients => Set<EmailRecipient>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(SouffleurDbContext).Assembly);

        // SQLite kennt keinen Zeitstempeltyp. Ohne Konverter lehnt EF Core
        // jedes ORDER BY auf DateTimeOffset ab; als UTC-Text stimmt die
        // Sortierung mit der zeitlichen Reihenfolge ueberein.
        foreach (var property in builder.Model.GetEntityTypes()
                     .SelectMany(t => t.GetProperties()))
        {
            if (property.ClrType == typeof(DateTimeOffset))
            {
                property.SetValueConverter(new UtcTimestampConverter());
                property.SetColumnType("TEXT");
            }
            else if (property.ClrType == typeof(DateTimeOffset?))
            {
                property.SetValueConverter(new NullableUtcTimestampConverter());
                property.SetColumnType("TEXT");
            }
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ApplyAuditAndImmutability();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ApplyAuditAndImmutability();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void ApplyAuditAndImmutability() =>
        AuditBehaviour.Apply(ChangeTracker, timeProvider.GetUtcNow(), currentUser.Id);
}
