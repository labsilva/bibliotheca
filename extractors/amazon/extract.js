function isbn10(isbn) {
    if (!isbn.startsWith("978")) return null;
    const digits = isbn.slice(3, 12);
    const sum = [...digits].reduce((total, digit, index) => total + Number(digit) * (10 - index), 0);
    const check = (11 - sum % 11) % 11;
    return digits + (check === 10 ? "X" : check);
}

async function extract(host, operation, isbn) {
    if (operation !== "cover" || !await host.goto("https://www.amazon.com/s?k=" + isbn)) return null;
    const identifiers = [isbn, isbn10(isbn)].filter(Boolean);
    for (const identifier of identifiers) {
        const candidate = '[data-component-type="s-search-result"][data-asin="' + identifier + '"]';
        const image = (await host.attribute(candidate + " img.s-image", "src"))[0];
        const title = (await host.text(candidate + " h2"))[0]?.trim();
        if (image && title && await host.downloadImage(image, "navigate")) return { isbn, title };
    }
    return null;
}
