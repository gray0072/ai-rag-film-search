using FilmSearch.Infrastructure.Ingestion.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FilmSearch.Infrastructure.Persistence.Configurations.Ingestion;

// EF Core mapping of the Ingestion module: tables in the "ingestion" schema.

internal static class IngestionSchema
{
    public const string Name = "ingestion";
}

internal sealed class JobRecordConfiguration : IEntityTypeConfiguration<JobRecord>
{
    /// <summary>Partial unique index: at most one queued or running job per kind (the 409 of the admin endpoints).</summary>
    public const string OneActivePerKindIndex = "ux_jobs_one_active_per_kind";

    public void Configure(EntityTypeBuilder<JobRecord> builder)
    {
        builder.ToTable("jobs", IngestionSchema.Name);
        builder.HasKey(j => j.Id);

        builder.Property(j => j.Kind).HasConversion<string>().HasMaxLength(32);
        builder.Property(j => j.State).HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.Trigger).HasConversion<string>().HasMaxLength(16);
        builder.Property(j => j.ParametersJson).HasColumnName("parameters").HasColumnType("jsonb");
        builder.Property(j => j.ProgressJson).HasColumnName("progress").HasColumnType("jsonb");
        builder.Property(j => j.ResultJson).HasColumnName("result").HasColumnType("jsonb");
        builder.Property(j => j.Error).HasMaxLength(JobStore.MaxErrorLength);
        builder.Property(j => j.WorkerId).HasMaxLength(200);

        builder.HasIndex(j => j.Kind)
            .HasDatabaseName(OneActivePerKindIndex)
            .IsUnique()
            .HasFilter("state IN ('Queued', 'Running')");

        // The claim query: the oldest queued job, or a running one whose heartbeat is stale.
        builder.HasIndex(j => new { j.State, j.QueuedAt });
        builder.HasIndex(j => new { j.Kind, j.QueuedAt });
    }
}
