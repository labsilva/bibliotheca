function normalizedIsbn(value) {
    const isbn = String(value ?? "").replace(/[- ]/g, "").toUpperCase();
    if (/^[0-9]{13}$/.test(isbn)) return isbn;
    if (!/^[0-9]{9}[0-9X]$/.test(isbn)) return null;
    const digits = isbn.slice(0, 9);
    const check = (11 - [...digits].reduce((total, digit, index) => total + Number(digit) * (10 - index), 0) % 11) % 11;
    if (isbn[9] !== (check === 10 ? "X" : String(check))) return null;
    const prefix = "978" + digits;
    const sum = [...prefix].reduce((total, digit, index) => total + Number(digit) * (index % 2 ? 3 : 1), 0);
    return prefix + (10 - sum % 10) % 10;
}

async function extract(host, operation, isbn) {
    if (operation !== "cover") return null;
    const url = "https://isbndb.com/book/" + isbn;
    const product = await host.goto(url);
    if (product?.split(/[?#]/, 1)[0].replace(/\/$/, "") !== url) return null;

    const fields = await host.attribute("meta[property='book:isbn'], meta[itemprop='isbn']", "content");
    fields.push(...await host.attribute("[data-isbn]", "data-isbn"));
    const displayed = await host.text("[itemprop='isbn'], .book-isbn, .isbn, .isbn13, .isbn10, tr:has(th:text-matches('ISBN', 'i')) td");
    const candidates = fields.concat(displayed.flatMap(value => value.match(/(?:\d[- ]*){9,12}[\dXx](?!\d)/g) ?? []));
    for (const block of await host.text("script[type='application/ld+json']")) {
        try {
            const parsed = JSON.parse(block);
            for (const row of [parsed, ...(Array.isArray(parsed) ? parsed : parsed?.["@graph"] ?? [])]) {
                candidates.push(row?.isbn, row?.gtin13);
            }
        } catch {
            // Other metadata blocks may still identify the edition.
        }
    }
    if (!candidates.some(value => normalizedIsbn(value) === isbn)) return null;

    const title = (await host.text("h1"))[0]?.trim() ??
        (await host.attribute("meta[property='og:title']", "content"))[0]?.trim();
    const image = (await host.attribute("div.artwork > object:first-child", "data"))[0] ??
        (await host.attribute("div.artwork img", "src"))[0];
    return title && image && await host.downloadImage(image, "navigate") ? { isbn, title } : null;
}
