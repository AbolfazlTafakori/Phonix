// Phone cameras produce 3–8 MB photos. Sent as they are, a card or receipt photo takes minutes on a weak
// mobile connection, stalls until nginx drops it (408), or trips the server's 6 MB limit — and a HEIC/odd-format
// file is refused outright. The server only needs a readable picture: it re-encodes every upload anyway. So the
// photo is scaled down and re-saved in the browser first, which turns megabytes into a few hundred KB.
//
// Never makes things worse: anything that can't be decoded here, that would come out larger, or that takes
// too long, is sent as it was and the server answers exactly as it did before.

const QUALITY = 0.85;
// A JPEG/PNG/WebP already this small is sent untouched: nothing to gain, and no reason to re-encode it.
const SMALL_ENOUGH = 1024 * 1024;
// A step that hasn't finished by now isn't going to help: send the original instead of leaving the upload hanging.
const STEP_TIMEOUT_MS = 10_000;

const SERVER_FORMATS = /^image\/(jpeg|png|webp)$/;

type ShrinkOptions = {
  // JPEG for customer photos (cards, receipts) — smallest, and they have no transparency to lose. WebP for site
  // imagery, which can be a transparent logo or screenshot. A browser that can't encode WebP hands back PNG,
  // which the server converts anyway.
  format?: "jpeg" | "webp";
  // px on the long side. 2000 keeps card numbers and receipt text sharp; site images get more room.
  maxEdge?: number;
};

function withTimeout<T>(work: Promise<T>): Promise<T> {
  return Promise.race([
    work,
    new Promise<T>((_, reject) => setTimeout(() => reject(new Error("image step timed out")), STEP_TIMEOUT_MS)),
  ]);
}

type Decoded = { source: CanvasImageSource; width: number; height: number; release: () => void };

// createImageBitmap first: it decodes off the page and keeps working in a background tab, where an <img>'s
// decode() waits until the tab is shown again. Both apply the photo's EXIF orientation, so it comes out upright.
// The <img> path stays for browsers that can't decode a given format through createImageBitmap.
async function decode(file: File): Promise<Decoded> {
  if (typeof createImageBitmap === "function") {
    try {
      const bmp = await withTimeout(createImageBitmap(file, { imageOrientation: "from-image" }));
      return { source: bmp, width: bmp.width, height: bmp.height, release: () => bmp.close() };
    } catch {
      /* fall back to <img> */
    }
  }
  const url = URL.createObjectURL(file);
  try {
    const img = new Image();
    img.src = url;
    await withTimeout(img.decode());
    return { source: img, width: img.naturalWidth, height: img.naturalHeight, release: () => URL.revokeObjectURL(url) };
  } catch (e) {
    URL.revokeObjectURL(url);
    throw e;
  }
}

export async function shrinkImage(file: File, { format = "jpeg", maxEdge = 2000 }: ShrinkOptions = {}): Promise<File> {
  if (typeof document === "undefined") return file;
  const serverAccepts = SERVER_FORMATS.test(file.type);
  if (serverAccepts && file.size <= SMALL_ENOUGH) return file;

  let decoded: Decoded | null = null;
  try {
    decoded = await decode(file);
    const { width: w, height: h } = decoded;
    if (!w || !h) return file;

    const scale = Math.min(1, maxEdge / Math.max(w, h));
    const canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(w * scale));
    canvas.height = Math.max(1, Math.round(h * scale));
    const ctx = canvas.getContext("2d");
    if (!ctx) return file;
    if (format === "jpeg") {
      // JPEG has no transparency: without a backdrop a transparent PNG screenshot would turn black.
      ctx.fillStyle = "#fff";
      ctx.fillRect(0, 0, canvas.width, canvas.height);
    }
    ctx.drawImage(decoded.source, 0, 0, canvas.width, canvas.height);

    const mime = format === "webp" ? "image/webp" : "image/jpeg";
    const blob = await withTimeout(new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, mime, QUALITY)));
    if (!blob) return file;
    if (serverAccepts && blob.size >= file.size) return file;

    const ext = blob.type === "image/webp" ? "webp" : blob.type === "image/png" ? "png" : "jpg";
    const base = file.name.replace(/\.[^./\\]*$/, "") || "image";
    return new File([blob], `${base}.${ext}`, { type: blob.type || mime, lastModified: Date.now() });
  } catch {
    // Not decodable in this browser (e.g. HEIC on Android Chrome), or too slow: the server gives its usual answer.
    return file;
  } finally {
    decoded?.release();
  }
}
