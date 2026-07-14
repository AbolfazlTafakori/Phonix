// Serves images through Next's optimizer (/_next/image): the source file is re-encoded to AVIF/WebP and
// resized to the width the layout actually needs, then cached. Files on disk are never touched.
//
// This renders a plain <img> rather than next/image on purpose: almost every call site sizes itself with
// CSS (w-full + aspect-*, object-cover) and has no intrinsic width/height to hand over, which is exactly
// the case next/image cannot express without a wrapper and a layout rewrite.
//
// Only same-origin static assets are optimized. Anything the backend serves (/api/upload/{id}) or any
// absolute URL is passed through untouched, so user uploads keep their current delivery path.

const WIDTHS = [64, 128, 256, 384, 640, 828, 1080, 1200, 1920];

function optimized(src: string, width: number, quality: number) {
  return `/_next/image?url=${encodeURIComponent(src)}&w=${width}&q=${quality}`;
}

function isOptimizable(src: string) {
  return src.startsWith("/") && !src.startsWith("/api/") && !src.startsWith("/_next/");
}

type Props = {
  src: string;
  alt: string;
  /** Rendered width, per the CSS. Drives which srcSet candidate the browser picks — keep it honest. */
  sizes: string;
  className?: string;
  /** Set on the LCP image (hero, above-the-fold) so it is fetched eagerly at high priority. */
  priority?: boolean;
  quality?: number;
  style?: React.CSSProperties;
  onError?: React.ReactEventHandler<HTMLImageElement>;
};

export default function Img({ src, alt, sizes, className, priority = false, quality = 75, style, onError }: Props) {
  const loading = priority ? "eager" : "lazy";

  if (!src || !isOptimizable(src)) {
    return (
      <img
        loading={loading}
        decoding="async"
        fetchPriority={priority ? "high" : undefined}
        src={src}
        alt={alt}
        className={className}
        style={style}
        onError={onError}
      />
    );
  }

  return (
    <img
      loading={loading}
      decoding="async"
      fetchPriority={priority ? "high" : undefined}
      src={optimized(src, 1200, quality)}
      srcSet={WIDTHS.map((w) => `${optimized(src, w, quality)} ${w}w`).join(", ")}
      sizes={sizes}
      alt={alt}
      className={className}
      style={style}
      onError={onError}
    />
  );
}
