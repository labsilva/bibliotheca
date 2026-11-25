namespace Bibliotheca.Backend.Models;

internal readonly record struct BookPage(int Index, int Size)
{
    internal int Offset => Index * Size;

    internal static BookPage? From(int? page, int? size)
    {
        int index = page ?? 0;
        int count = size ?? 20;
        return count is < 1 or > 100 || index < 0 || index > int.MaxValue / count
            ? null : new BookPage(index, count);
    }
}
