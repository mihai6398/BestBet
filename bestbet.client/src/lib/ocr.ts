import { createWorker, OEM, PSM } from 'tesseract.js';
import type { Worker } from 'tesseract.js';
import workerUrl from 'tesseract.js/dist/worker.min.js?url';
import coreSimdUrl from 'tesseract.js-core/tesseract-core-simd-lstm.wasm.js?url';
import corePlainUrl from 'tesseract.js-core/tesseract-core-lstm.wasm.js?url';
import { layoutLines, normaliseOcrText } from './ocrText';
import type { OcrWord } from './ocrText';

/**
 * Reading a bookmaker's odds off a screenshot.
 *
 * This exists because the app the prices come from does not let you select its text. Retyping eight
 * prices off a second screen while a match is running is exactly where a digit gets lost, and a lost
 * digit here is not a typo — it is a stake sized against a price nobody is offering.
 *
 * Everything runs in the browser. The engine, its WebAssembly core and the English training data are
 * all served from this app, so a screenshot of someone's betting account is never uploaded anywhere,
 * and the import keeps working with no network at all.
 *
 * Accuracy is treated as a help, not as a source of truth: the transcript is handed back for the
 * user to correct before anything is parsed, let alone priced.
 */

export interface OcrProgress {
    /** 0..1 over the whole job, including the one-off engine load. */
    progress: number;
    /** True while the engine itself is still loading, which only happens on the first run. */
    loading: boolean;
}

export interface OcrReading {
    /** Rebuilt row by row from the word boxes, then cleaned up. */
    text: string;
    /** The engine's own mean confidence, 0..100. Below ~70 the transcript needs real checking. */
    confidence: number;
    /** True when the capture was read as light text on a dark background and inverted first. */
    inverted: boolean;
}

export type InvertMode = 'auto' | 'always' | 'never';

export interface OcrOptions {
    invert?: InvertMode;
    onProgress?: (progress: OcrProgress) => void;
}

// --- The engine -------------------------------------------------------------

let workerPromise: Promise<Worker> | null = null;

/** Set for the duration of one recognition, so the worker's single logger can address this call. */
let listener: ((progress: OcrProgress) => void) | null = null;

/** Statuses the engine reports while loading itself rather than while reading the image. */
const LOADING_STATUSES = ['loading tesseract core', 'initializing tesseract', 'loading language traineddata', 'initializing api'];

/**
 * SIMD has been in every desktop browser since 2021 and roughly halves the time this takes, but a
 * browser without it should get a slower read rather than a blank screen.
 */
const SIMD_PROBE = new Uint8Array([
    0, 97, 115, 109, 1, 0, 0, 0, 1, 5, 1, 96, 0, 1, 123, 3, 2, 1, 0, 10, 10, 1, 8, 0, 65, 0, 253, 15,
    253, 98, 11,
]);

const hasSimd = () => {
    try {
        return WebAssembly.validate(SIMD_PROBE);
    } catch {
        return false;
    }
};

function engine(): Promise<Worker> {
    workerPromise ??= createWorker('eng', OEM.LSTM_ONLY, {
        workerPath: workerUrl,
        corePath: hasSimd() ? coreSimdUrl : corePlainUrl,
        // Served from this app rather than from a CDN: the training data is the one piece big enough
        // to notice, and fetching it from someone else's server would make the import need the
        // internet to read an image that is already on the machine.
        langPath: `${import.meta.env.BASE_URL}tesseract/lang`,
        gzip: true,
        logger: (message) => {
            if (!listener) return;
            const loading = LOADING_STATUSES.includes(message.status);
            listener({ progress: message.progress, loading });
        },
    }).catch((error: unknown) => {
        // A failed load must not poison every later attempt — the next call gets a fresh worker.
        workerPromise = null;
        throw error;
    });

    return workerPromise;
}

/** Frees the WebAssembly instance. Worth doing when the panel closes; not required for correctness. */
export async function releaseOcr(): Promise<void> {
    const pending = workerPromise;
    workerPromise = null;
    if (!pending) return;

    try {
        await (await pending).terminate();
    } catch {
        // Nothing useful to do: the page is either going away or the worker was already gone.
    }
}

// --- Reading ----------------------------------------------------------------

export async function readOddsFromImage(source: Blob, options: OcrOptions = {}): Promise<OcrReading> {
    const prepared = await prepare(source, options.invert ?? 'auto');

    listener = options.onProgress ?? null;
    try {
        const worker = await engine();

        await worker.setParameters({
            // The odds block is one uniform grid. Left on AUTO, the engine tries to work out a
            // reading order for the columns and gets it wrong often enough to matter; SINGLE_BLOCK
            // keeps it reading straight across, and the row rebuild below fixes the rest.
            tessedit_pageseg_mode: PSM.SINGLE_BLOCK,
            preserve_interword_spaces: '1',
            // The capture has been upscaled, and saying so stops the engine guessing a DPI from it.
            user_defined_dpi: '300',
        });

        const { data } = await worker.recognize(prepared.canvas, {}, { text: true, blocks: true });

        const words = wordsOf(data.blocks);
        const text = words.length > 0 ? layoutLines(words).join('\n') : data.text;

        return {
            text: normaliseOcrText(text),
            confidence: Math.round(data.confidence),
            inverted: prepared.inverted,
        };
    } finally {
        listener = null;
    }
}

