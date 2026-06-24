"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { api } from "@/lib/api";
import type { LogFile, LogView } from "@/lib/types";
import { toFa } from "@/lib/format";
import { Card, PageHeader, Spinner } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

const fmtDateTime = new Intl.DateTimeFormat("fa-IR", {
  calendar: "persian",
  dateStyle: "short",
  timeStyle: "medium",
});

const tailOptions: { label: string; value: number }[] = [
  { label: "۵۰", value: 50 },
  { label: "۱۰۰", value: 100 },
  { label: "۵۰۰", value: 500 },
  { label: "همه", value: 0 },
];

const levelMeta: Record<string, { cls: string; label: string }> = {
  Fatal: { cls: "bg-rose-500/20 text-rose-300", label: "بحرانی" },
  Error: { cls: "bg-rose-500/15 text-rose-400", label: "خطا" },
  Warning: { cls: "bg-amber-500/15 text-amber-400", label: "هشدار" },
  Information: { cls: "bg-sky-500/15 text-sky-300", label: "اطلاع" },
  Debug: { cls: "bg-white/10 text-white/55", label: "دیباگ" },
  Verbose: { cls: "bg-white/10 text-white/45", label: "ورباز" },
};

function formatBytes(bytes: number): string {
  if (bytes < 1024) return `${toFa(bytes)} بایت`;
  if (bytes < 1024 * 1024) return `${toFa((bytes / 1024).toFixed(1))} کیلوبایت`;
  return `${toFa((bytes / (1024 * 1024)).toFixed(1))} مگابایت`;
}

function formatTime(iso: string): string {
  if (!iso) return "";
  const d = new Date(iso);
  return Number.isNaN(d.getTime()) ? iso : fmtDateTime.format(d);
}

function saveBlob(blob: Blob, filename: string) {
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  a.remove();
  URL.revokeObjectURL(url);
}

