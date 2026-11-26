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
    if ((isbn && normalizedIsbn(actual) !== isbn) || !title) return null;

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
        format: fields.get("encadernação") ?? fields.get("tipo de produto"),
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

async function extract(host, operation, input) {
    if (operation === "editionSearch" || operation === "editionResolve") return await extractEditions(host, operation, input);
    if (operation === "authorBiography") return await extractBiography(host, input);
    return await findProduct(host, input, async () => {
        const product = await readProduct(host, input);
        if (!product || operation === "cover" &&
            (!product.image || !await host.downloadImage(product.image, "navigate"))) return null;
        return product.metadata;
    });
}

async function findProduct(host, isbn, read) {
    const search = await host.goto(origin + "/pesquisa?keyword=" + isbn);
    if (!search) return null;
    if (productAddress(search)) return await read(search);

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
        const actual = await host.goto(candidate);
        if (productAddress(actual)?.split("/").at(-1) !== key) continue;
        const result = await read(actual);
        if (result) return result;
    }
    return null;
}


function authorKey(value) {
    return (value ?? "").normalize("NFD").toLowerCase().replace(/[^\p{L}\p{N}]/gu, "");
}

function biographyText(value) {
    const text = value?.replace(/\s+/g, " ").trim();
    return text && text.length <= 16384 ? text : null;
}

