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

const origin = "https://www.goodreads.com";
const bookPath = /^\/book\/show\/\d+[\w.-]*(?:[?#].*)?$/;

function bookAddress(value) {
    const path = value?.replace(/^https:\/\/www\.goodreads\.com(?=\/)/i, "");
    return bookPath.test(path) ? origin + path.split(/[?#]/, 1)[0] : null;
}

function clean(value) {
    return value?.replace(/\s+/g, " ").trim() || undefined;
}

function publicationDate(value) {
    const date = /^(\w+)\s+(\d{1,2}),?\s+(\d{4})/.exec(value ?? "");
    if (!date) return undefined;
    const months = ["January", "February", "March", "April", "May", "June",
        "July", "August", "September", "October", "November", "December"];
    const month = months.indexOf(date[1]) + 1;
    return month ? date[3] + "-" + String(month).padStart(2, "0") + "-01" : undefined;
}

async function findBook(host, isbn) {
    const search = await host.goto(origin + "/search?query=" + isbn);
    if (!search) return null;
    const details = "button[aria-label='Book details and editions']";
    let product = bookAddress(search);
    if (!product) {
        const links = await host.attribute("main a.bookTitle[href], #searchResults a.bookTitle[href], a.bookTitle[href]", "href");
        product = links.map(bookAddress).find(Boolean);
        if (!product) return null;
        const actual = await host.goto(product);
        if (!bookAddress(actual)) return null;
        product = actual;
    } else {
        product = search;
    }
    await host.click(details);
    await host.waitForSelector(".EditionDetails .DescListItem:has(> dt:text-is(\"ISBN\")) > dd");
    const detail = label => readDetail(host, label);
    const editionIsbn = (await detail("ISBN"))?.match(/^[0-9Xx-]+/)?.[0];
    if (normalizedIsbn(editionIsbn) !== isbn) return null;

    return { editionUrl: product, isbn: editionIsbn };
}

async function readDetail(host, label) {
    return clean((await host.text(".EditionDetails .DescListItem:has(> dt:text-is(\"" + label + "\")) > dd"))[0]);
}

async function extract(host, operation, input) {
    if (operation === "authorBiography") return await extractBiography(host, input);
    const book = await findBook(host, input);
    if (!book) return null;
    const first = async selector => clean((await host.text(selector))[0]);
    const detail = label => readDetail(host, label);
    const title = await first(".BookPageTitleSection__title h1");
    if (operation === "cover") {
        const images = await host.attribute(".BookCover img, img[data-testid='coverImage'], img[role='presentation']", "src");
        for (const image of [...new Set(images.filter(Boolean))].slice(0, 3))
            if (title && await host.downloadImage(image, "navigate")) return { isbn: book.isbn, title };
        return null;
    }
    if (!title) return null;
    const published = (await detail("Published"))?.split(" by ");
    const pages = Number(/^(\d+)\s+pages/i.exec(await detail("Format") ?? "")?.[1]);
    const authors = ':is(.ContributorLinksList, .BookPageMetadataSection__contributor) ' +
        '.ContributorLink:not(:has(.ContributorLink__role:text-matches("illustrat", "i"))) .ContributorLink__name';
    return {
        isbn: book.isbn,
        title,
        subtitle: await first(".BookPageTitleSection__title h3.Text__title3 a"),
        authors: await structuredAuthors(host, book.editionUrl) ?? (await host.text(authors)).map(clean).filter(Boolean),
        synopsis: await first(".BookPageMetadataSection__description"),
        genres: (await host.text(".BookPageMetadataSection__genreButton")).map(clean).filter(Boolean),
        language: await detail("Language") ?? "English",
        collection: await detail("Series"),
        publisher: published?.length > 1 ? published.at(-1) : undefined,
        publishedOn: publicationDate(published?.[0]),
        pages: Number.isInteger(pages) && pages > 0 && pages <= 100000 ? pages : undefined
    };
}


function authorKey(value) {
    return (value ?? "").normalize("NFD").toLowerCase().replace(/[^\p{L}\p{N}]/gu, "");
}

function biographyText(value) {
    const text = value?.replace(/\s+/g, " ").trim();
    return text && text.length <= 16384 ? text : null;
}

function authorAddress(value) {
    const path = value?.replace(/^https:\/\/www\.goodreads\.com(?=\/)/i, "").split(/[?#]/, 1)[0];
    const match = /^\/author\/show\/(\d+)(?:[.\w-]+)?\/?$/.exec(path ?? "");
    return match ? { url: origin + path, id: match[1] } : null;
}

async function extractBiography(host, input) {
    const expected = authorKey(input.name);
    const candidates = new Map();
    const selector = ':is(.ContributorLinksList, .BookPageMetadataSection__contributor) ' +
        'a.ContributorLink[href*="/author/show/"]:not(:has(.ContributorLink__role:text-matches("illustrat", "i")))';
    for (const [editionIndex, edition] of input.editions.entries()) {
        const book = edition.isbn && await findBook(host, edition.isbn);
        if (!book) continue;
        const names = await host.text(selector + " .ContributorLink__name");
        const links = await host.attribute(selector + ":has(.ContributorLink__name)", "href");
        for (const [index, name] of names.entries()) {
            if (authorKey(name) !== expected) continue;
            const profile = authorAddress(links[index]);
            if (profile && await host.authorEvidence(profile.url, input.name)) candidates.set(profile.id, { ...book, editionIndex, address: profile.url });
        }
        if (candidates.size > 1) return { ambiguous: true };
    }
    if (candidates.size !== 1) return null;
    const [id, { address, ...proof }] = candidates.entries().next().value;
    if (authorAddress(await host.goto(address))?.id !== id) return null;
    const name = clean((await host.text("h1.authorName, h1[data-testid='name'], h1.AuthorPage__name"))[0]);
    if (authorKey(name) !== expected) return null;
    const biography = biographyText((await host.text(".aboutAuthorInfo span[id^='freeText']:not([id^='freeTextContainer'])"))[0]) ??
        biographyText((await host.text("[data-testid='authorBiography'], .AuthorPage__biography, .aboutAuthorInfo"))[0]);
    return biography ? { ...proof, name, biography } : null;
}

function isBookAuthor(role) {
    return ["", "author", "co-author", "coauthor", "writer", "adaptor", "adapter", "adaptation", "story"]
        .includes((role ?? "").trim().replace(/^\(|\)$/g, "").trim().toLowerCase());
}

async function structuredAuthors(host, url) {
    const bookId = /\/book\/show\/(\d+)/.exec(url)?.[1];
    const selector = "script#__NEXT_DATA__";
    if (!bookId || !(await host.attribute(selector, "id")).length) return undefined;
    const state = ["props", "pageProps", "apolloState"];
    const reference = await host.json(selector, [...state, "ROOT_QUERY",
        'getBookByLegacyId({"legacyId":"' + bookId + '"})', "__ref"]);
    if (typeof reference !== "string") return undefined;
    const primary = await host.json(selector, [...state, reference, "primaryContributorEdge"]);
    const secondary = await host.json(selector, [...state, reference, "secondaryContributorEdges"]);
    if (!primary && !Array.isArray(secondary)) return undefined;
    const authors = [];
    for (const edge of [primary, ...(secondary ?? [])].filter(Boolean).slice(0, 24)) {
        if (!isBookAuthor(edge.role)) continue;
        const name = clean(edge.node?.name ?? (edge.node?.__ref
            ? await host.json(selector, [...state, edge.node.__ref, "name"]) : undefined));
        if (name && !authors.some(author => author.toLowerCase() === name.toLowerCase())) authors.push(name);
    }
    return authors;
}
