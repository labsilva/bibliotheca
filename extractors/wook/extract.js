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

const origin = "https://www.wook.pt";
const productPath = /^\/(?:en\/)?(?:livro|ebook|audiolivro)\/[a-zA-Z0-9][a-zA-Z0-9._~-]{0,159}\/[0-9]{1,12}\/?$/;

function productAddress(value) {
    const path = value?.replace(/^(?:https:)?\/\/(?:www\.)?wook\.pt(?=\/)/i, "").split(/[?#]/, 1)[0];
    return productPath.test(path) ? origin + path.replace(/^\/en\//, "/").replace(/\/$/, "") : null;
}

function clean(value) {
    return value?.replace(/\\n/g, "").replace(/\s+/g, " ").trim() || undefined;
}

function publicationDate(value) {
    if (/^\d{4}-\d{2}-\d{2}$/.test(value ?? "")) return value;
    const numeric = /^(\d{2})\/(\d{2})\/(\d{4})$/.exec(value ?? "");
    if (numeric) return numeric[3] + "-" + numeric[2] + "-" + numeric[1];
    if (/^\d{4}(?:-\d{2})?$/.test(value ?? "")) return value + (value.length === 4 ? "-01-01" : "-01");
    const months = ["janeiro", "fevereiro", "março", "abril", "maio", "junho",
        "julho", "agosto", "setembro", "outubro", "novembro", "dezembro"];
    const match = /^(?:(\d{1,2}) )?([a-zç]+) (\d{4})$/.exec(value?.toLowerCase().replace(/ de /g, " ") ?? "");
    if (!match) return undefined;
    const month = months.indexOf(match[2] === "marco" ? "março" : match[2]) + 1;
    return month ? match[3] + "-" + String(month).padStart(2, "0") + "-" + (match[1] ?? "1").padStart(2, "0") : undefined;
}

async function readProduct(host, isbn) {
    const rowSelector = " tr:has(> :nth-child(2))";
    let rows = "table:has(caption p.font-medium, caption p.authors)" + rowSelector;
    let labels = await host.text(rows + " > :first-child");
    if (!labels.length) {
        rows = "table" + rowSelector;
        labels = await host.text(rows + " > :first-child");
    }
    const values = await host.text(rows + " > :nth-child(2)");
    const fields = new Map(labels.map((label, index) => [clean(label)?.replace(/:$/, "").toLowerCase(), clean(values[index])]));
    const firstText = async selector => clean((await host.text(selector))[0]);
    const firstAttribute = async (selector, name) => clean((await host.attribute(selector, name))[0]);
    const property = async (name, attribute = "content") =>
        await firstAttribute("[itemprop='" + name + "']", attribute) ?? await firstText("[itemprop='" + name + "']");
    const actual = fields.get("isbn") ?? await firstAttribute("meta[property='book:isbn']", "content") ?? await property("isbn");
    const title = await firstText("caption p.font-medium, caption [itemprop='name']") ?? await firstText("h1");
    if (normalizedIsbn(actual) !== isbn || !title) return null;

    const byline = ".authors, .author, .product-author, [itemprop='author'], [class*='product-author']";
    const excluded = ":not(:is(.illustrator, [itemprop='illustrator'], [data-role='illustrator' i], .illustrator *, [itemprop='illustrator'] *, [data-role='illustrator' i] *))";
    const rawByline = (await host.text(byline))[0];
    const credit = /(?:^|[;\n])\s*(?:ilustra(?:ção|ções|dor(?:a|es|as)?)|illustrat(?:ion|ors?))\s*:/i;
    const names = rawByline?.split(credit)[0];
    const authors = (await host.text(":is(" + byline + ") a[href*='/autor/']" + excluded))
        .map(clean).filter(name => name && (!credit.test(rawByline ?? "") || clean(names)?.includes(name)));
    const structured = authors.length > 0;
    if (!structured) {
        for (const name of names?.replace(/^de /i, "").split(";") ?? []) {
            const parts = name.split(" e ").map(clean).filter(Boolean);
            authors.push(...(parts.every(part => part.includes(" ")) ? parts : [clean(name)].filter(Boolean)));
        }
    }

    const image = await firstAttribute("meta[property='og:image']", "content") ??
        await firstAttribute("[itemprop='image']", "content") ??
        await firstAttribute("img[itemprop='image']", "src") ??
        await firstAttribute("img[data-src]", "data-src") ??
        await firstAttribute("img[src]", "src");
    const genre = (fields.get("classificação temática") ?? fields.get("classificação"))?.split(">").map(clean).filter(Boolean).at(-1);
    const pages = Number(fields.get("páginas"));
    return {
        image: image?.replace(/\s+\d+(?:\.\d+)?[wx]$/, ""),
        metadata: {
            isbn: actual, title, authors, hasStructuredAuthors: structured,
            subtitle: await firstText("caption .subtitle, .product-subtitle"),
            publisher: fields.get("editor") ?? fields.get("editora") ?? await property("publisher"),
            publishedOn: publicationDate(fields.get("data de lançamento") ?? fields.get("lançamento") ??
                await firstAttribute("[itemprop='datePublished']", "datetime") ?? await property("datePublished")),
            synopsis: await firstText("#synopsis-toggle-collapse") ?? await property("description"),
            language: fields.get("idioma") ?? fields.get("língua") ?? await property("inLanguage"),
            pages: Number.isInteger(pages) && pages > 0 && pages <= 100000 ? pages : undefined,
            collection: fields.get("coleção"), genres: genre ? [genre] : undefined
        }
    };
}

async function extract(host, operation, isbn) {
    const search = await host.goto(origin + "/pesquisa?keyword=" + isbn);
    if (!search) return null;

    const read = async () => {
        const product = await readProduct(host, isbn);
        if (!product || operation === "cover" &&
            (!product.image || !await host.downloadImage(product.image, "navigate"))) return null;
        return product.metadata;
    };
    if (productAddress(search)) return await read();

    const results = "#search-results, .search-results, #product-list, .product-list, .product-listing";
    const resultLinks = " a[href]:not(:is(header *, footer *, nav *, aside *))";
    let links = await host.attribute(":is(" + results + ")" + resultLinks, "href");
    if (!links.length) links = await host.attribute("main" + resultLinks, "href");
    if (!links.length) links = await host.attribute("body" + resultLinks, "href");
    const candidates = new Map();
    for (const link of links) {
        const candidate = productAddress(link);
        const key = candidate?.split("/").at(-1);
        if (key && !candidates.has(key)) candidates.set(key, candidate);
    }
    for (const [key, candidate] of [...candidates].slice(0, 5)) {
        if (productAddress(await host.goto(candidate))?.split("/").at(-1) !== key) continue;
        const result = await read();
        if (result) return result;
    }
    return null;
}
