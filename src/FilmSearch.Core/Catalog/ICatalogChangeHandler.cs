namespace FilmSearch.Core.Catalog;

/// <summary>
/// Called by <see cref="ICatalogWriter"/> inside its write transaction, after the changes are saved, with the ids
/// of the movies that were created or whose data changed. Lets other modules keep data derived from the catalog
/// in sync (keyword search documents from I04; embeddings from I06). A failing handler rolls back the batch.
/// </summary>
public interface ICatalogChangeHandler
{
    Task OnMoviesChangedAsync(IReadOnlyCollection<Guid> movieIds, CancellationToken cancellationToken);
}
