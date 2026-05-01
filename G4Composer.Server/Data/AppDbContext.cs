using Microsoft.EntityFrameworkCore;
using G4Composer.Server.Data.Entities;

namespace G4Composer.Server.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SilvaGroup>      SilvaGroups      => Set<SilvaGroup>();
    public DbSet<SilvaSubtype>    SilvaSubtypes    => Set<SilvaSubtype>();
    public DbSet<StructureExample> StructureExamples => Set<StructureExample>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        // ── SilvaGroup ────────────────────────────────────────────────────
        model.Entity<SilvaGroup>(e =>
        {
            e.HasIndex(g => g.Code).IsUnique();
            e.Property(g => g.Code).HasMaxLength(4);
            e.Property(g => g.GroupNumber).HasMaxLength(8);
            e.Property(g => g.Name).HasMaxLength(64);
            e.Property(g => g.Groove).HasMaxLength(8);
        });

        // ── SilvaSubtype ──────────────────────────────────────────────────
        model.Entity<SilvaSubtype>(e =>
        {
            e.HasIndex(s => s.Code).IsUnique();
            e.Property(s => s.Code).HasMaxLength(8);
            e.Property(s => s.Loop).HasMaxLength(32);
            e.Property(s => s.Silva).HasMaxLength(32);
            e.Property(s => s.Onz).HasMaxLength(2);
            e.Property(s => s.Note).HasMaxLength(128);

            e.HasOne(s => s.Group)
             .WithMany(g => g.Subtypes)
             .HasForeignKey(s => s.SilvaGroupId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── StructureExample ──────────────────────────────────────────────
        model.Entity<StructureExample>(e =>
        {
            e.HasIndex(x => x.PdbId).IsUnique();
            e.Property(x => x.PdbId).HasMaxLength(32);
            e.Property(x => x.InpName).HasMaxLength(64);
            e.Property(x => x.Note).HasMaxLength(256);
            e.Property(x => x.Orient).HasMaxLength(32);
            e.Property(x => x.Twist).HasMaxLength(32);

            // Sequence and structure can be long (hundreds of chars)
            e.Property(x => x.Sequence).HasMaxLength(512);
            e.Property(x => x.Structure).HasMaxLength(512);
            e.Property(x => x.Chi).HasMaxLength(512);
            e.Property(x => x.Path).HasMaxLength(512);

            e.HasOne(x => x.Subtype)
             .WithMany(s => s.Examples)
             .HasForeignKey(x => x.SilvaSubtypeId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
