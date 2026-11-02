using EncodeLabs.Domain.Contacts;
using EncodeLabs.Domain.Messages;
using Microsoft.EntityFrameworkCore;

namespace EncodeLabs.Infrastructure.Persistence;

public sealed class InboxDbContext(DbContextOptions<InboxDbContext> options) : DbContext(options)
{
    public DbSet<Contact> Contacts => Set<Contact>();

    public DbSet<Message> Messages => Set<Message>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InboxDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }
}
