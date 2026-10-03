// Phone cameras produce 3–8 MB photos. Sent as they are, a card or receipt photo takes minutes on a weak
// mobile connection, stalls until nginx drops it (408), or trips the server's 6 MB limit — and a HEIC/odd-format
// file is refused outright. The server only needs a readable picture: it re-encodes every upload anyway. So the
// photo is scaled down and re-saved as JPEG in the browser first, which turns megabytes into a few hundred KB.
//
// Never makes things worse: anything that can't be decoded here, or that would come out larger, is sent as it
// was and the server answers exactly as it did before.

const MAX_EDGE = 2000; // px on the long side — card numbers and receipt text stay sharp well below this
const QUALITY = 0.85;
// A JPEG/PNG/WebP already this small is sent untouched: nothing to gain, and no reason to re-encode it.
const SMALL_ENOUGH = 1024 * 1024;

const SERVER_FORMATS = /^image\/(jpeg|png|webp)$/;

export async function shrinkImage(file: File): Promise<File> {
  if (typeof document === "undefined") return file;
  const serverAccepts = SERVER_FORMATS.test(file.type);
  if (serverAccepts && file.size <= SMALL_ENOUGH) return file;

  const url = URL.createObjectURL(file);
  try {
    const img = new Image();
    img.src = url;
    // Browsers apply the EXIF orientation when decoding into an <img>, so the redrawn photo comes out upright.
    await img.decode();
    const { naturalWidth: w, naturalHeight: h } = img;
    if (!w || !h) return file;

    const scale = Math.min(1, MAX_EDGE / Math.max(w, h));
    const canvas = document.createElement("canvas");
    canvas.width = Math.max(1, Math.round(w * scale));
    canvas.height = Math.max(1, Math.round(h * scale));
    const ctx = canvas.getContext("2d");
    if (!ctx) return file;
    // JPEG has no transparency: without a backdrop a transparent PNG screenshot would turn black.
    ctx.fillStyle = "#fff";
    ctx.fillRect(0, 0, canvas.width, canvas.height);
    ctx.drawImage(img, 0, 0, canvas.width, canvas.height);

    const blob = await new Promise<Blob | null>((resolve) => canvas.toBlob(resolve, "image/jpeg", QUALITY));
    if (!blob) return file;
    if (serverAccepts && blob.size >= file.size) return file;

    const base = file.name.replace(/\.[^./\\]*$/, "") || "image";
    return new File([blob], `${base}.jpg`, { type: "image/jpeg", lastModified: Date.now() });
  } catch {
    // Not decodable in this browser (e.g. HEIC on Android Chrome): the server gives its usual answer.
    return file;
  } finally {
    URL.revokeObjectURL(url);
  }
}
