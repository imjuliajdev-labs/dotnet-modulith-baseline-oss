namespace Blog.PublicContracts.Queries;

public interface IBlogPublishedPostQueryService
{
    ValueTask<IReadOnlyCollection<BlogReadModel>> ListAsync(int limit, CancellationToken cancellationToken);
}
