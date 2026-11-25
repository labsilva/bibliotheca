using Bibliotheca.Backend.Fetchers;

namespace Bibliotheca.Backend.Services;

public abstract class FetcherBaseService
{
    public T[] PrioritizeFetchers<T>(IEnumerable<T> fetchers) 
    {
        return fetchers
            .Select(s => new
            {
                Fetcher = s,
                HasPriority = s.GetType().IsAssignableTo(typeof(IFetcherPriority)),
            })
            .Select(s => new
            {
                s.Fetcher,
                s.HasPriority,
                Priority = s.HasPriority ? s.Fetcher.GetType().GetProperty("Priority").GetValue(s.Fetcher) : 100
            })
            .OrderBy(o => o.Priority)
            .Select(s => s.Fetcher)
            .ToArray();
    }
}
