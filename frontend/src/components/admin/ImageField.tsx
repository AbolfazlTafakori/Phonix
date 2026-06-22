"use client";

import { useRef, useState } from "react";
import { Spinner } from "./ui";
import AdminIcon from "./AdminIcon";

type Props = {
  value: string | null;
  onChange: (url: string) => void;
  label?: string;
  aspect?: "square" | "wide" | "logo";
  className?: string;
  // Opt-in secure mode for sensitive images (KYC, bank cards): `uploader` saves to protected storage and
  // returns the stored id; `srcFor` resolves the stored value to a streamable (authenticated) URL. When
  // omitted, the field keeps its default behaviour — public upload via /api/upload, value used as src.
  uploader?: (file: File) => Promise<string>;
  srcFor?: (value: string) => string;
};

const aspectCls: Record<NonNullable<Props["aspect"]>, string> = {
  square: "aspect-square",
  wide: "aspect-[4/3]",
  logo: "h-20",
};

export default function ImageField({ value, onChange, label, aspect = "square", className = "", uploader, srcFor }: Props) {
  const ref = useRef<HTMLInputElement>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState("");

  async function upload(file: File) {
    setBusy(true);
    setError("");
    try {
      if (uploader) {
        onChange(await uploader(file));
      } else {
        const fd = new FormData();
        fd.append("file", file);
        const res = await fetch("/api/upload", { method: "POST", body: fd });
        const data = await res.json();
        if (!res.ok) throw new Error(data.error ?? "خطا در آپلود");
        onChange(data.url as string);
      }
    } catch (e) {
      setError(e instanceof Error ? e.message : "خطا در آپلود");
    } finally {
      setBusy(false);
      if (ref.current) ref.current.value = "";
    }
  }

  const displaySrc = value ? (srcFor ? srcFor(value) : value) : null;

  return (
    <div className={className}>
      {label && <span className="mb-1.5 block text-xs font-medium text-white/55">{label}</span>}

      <div
        onClick={() => !busy && ref.current?.click()}
        className={`group relative grid w-full cursor-pointer place-items-center overflow-hidden rounded-xl border border-dashed border-white/15 bg-[#0d0d15] transition hover:border-[#e60053]/50 ${aspectCls[aspect]}`}
      >
        {displaySrc ? (
          <img src={displaySrc} alt="" className="h-full w-full object-contain" />
        ) : (
          <div className="flex flex-col items-center gap-1 text-white/35">
            <AdminIcon name="image" className="h-7 w-7" />
            <span className="text-[11px]">برای آپلود کلیک کنید</span>
          </div>
        )}

        {busy && (
          <div className="absolute inset-0 grid place-items-center bg-black/60">
            <Spinner className="h-6 w-6" />
          </div>
        )}

        {value && !busy && (
          <div className="absolute inset-0 grid place-items-center bg-black/0 opacity-0 transition group-hover:bg-black/40 group-hover:opacity-100">
            <span className="rounded-lg bg-white/15 px-3 py-1.5 text-xs font-bold text-white">تغییر تصویر</span>
          </div>
        )}
      </div>

      <div className="mt-2 flex items-center gap-2">
        <button
          type="button"
          onClick={() => ref.current?.click()}
          disabled={busy}
          className="flex h-9 flex-1 items-center justify-center gap-1.5 rounded-lg border border-white/10 text-xs font-bold text-white/80 transition hover:bg-white/5"
        >
          <AdminIcon name="plus" className="h-3.5 w-3.5" />
          آپلود تصویر
        </button>
        {value && (
          <button
            type="button"
            onClick={() => onChange("")}
            className="grid h-9 w-9 place-items-center rounded-lg border border-white/10 text-white/55 transition hover:border-rose-500/50 hover:text-rose-400"
          >
            <AdminIcon name="trash" className="h-4 w-4" />
          </button>
        )}
      </div>

      {error && <p className="mt-1.5 text-xs text-rose-400">{error}</p>}

      <input
        ref={ref}
        type="file"
        accept="image/*"
        className="hidden"
        onChange={(e) => {
          const f = e.target.files?.[0];
          if (f) upload(f);
        }}
      />
    </div>
  );
}
