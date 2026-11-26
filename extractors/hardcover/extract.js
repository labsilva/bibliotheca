async function extract(host, operation) {
    if (operation === "editionResolve") return await host.hardcover("resolve");

    const works = await host.hardcover("search");
    if (!works.length) return [];
    const books = await host.hardcover("editions");
    const editionsByWork = new Map(books.map(book => [book.id, book.editions]));
    return works.flatMap(id => editionsByWork.get(id) ?? []);
}
