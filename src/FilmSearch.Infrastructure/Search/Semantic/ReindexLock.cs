using Microsoft.EntityFrameworkCore;

namespace FilmSearch.Infrastructure.Search.Semantic;

/// <summary>
/// A session-level PostgreSQL advisory lock held for the length of a reindex (which spans many transactions). A second
/// reindex, in this or another process, waits for the first. The context's connection is opened by hand, so EF keeps it
/// (and the lock) for every query of the reindex; closing it at the end releases the lock even if the unlock fails.
/// </summary>
internal sealed class ReindexLock : IAsyncDisposable
{
    private const long Key = 0x46_53_45_4D_42; // "FSEMB"

    private readonly DbContext _db;

    private ReindexLock(DbContext db) => _db = db;

    public static async Task<ReindexLock> AcquireAsync(DbContext db, CancellationToken cancellationToken)
    {
        await db.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_lock({Key})", cancellationToken);
            return new ReindexLock(db);
        }
        catch
        {
            await db.Database.CloseConnectionAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _db.Database.ExecuteSqlAsync($"SELECT pg_advisory_unlock({Key})", CancellationToken.None);
        }
        catch (Exception ex) when (ex is Npgsql.NpgsqlException or InvalidOperationException)
        {
            // The connection is closed below; that releases the lock.
        }
        finally
        {
            await _db.Database.CloseConnectionAsync();
        }
    }
}
