/**
 * Turns the HTML a browser puts on the clipboard into line-structured plain text.
 *
 * When you copy from a web page the clipboard carries two flavours: `text/plain`, which has already
 * been flattened and often loses which numbers belonged to which row, and `text/html`, which still
 * has the table and div structure. Reading the HTML flavour and rebuilding the lines from the markup
 * is markedly more reliable than parsing the flattened text — a bookmaker's odds grid survives it.
 *
 * SAFETY: the markup is parsed with DOMParser into a detached, inert document. Scripts do not run,
 * resources do not load, and nothing here ever reaches the live DOM — only text is read back out.
 * Pasted markup must never be handed to innerHTML or dangerouslySetInnerHTML anywhere downstream.
 */

/** Elements whose content is not text a human pasted.  */
const SKIP = new Set(['SCRIPT', 'STYLE', 'NOSCRIPT', 'TEMPLATE', 'HEAD', 'SVG']);

/** Elements that end a line. Table cells count: each cell is its own value in an odds grid. */
const BLOCK = new Set([
    'ADDRESS', 'ARTICLE', 'ASIDE', 'BLOCKQUOTE', 'DD', 'DIV', 'DL', 'DT', 'FIELDSET',
    'FIGCAPTION', 'FIGURE', 'FOOTER', 'FORM', 'H1', 'H2', 'H3', 'H4', 'H5', 'H6',
    'HEADER', 'HR', 'LI', 'MAIN', 'NAV', 'OL', 'P', 'PRE', 'SECTION', 'TABLE',
    'TBODY', 'TD', 'TFOOT', 'TH', 'THEAD', 'TR', 'UL',
]);

/**
 * The HTML flavour of a paste, rebuilt as text, or null when the clipboard had no markup.
 * Callers fall back to the plain-text flavour, which the textarea would have taken anyway.
 */
export function readPastedHtml(clipboard: DataTransfer | null): string | null {
    const html = clipboard?.getData('text/html');
    if (!html || html.trim() === '') {
        return null;
    }

    const text = htmlToLines(html);
    return text.trim() === '' ? null : text;
}

export function htmlToLines(html: string): string {
    // Guard rather than trust: a runtime without DOMParser, or markup so broken it throws, should
    // degrade to "no HTML flavour" rather than take the paste down with it.
    let document: Document;
    try {
        document = new DOMParser().parseFromString(html, 'text/html');
    } catch {
        return '';
    }

    const pieces: string[] = [];
    collect(document.body, pieces);

    return pieces
        .join(' ')
        .replace(/[^\S\n]*\n[^\S\n]*/g, '\n')   // trim spaces hugging a break
        .replace(/\n{2,}/g, '\n')                // collapse the runs that nesting produces
        .replace(/[^\S\n]{2,}/g, ' ')
        .trim();
}

function collect(node: Node | null, pieces: string[]): void {
    if (!node) return;

    if (node.nodeType === 3 /* TEXT_NODE */) {
        const text = node.textContent?.replace(/\s+/g, ' ').trim();
        if (text) pieces.push(text);
        return;
    }

    if (node.nodeType !== 1 /* ELEMENT_NODE */) return;

    const element = node as Element;
    const tag = element.tagName.toUpperCase();
    if (SKIP.has(tag)) return;

    if (tag === 'BR') {
        pieces.push('\n');
        return;
    }

    const isBlock = BLOCK.has(tag);
    if (isBlock) pieces.push('\n');

    for (const child of Array.from(element.childNodes)) {
        collect(child, pieces);
    }

    if (isBlock) pieces.push('\n');
}
