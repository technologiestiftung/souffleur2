using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Souffleur.Domain.Ticketing;

namespace Souffleur.Infrastructure.Persistence.Configurations;

public class TicketingConfiguration : IEntityTypeConfiguration<TicketingSettings>
{
    public void Configure(EntityTypeBuilder<TicketingSettings> builder)
    {
        builder.Property(s => s.BaseUrl).HasMaxLength(300);
        builder.Property(s => s.Organizer).HasMaxLength(100);

        // Das Token steht im Klartext in der Datenbank. Sie liegt auf der
        // Maschine des Hauses, und ein Lesetoken auf den eigenen Vorverkauf
        // ist kein Zugang zu Geld -- aber die Datei gehoert trotzdem
        // geschuetzt wie die Buchhaltung, die daneben liegt.
        builder.Property(s => s.Token).HasMaxLength(200);

        builder.Property(s => s.LastProblem).HasMaxLength(500);
    }
}