/** Flattens the engine's block/paragraph/line tree down to the only thing the layout pass needs. */
function wordsOf(blocks: Awaited<ReturnType<Worker['recognize']>>['data']['blocks']): OcrWord[] {
    const words: OcrWord[] = [];

    for (const block of blocks ?? []) {
        for (const paragraph of block.paragraphs ?? []) {
            for (const line of paragraph.lines ?? []) {
                for (const word of line.words ?? []) {
                    if (!word.text || word.text.trim() === '') continue;
                    words.push({
                        text: word.text,
                        x0: word.bbox.x0,
                        y0: word.bbox.y0,
                        x1: word.bbox.x1,
                        y1: word.bbox.y1,
                    });
                }
            }
        }
    }

    return words;
}

// --- Preparing the image ----------------------------------------------------

/** Below this the characters are too few pixels tall to be read reliably, so the capture is grown. */
const TARGET_WIDTH = 1600;
const MAX_SCALE = 4;
const MAX_PIXELS = 16e6;

/** Mean luminance below this reads as a dark interface, which is what every betting app ships. */
const DARK_THRESHOLD = 120;

/** Fraction of pixels ignored at each end when stretching the contrast, so one bright pixel cannot flatten the rest. */
const CLIP = 0.01;

async function prepare(source: Blob, invert: InvertMode) {
    const bitmap = await createImageBitmap(source);

    try {
        const scale = Math.min(
            MAX_SCALE,
            Math.max(1, TARGET_WIDTH / bitmap.width),
            Math.sqrt(MAX_PIXELS / (bitmap.width * bitmap.height)),
        );

        const canvas = document.createElement('canvas');
        canvas.width = Math.round(bitmap.width * scale);
        canvas.height = Math.round(bitmap.height * scale);

        const context = canvas.getContext('2d', { willReadFrequently: true });
        if (!context) throw new Error('This browser would not give a 2D canvas to work on.');

        context.imageSmoothingEnabled = true;
        context.imageSmoothingQuality = 'high';
        // A white bed first: a screenshot with transparency would otherwise read as black on black.
        context.fillStyle = '#ffffff';
        context.fillRect(0, 0, canvas.width, canvas.height);
        context.drawImage(bitmap, 0, 0, canvas.width, canvas.height);

        const inverted = enhance(context, canvas.width, canvas.height, invert);
        return { canvas, inverted };
    } finally {
        bitmap.close();
    }
}

/**
 * Grey, invert if the interface is dark, then stretch what is left over the full range.
 *
 * The engine does its own binarisation, and it does it well — on an image whose text and background
 * are far apart. A dark theme's dim grey on near-black is exactly the case where it is not, and
 * that is every bookmaker's live view.
 */
function enhance(
    context: CanvasRenderingContext2D,
    width: number,
    height: number,
    invert: InvertMode,
): boolean {
    const image = context.getImageData(0, 0, width, height);
    const pixels = image.data;
    const count = width * height;

    const grey = new Uint8ClampedArray(count);
    const histogram = new Uint32Array(256);
    let total = 0;

    for (let i = 0; i < count; i += 1) {
        const offset = i * 4;
        // Rec. 601 luma: the weights that match how the eye reads contrast, which is what the text
        // was designed against in the first place.
        const value =
            (pixels[offset] * 299 + pixels[offset + 1] * 587 + pixels[offset + 2] * 114) / 1000;
        const rounded = value < 0 ? 0 : value > 255 ? 255 : Math.round(value);

        grey[i] = rounded;
        histogram[rounded] += 1;
        total += rounded;
    }

    const shouldInvert =
        invert === 'always' || (invert === 'auto' && total / count < DARK_THRESHOLD);

    const low = percentile(histogram, count, CLIP);
    const high = percentile(histogram, count, 1 - CLIP);
    const span = Math.max(1, high - low);

    for (let i = 0; i < count; i += 1) {
        const stretched = ((grey[i] - low) / span) * 255;
        const clamped = stretched < 0 ? 0 : stretched > 255 ? 255 : stretched;
        const value = shouldInvert ? 255 - clamped : clamped;

        const offset = i * 4;
        pixels[offset] = value;
        pixels[offset + 1] = value;
        pixels[offset + 2] = value;
        pixels[offset + 3] = 255;
    }

    context.putImageData(image, 0, 0);
    return shouldInvert;
}

function percentile(histogram: Uint32Array, count: number, fraction: number): number {
    const target = count * fraction;
    let seen = 0;

    for (let value = 0; value < histogram.length; value += 1) {
        seen += histogram[value];
        if (seen >= target) return value;
    }

    return 255;
}

// --- Getting an image out of the page ---------------------------------------

/** Pulls the first image out of a paste or a drop, or null when there was not one. */
export function imageFrom(transfer: DataTransfer | null): File | null {
    for (const item of Array.from(transfer?.items ?? [])) {
        if (item.kind === 'file' && item.type.startsWith('image/')) {
            const file = item.getAsFile();
            if (file) return file;
        }
    }

    for (const file of Array.from(transfer?.files ?? [])) {
        if (file.type.startsWith('image/')) return file;
    }

    return null;
}
