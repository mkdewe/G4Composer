using Microsoft.EntityFrameworkCore;
using G4Composer.Server.Data.Entities;

namespace G4Composer.Server.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<SilvaGroup>      SilvaGroups      => Set<SilvaGroup>();
    public DbSet<SilvaSubtype>    SilvaSubtypes    => Set<SilvaSubtype>();
    public DbSet<StructureExample> StructureExamples => Set<StructureExample>();
    public DbSet<PdbCacheEntry>   PdbCacheEntries  => Set<PdbCacheEntry>();
    public DbSet<PdbCacheFrame>   PdbCacheFrames   => Set<PdbCacheFrame>();

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
            e.Property(x => x.Rise).HasMaxLength(32);

            e.HasOne(x => x.Subtype)
             .WithMany(s => s.Examples)
             .HasForeignKey(x => x.SilvaSubtypeId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── PdbCacheEntry ─────────────────────────────────────────────────
        model.Entity<PdbCacheEntry>(e =>
        {
            e.HasIndex(c => c.Hash).IsUnique();
            e.HasIndex(c => c.PdbId);
            e.HasIndex(c => c.LastAccessedAtUtc);

            e.Property(c => c.Hash).HasMaxLength(64);
            e.Property(c => c.PdbId).HasMaxLength(32);
            e.Property(c => c.EngineVersion).HasMaxLength(16);

            e.HasOne(c => c.StructureExample)
             .WithMany()
             .HasForeignKey(c => c.StructureExampleId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── PdbCacheFrame ─────────────────────────────────────────────────
        model.Entity<PdbCacheFrame>(e =>
        {
            // Variant is part of the key: std and alt share the same iteration numbers.
            e.HasIndex(f => new { f.PdbCacheEntryId, f.Variant, f.Step }).IsUnique();
            e.Property(f => f.Variant).HasMaxLength(8).HasDefaultValue(FrameVariants.Standard);

            e.HasOne(f => f.Entry)
             .WithMany(c => c.Frames)
             .HasForeignKey(f => f.PdbCacheEntryId)
             .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
