namespace Bibliotheca.Backend.Models;

public record DashboardModel
{
    public DashboardPanel CurrentYearReadBooks { get; init; }
    public DashboardPanel ReadBooks { get; init; }
    public DashboardPanel UnreadBooks { get; init; }
    public DashboardPanel ReadingBooks { get; init; }
    public DashboardPanel TotalPagesRead { get; init; }
    public DashboardBook? CurrentBook { get; init; }
    public DashboardBook[] NextBooks { get; init; } = [];
    public int[] ReadByMonth { get; init; } = new int[12];
    public DashboardActivity[] Activity { get; init; } = [];
    public DashboardShelf[] Shelves { get; init; } = [];
    public int UnshelvedCount { get; init; }
    public int ActiveImports { get; init; }
    public int RecentFailedImports { get; init; }
}

public record DashboardBook(int Id, string Title, string Author, string? Shelf);
public record DashboardActivity(int Id, string Title, DateTime Date, string Kind);
public record DashboardShelf(string Name, int Count);

public record DashboardPanel
{
    public string Title { get; init; }
    public string Description { get; init; }
    public int Count { get; init; }
}
