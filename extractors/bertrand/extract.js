function* records(value) {
    if (Array.isArray(value)) {
        for (const item of value) yield* records(item);
    } else if (value && typeof value === "object") {
        yield value;
        yield* records(value["@graph"]);
    }
}

function text(value) {
    return typeof value === "string" && value.trim() ? value.trim() : undefined;
}

function name(value) {
    return text(value) ?? text(value?.name);
}

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

function isProduct(url) {
    return /^https:\/\/www\.bertrand\.pt\/livro\/[^/?#]+\/\d+\/?(?:[?#]|$)/i.test(url);
}

async function readProduct(host, isbn) {
    for (const block of await host.text("script[type='application/ld+json']")) {
        let data;
        try {
            data = JSON.parse(block);
        } catch {
            continue;
        }
        for (const item of records(data)) {
            const candidate = item.isbn ?? item.gtin13 ?? item.gtin;
            const title = text(item.name);
            if (normalizedIsbn(candidate) !== isbn || !title) continue;
            const authors = (Array.isArray(item.author) ? item.author : [item.author]).map(name).filter(Boolean);
            const date = text(item.datePublished);
            const pages = Number(item.numberOfPages);
            return {
                metadata: {
                    isbn: String(candidate),
                    title,
                    authors,
                    publisher: name(item.publisher),
                    publishedOn: /^\d{4}-\d{2}-\d{2}$/.test(date ?? "") ? date : undefined,
                    synopsis: text(item.description),
                    language: text(item.inLanguage),
                    pages: Number.isSafeInteger(pages) && pages > 0 ? pages : undefined
                },
                image: text(item.image) ?? text(item.image?.url)
            };
        }
    }
    return null;
}

async function extract(host, operation, isbn) {
    const origin = "https://www.bertrand.pt";
    const search = await host.goto(origin + "/pesquisa/" + isbn);
    if (!search) return null;

    let product = null;
    if (isProduct(search)) {
        product = await readProduct(host, isbn);
    } else {
        const links = await host.attribute("a[href*='/livro/']", "href");
        const candidates = new Set();
        for (const link of links) {
            const absolute = link.startsWith("/livro/") ? origin + link : link;
            if (isProduct(absolute)) candidates.add(absolute.split(/[?#]/, 1)[0]);
        }
        for (const candidate of [...candidates].slice(0, 10)) {
            if (!isProduct(await host.goto(candidate))) continue;
            product = await readProduct(host, isbn);
            if (product) break;
        }
    }
    if (!product) return null;
    if (operation === "cover" && (!product.image || !await host.downloadImage(product.image))) return null;
    return product.metadata;
}