function authorAddress(value) {
    const path = value?.replace(/^https:\/\/www\.wook\.pt(?=\/)/i, "").split(/[?#]/, 1)[0];
    const match = /^(\/autor\/[^/]+\/(\d+))(?:\/\d+)?\/?$/.exec(path ?? "");
    return match ? { url: origin + match[1], id: match[2] } : null;
}

async function extractBiography(host, input) {
    const expected = authorKey(input.name);
    const candidates = new Map();
    for (const [editionIndex, edition] of input.editions.entries()) {
        const read = async editionUrl => {
            const isbn = edition.isbn;
            if (isbn) {
                const table = "table tr:has(> :first-child:is(:text-is('ISBN'), :text-is('ISBN:'))) > :nth-child(2)";
                const observed = (await host.text(table))[0]?.trim() ??
                    (await host.attribute("[itemprop='isbn'], meta[property='book:isbn']", "content"))[0]?.trim() ??
                    (await host.text("[itemprop='isbn']"))[0]?.trim();
                if (normalizedIsbn(observed) !== isbn) return null;
            }

            const selector = "table caption :is(.author, .authors) a[href^='#author-']";
            const names = await host.text(selector);
            const anchors = await host.attribute(selector, "href");
            const links = (await host.attribute("a[href*='/autor/']", "href")).map(authorAddress).filter(Boolean);
            let matched = false;
            for (const [index, name] of names.entries()) {
                if (authorKey(name) !== expected) continue;
                const id = /^#author-(\d+)$/.exec(anchors[index] ?? "")?.[1];
                if (!id) continue;
                const profiles = new Set(links.filter(link => link.id === id).map(link => link.url));
                if (profiles.size > 1) return { ambiguous: true };
                if (profiles.size === 1) {
                    const address = profiles.values().next().value;
                    if (!await host.authorEvidence(address, input.name)) continue;
                    matched = true;
                    candidates.set(id, { address, editionIndex, editionUrl, isbn });
                }
            }
            return matched ? { matched: true } : null;
        };
        const known = edition.externalId && productAddress("/" + edition.externalId.replace(/^\/+/, ""));
        let found = null;
        if (known) {
            const actual = await host.goto(known);
            if (productAddress(actual)?.split("/").at(-1) === known.split("/").at(-1)) found = await read(actual);
        }
        if (!found && edition.isbn) found = await findProduct(host, edition.isbn, read);
        if (found?.ambiguous || candidates.size > 1) return { ambiguous: true };
    }
    if (candidates.size !== 1) return null;
    const [id, { address, ...proof }] = candidates.entries().next().value;
    if (authorAddress(await host.goto(address))?.id !== id) return null;
    const person = '[itemtype="https://schema.org/Person"]';
    const name = (await host.text(person + " h1[itemprop='name']"))[0]?.trim();
    if (authorKey(name) !== expected) return null;
    const biography = biographyText((await host.text(person + " [itemprop='description'] .bio-text"))[0]);
    const photoUrl = (await host.attribute(person + " img[itemprop='image']", "src"))[0]?.trim() || undefined;
    return biography || photoUrl ? { ...proof, name, biography, photoUrl } : null;
}

function editionFormat(url) {
    return { livro: "Livro", ebook: "eBook", audiolivro: "Audiolivro" }[url.slice(origin.length + 1).split("/")[0]];
}

function coverAddress(value) {
    if (value?.startsWith("//")) return "https:" + value;
    if (value?.startsWith("/")) return origin + value;
    return value?.startsWith("https://") ? value : undefined;
}

function asEdition(product, url, externalId = productAddress(url).slice(origin.length + 1)) {
    const data = product.metadata;
    return {
        externalId, sourceUrl: url, title: data.title, author: data.authors.length ? data.authors.join(", ") : undefined,
        isbn: normalizedIsbn(data.isbn), language: data.language, publisher: data.publisher,
        year: data.publishedOn ? Number(data.publishedOn.slice(0, 4)) : undefined,
        coverUrl: coverAddress(product.image), format: product.format ?? editionFormat(productAddress(url)),
        details: { subtitle: data.subtitle, authors: data.authors, publishedOn: data.publishedOn,
            collection: data.collection, pages: data.pages ? String(data.pages) : undefined,
            synopsis: data.synopsis, genres: data.genres, hasStructuredAuthors: data.hasStructuredAuthors }
    };
}

async function extractEditions(host, operation, input) {
    if (operation === "editionResolve") {
        const expected = productAddress("/" + input.externalId);
        if (!expected || expected.slice(origin.length + 1) !== input.externalId) return [];
        const url = await host.goto(expected);
        if (productAddress(url)?.split("/").at(-1) !== expected.split("/").at(-1)) return [];
        const product = await readProduct(host);
        return product ? [asEdition(product, url, input.externalId)] : [];
    }

    const keyword = [input.title, input.author].filter(Boolean).join(" ");
    const url = await host.goto(origin + "/pesquisa?keyword=" + encodeURIComponent(keyword));
    if (!url) return [];
    if (productAddress(url)) {
        const product = await readProduct(host);
        return product ? [asEdition(product, url)] : [];
    }

    const area = ":is(#search-results, .search-results, #product-list, .product-list, .product-listing, main)";
    const link = "a[href*='/livro/'], a[href*='/ebook/'], a[href*='/audiolivro/']";
    const rows = await host.rows(area + " :is(article, li, .product-item, .product-card, [data-product-id])", [
        [link, "href"], ["h2, h3, .title, .book-title, .product-title, [itemprop='name']", null],
        [link, "title"], [".authors, .author, [itemprop='author']", null], [null, "data-isbn"],
        ["[itemprop='isbn'], .isbn", null], ["[itemprop='inLanguage'], .language", null],
        ["[itemprop='publisher'], .publisher", null], ["img[data-src]", "data-src"], ["img[src]", "src"],
        ["[itemprop='datePublished'], .publication-date, .year", null]
    ]);
    const fallbackLinks = await host.rows(area + " :is(" + link + "):not(:is(header *, footer *, nav *, aside *))",
        [[null, "href"], [null, null], [null, "title"]]);
    const editions = new Map();
    for (const [href, heading, label, author, isbn, isbnText, language, publisher, lazy, image, date] of rows.concat(fallbackLinks)) {
        const address = productAddress(href);
        const title = clean(heading) ?? clean(label);
        const key = address?.split("/").at(-1);
        if (!key || !title || editions.has(key)) continue;
        const published = publicationDate(clean(date));
        editions.set(key, {
            externalId: address.slice(origin.length + 1), title, author: clean(author), isbn: normalizedIsbn(isbn ?? isbnText),
            language: clean(language), publisher: clean(publisher), sourceUrl: address, format: editionFormat(address),
            coverUrl: coverAddress((lazy ?? image)?.replace(/\s+\d+(?:\.\d+)?[wx]$/, "")),
            year: published ? Number(published.slice(0, 4)) : undefined
        });
        if (editions.size === input.limit) break;
    }
    if (!editions.size && !(await host.text("body")).some(text =>
        /não foram encontrados resultados|não encontrámos resultados|não encontrou o que procura/i.test(text)))
        throw new Error("Wook search results could not be recognized.");
    return [...editions.values()];
}