export default function SystemLogsPage() {
  const [files, setFiles] = useState<LogFile[]>([]);
  const [selected, setSelected] = useState<string>("");
  const [tail, setTail] = useState(100);
  const [searchInput, setSearchInput] = useState("");
  const [search, setSearch] = useState("");

  const [view, setView] = useState<LogView | null>(null);
  const [loadingFiles, setLoadingFiles] = useState(true);
  const [loadingView, setLoadingView] = useState(false);
  const [error, setError] = useState("");
  const [downloading, setDownloading] = useState<string | null>(null);

  const loadFiles = useCallback(async () => {
    setLoadingFiles(true);
    try {
      const list = await api.logs.list();
      setFiles(list);
      setError("");
      setSelected((cur) => (cur && list.some((f) => f.name === cur) ? cur : list[0]?.name ?? ""));
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "خطا در بارگذاری فهرست لاگ‌ها");
    } finally {
      setLoadingFiles(false);
    }
  }, []);

  useEffect(() => {
    loadFiles();
  }, [loadFiles]);

  // Debounce the search box so typing doesn't fire a request per keystroke.
  useEffect(() => {
    const id = setTimeout(() => setSearch(searchInput.trim()), 400);
    return () => clearTimeout(id);
  }, [searchInput]);

  // Re-fetch the viewer whenever the file, tail size, or search term changes.
  useEffect(() => {
    if (!selected) {
      setView(null);
      return;
    }
    let active = true;
    setLoadingView(true);
    api.logs
      .view({ name: selected, tail, search: search || undefined })
      .then((res) => {
        if (!active) return;
        setView(res);
        setError("");
      })
      .catch((e: unknown) => {
        if (active) setError(e instanceof Error ? e.message : "خطا در نمایش لاگ");
      })
      .finally(() => {
        if (active) setLoadingView(false);
      });
    return () => {
      active = false;
    };
  }, [selected, tail, search]);

  const reloadView = useCallback(() => {
    if (!selected) return;
    setLoadingView(true);
    api.logs
      .view({ name: selected, tail, search: search || undefined })
      .then((res) => {
        setView(res);
        setError("");
      })
      .catch((e: unknown) => setError(e instanceof Error ? e.message : "خطا در نمایش لاگ"))
      .finally(() => setLoadingView(false));
  }, [selected, tail, search]);

  const download = useCallback(async (kind: "all" | string) => {
    setDownloading(kind);
    setError("");
    try {
      const { blob, filename } = kind === "all" ? await api.logs.downloadAll() : await api.logs.download(kind);
      saveBlob(blob, filename);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "خطا در دانلود");
    } finally {
      setDownloading(null);
    }
  }, []);

  const selectedMeta = useMemo(() => files.find((f) => f.name === selected), [files, selected]);

  return (
    <div>
      <PageHeader
        title="لاگ‌های فایل سیستم"
        desc="مشاهده، جستجو و دانلود فایل‌های لاگ سرور (Serilog)"
        action={
          <div className="flex flex-wrap items-center gap-2">
            <button
              onClick={() => download("all")}
              disabled={downloading !== null || files.length === 0}
              className="inline-flex items-center gap-2 rounded-xl bg-gradient-to-l from-[#e60053] to-[#9c0038] px-4 py-2 text-sm font-bold text-white transition hover:brightness-110 disabled:opacity-40"
            >
              {downloading === "all" ? <Spinner className="h-4 w-4" /> : <AdminIcon name="disk" className="h-4 w-4" />}
              دانلود همه (ZIP)
            </button>
            <button
              onClick={() => {
                loadFiles();
                reloadView();
              }}
              disabled={loadingFiles || loadingView}
              className="inline-flex items-center gap-2 rounded-xl border border-white/10 bg-white/[0.03] px-4 py-2 text-sm font-medium text-white/75 transition hover:text-white disabled:opacity-50"
            >
              <AdminIcon name="activity" className="h-4 w-4" />
              بازخوانی
            </button>
          </div>
        }
      />

      <Card className="mb-5 p-4">
        <div className="flex flex-col gap-3 lg:flex-row lg:items-center">
          <div className="flex flex-1 items-center gap-2">
            <select
              value={selected}
              onChange={(e) => setSelected(e.target.value)}
              disabled={files.length === 0}
              dir="ltr"
              className="h-11 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-3 text-left font-mono text-xs text-white outline-none transition focus:border-[#e60053]"
            >
              {files.length === 0 ? (
                <option value="">— فایلی موجود نیست —</option>
              ) : (
                files.map((f) => (
                  <option key={f.name} value={f.name} className="bg-[#15151f]">
                    {f.name} · {formatBytes(f.sizeBytes)}
                  </option>
                ))
              )}
            </select>
            <button
              onClick={() => selected && download(selected)}
              disabled={!selected || downloading !== null}
              title="دانلود این فایل"
              className="grid h-11 w-11 shrink-0 place-items-center rounded-xl border border-white/10 bg-white/[0.03] text-white/70 transition hover:text-white disabled:opacity-40"
            >
              {downloading === selected ? <Spinner className="h-4 w-4" /> : <AdminIcon name="disk" className="h-4 w-4" />}
            </button>
          </div>

          <div className="flex shrink-0 items-center rounded-xl border border-white/10 bg-[#0d0d15] p-1">
            {tailOptions.map((o) => (
              <button
                key={o.value}
                onClick={() => setTail(o.value)}
                className={`rounded-lg px-3 py-1.5 text-xs font-bold transition ${
                  tail === o.value ? "bg-[#e60053] text-white" : "text-white/55 hover:text-white"
                }`}
              >
                {o.label}
              </button>
            ))}
          </div>

          <div className="relative flex-1">
            <input
              value={searchInput}
              onChange={(e) => setSearchInput(e.target.value)}
              placeholder="جستجو در محتوای لاگ…"
              className="h-11 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition placeholder:text-white/35 focus:border-[#e60053]"
            />
          </div>
        </div>

        {selectedMeta && (
          <p className="mt-3 text-xs text-white/40">
            {view ? (
              <>
                نمایش <span className="text-white/70">{toFa(view.returned)}</span> از{" "}
                <span className="text-white/70">{toFa(view.totalMatches)}</span> مورد
                {search && <> برای «{search}»</>} · آخرین تغییر {formatTime(selectedMeta.lastModifiedUtc)}
              </>
            ) : (
              <>حجم {formatBytes(selectedMeta.sizeBytes)}</>
            )}
          </p>
        )}
      </Card>

      <Card className="overflow-hidden">
        {loadingFiles && files.length === 0 ? (
          <div className="grid place-items-center py-24">
            <Spinner className="h-8 w-8" />
          </div>
        ) : error && !view ? (
          <p className="px-6 py-16 text-center text-sm text-rose-400">{error}</p>
        ) : !selected ? (
          <p className="px-6 py-16 text-center text-sm text-white/40">هیچ فایل لاگی یافت نشد.</p>
        ) : (
          <div className="relative">
            {loadingView && (
              <div className="absolute inset-x-0 top-0 z-10 grid place-items-center bg-[#0d0d15]/60 py-3">
                <Spinner className="h-6 w-6" />
              </div>
            )}
            {error && <p className="px-6 py-3 text-center text-sm text-rose-400">{error}</p>}
            {view && view.lines.length === 0 ? (
              <p className="px-6 py-16 text-center text-sm text-white/40">
                {search ? "موردی مطابق جستجو یافت نشد." : "این فایل خالی است."}
              </p>
            ) : (
              <div className="max-h-[64vh] divide-y divide-white/5 overflow-y-auto">
                {view?.lines.map((line, i) => {
                  const meta = levelMeta[line.level] ?? levelMeta.Information;
                  return (
                    <div key={i} className="px-4 py-2.5 hover:bg-white/[0.02]">
                      <div className="mb-1 flex items-center gap-2">
                        <span className={`inline-block rounded px-1.5 py-0.5 text-[10px] font-bold ${meta.cls}`}>
                          {meta.label}
                        </span>
                        <span dir="ltr" className="font-mono text-[11px] text-white/40">{formatTime(line.timestamp)}</span>
                      </div>
                      <p dir="ltr" className="whitespace-pre-wrap break-all text-left font-mono text-xs leading-5 text-white/80">
                        {line.message}
                      </p>
                    </div>
                  );
                })}
              </div>
            )}
          </div>
        )}
      </Card>
    </div>
  );
}
