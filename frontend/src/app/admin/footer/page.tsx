"use client";

import { useSiteContent } from "@/components/admin/useSiteContent";
import { Card, PageHeader, Spinner, Field, Toggle, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";
import type { SiteContent } from "@/lib/types";

const socialOptions = ["twitter", "telegram", "instagram"];

type Footer = SiteContent["footer"];

const rowInput =
  "h-10 min-w-0 flex-1 rounded-lg border border-white/10 bg-[#0d0d15] px-3 text-sm text-white outline-none focus:border-[#3a64f2]";
const rowInputLtr = `${rowInput} text-left text-white/70`;
const iconBtn =
  "grid h-10 w-9 shrink-0 place-items-center rounded-lg border border-white/10 text-white/50 transition hover:border-rose-500/50 hover:text-rose-400";
const addBtn =
  "flex items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/80 transition hover:bg-white/5";

export default function AdminFooterPage() {
  const { content, setContent, loading, error, saving, saved, save } = useSiteContent();

  const patch = (fn: (f: Footer) => Footer) =>
    setContent((c) => (c ? { ...c, footer: fn(c.footer) } : c));

  const setText = (key: "aboutTitle" | "aboutText" | "copyright", value: string) =>
    patch((f) => ({ ...f, [key]: value }));

  // ── link columns ──────────────────────────────────────────────────────────
  const setColTitle = (ci: number, value: string) =>
    patch((f) => ({ ...f, columns: f.columns.map((col, i) => (i === ci ? { ...col, title: value } : col)) }));
  const addColumn = () =>
    patch((f) => ({ ...f, columns: [...f.columns, { title: "ستون جدید", links: [] }] }));
  const removeColumn = (ci: number) =>
    patch((f) => ({ ...f, columns: f.columns.filter((_, i) => i !== ci) }));
  const setColLink = (ci: number, li: number, key: "label" | "href", value: string) =>
    patch((f) => ({
      ...f,
      columns: f.columns.map((col, i) =>
        i === ci ? { ...col, links: col.links.map((l, j) => (j === li ? { ...l, [key]: value } : l)) } : col,
      ),
    }));
  const addColLink = (ci: number) =>
    patch((f) => ({
      ...f,
      columns: f.columns.map((col, i) => (i === ci ? { ...col, links: [...col.links, { label: "لینک جدید", href: "#" }] } : col)),
    }));
  const removeColLink = (ci: number, li: number) =>
    patch((f) => ({
      ...f,
      columns: f.columns.map((col, i) => (i === ci ? { ...col, links: col.links.filter((_, j) => j !== li) } : col)),
    }));

  // ── contact ───────────────────────────────────────────────────────────────
  const setContact = (key: "phone" | "email" | "hours" | "address", value: string) =>
    patch((f) => ({ ...f, contact: { ...f.contact, [key]: value } }));

  // ── trust seals ───────────────────────────────────────────────────────────
  const setSeal = (i: number, key: "title" | "subtitle" | "link", value: string) =>
    patch((f) => ({ ...f, trustSeals: f.trustSeals.map((s, idx) => (idx === i ? { ...s, [key]: value } : s)) }));
  const toggleSeal = (i: number, value: boolean) =>
    patch((f) => ({ ...f, trustSeals: f.trustSeals.map((s, idx) => (idx === i ? { ...s, enabled: value } : s)) }));
  const addSeal = () =>
    patch((f) => ({ ...f, trustSeals: [...f.trustSeals, { title: "نماد جدید", subtitle: "", link: "#", enabled: true }] }));
  const removeSeal = (i: number) =>
    patch((f) => ({ ...f, trustSeals: f.trustSeals.filter((_, idx) => idx !== i) }));

  // ── socials ───────────────────────────────────────────────────────────────
  const setSocial = (i: number, key: "label" | "icon" | "href", value: string) =>
    patch((f) => ({ ...f, socials: f.socials.map((s, idx) => (idx === i ? { ...s, [key]: value } : s)) }));
  const addSocial = () =>
    patch((f) => ({ ...f, socials: [...f.socials, { label: "شبکه جدید", icon: "twitter", href: "#" }] }));
  const removeSocial = (i: number) =>
    patch((f) => ({ ...f, socials: f.socials.filter((_, idx) => idx !== i) }));

  return (
    <div>
      <PageHeader
        title="فوتر"
        desc="معرفی، ستون‌های لینک، تماس، نمادهای اعتماد و شبکه‌های اجتماعی"
        action={
          content && (
            <div className="flex items-center gap-3">
              {saved && <span className="text-sm font-medium text-emerald-400">✓ ذخیره شد</span>}
              <button
                onClick={save}
                disabled={saving}
                className="flex h-11 items-center gap-2 rounded-xl bg-gradient-to-l from-[#1733d6] to-[#3a64f2] px-6 text-sm font-bold text-white transition hover:brightness-110"
              >
                {saving ? <Spinner /> : "ذخیره تغییرات"}
              </button>
            </div>
          )
        }
      />

      {loading ? (
        <div className="grid place-items-center py-24">
          <Spinner className="h-8 w-8" />
        </div>
      ) : error || !content ? (
        <Card className="p-8 text-center text-rose-400">{error || "محتوا یافت نشد"}</Card>
      ) : (
        <div className="grid items-start gap-6 lg:grid-cols-2">
          {/* about + copyright */}
          <Card className="min-w-0 p-6">
            <h3 className="mb-5 text-lg font-bold text-white">معرفی و کپی‌رایت</h3>
            <div className="grid gap-4">
              <Field label="متن معرفی (زیر لوگو)">
                <textarea rows={3} value={content.footer.aboutText} onChange={(e) => setText("aboutText", e.target.value)} className={`${inputCls} h-auto py-3`} />
              </Field>
              <Field label="متن کپی‌رایت">
                <input value={content.footer.copyright} onChange={(e) => setText("copyright", e.target.value)} className={inputCls} />
              </Field>
            </div>
          </Card>

          {/* contact */}
          <Card className="min-w-0 p-6">
            <h3 className="mb-5 text-lg font-bold text-white">اطلاعات تماس</h3>
            <p className="mb-4 -mt-3 text-xs text-white/40">هر مورد را خالی بگذارید تا در فوتر نمایش داده نشود.</p>
            <div className="grid gap-4 sm:grid-cols-2">
              <Field label="تلفن پشتیبانی">
                <input value={content.footer.contact?.phone ?? ""} onChange={(e) => setContact("phone", e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
              </Field>
              <Field label="ایمیل">
                <input value={content.footer.contact?.email ?? ""} onChange={(e) => setContact("email", e.target.value)} dir="ltr" className={`${inputCls} text-left`} />
              </Field>
              <Field label="ساعات پاسخ‌گویی">
                <input value={content.footer.contact?.hours ?? ""} onChange={(e) => setContact("hours", e.target.value)} className={inputCls} />
              </Field>
              <Field label="آدرس (اختیاری)">
                <input value={content.footer.contact?.address ?? ""} onChange={(e) => setContact("address", e.target.value)} className={inputCls} />
              </Field>
            </div>
          </Card>

          {/* link columns */}
          <Card className="min-w-0 p-6 lg:col-span-2">
            <div className="mb-4 flex items-center justify-between">
              <h3 className="text-lg font-bold text-white">ستون‌های لینک</h3>
              <button onClick={addColumn} className={addBtn}>
                <AdminIcon name="plus" className="h-3.5 w-3.5" /> افزودن ستون
              </button>
            </div>
            <div className="grid gap-5 md:grid-cols-2">
              {content.footer.columns?.map((col, ci) => (
                <div key={ci} className="rounded-xl border border-white/8 bg-white/[0.02] p-4">
                  <div className="mb-3 flex items-center gap-2">
                    <input value={col.title} onChange={(e) => setColTitle(ci, e.target.value)} className={`${rowInput} font-bold`} placeholder="عنوان ستون" />
                    <button onClick={() => removeColumn(ci)} className={iconBtn} title="حذف ستون">
                      <AdminIcon name="trash" className="h-4 w-4" />
                    </button>
                  </div>
                  <div className="space-y-2">
                    {col.links.map((l, li) => (
                      <div key={li} className="flex items-center gap-2">
                        <input value={l.label} onChange={(e) => setColLink(ci, li, "label", e.target.value)} className={rowInput} placeholder="عنوان" />
                        <input value={l.href} onChange={(e) => setColLink(ci, li, "href", e.target.value)} dir="ltr" className={rowInputLtr} placeholder="/link" />
                        <button onClick={() => removeColLink(ci, li)} className={iconBtn}>
                          <AdminIcon name="trash" className="h-4 w-4" />
                        </button>
                      </div>
                    ))}
                    <button onClick={() => addColLink(ci)} className={`${addBtn} w-full justify-center`}>
                      <AdminIcon name="plus" className="h-3.5 w-3.5" /> افزودن لینک
                    </button>
                  </div>
                </div>
              ))}
            </div>
          </Card>

          {/* trust seals */}
          <Card className="min-w-0 p-6">
            <div className="mb-4 flex items-center justify-between">
              <h3 className="text-lg font-bold text-white">نمادهای اعتماد</h3>
              <button onClick={addSeal} className={addBtn}>
                <AdminIcon name="plus" className="h-3.5 w-3.5" /> افزودن نماد
              </button>
            </div>
            <p className="mb-4 -mt-3 text-xs text-white/40">با خاموش‌کردن کلید، نماد از فوتر حذف می‌شود.</p>
            <div className="space-y-3">
              {content.footer.trustSeals?.map((s, i) => (
                <div key={i} className="rounded-xl border border-white/8 bg-white/[0.02] p-3">
                  <div className="mb-2 flex items-center justify-between gap-3">
                    <label className="flex items-center gap-2 text-sm text-white/80">
                      <Toggle checked={s.enabled} onChange={(v) => toggleSeal(i, v)} />
                      {s.enabled ? "نمایش در فوتر" : "خاموش"}
                    </label>
                    <button onClick={() => removeSeal(i)} className={iconBtn}>
                      <AdminIcon name="trash" className="h-4 w-4" />
                    </button>
                  </div>
                  <div className="grid gap-2 sm:grid-cols-3">
                    <input value={s.title} onChange={(e) => setSeal(i, "title", e.target.value)} className={rowInput} placeholder="عنوان (نماد اعتماد)" />
                    <input value={s.subtitle} onChange={(e) => setSeal(i, "subtitle", e.target.value)} className={rowInput} placeholder="زیرعنوان (eNamad)" />
                    <input value={s.link} onChange={(e) => setSeal(i, "link", e.target.value)} dir="ltr" className={rowInputLtr} placeholder="https://" />
                  </div>
                </div>
              ))}
            </div>
          </Card>

          {/* socials */}
          <Card className="min-w-0 p-6">
            <div className="mb-4 flex items-center justify-between">
              <h3 className="text-lg font-bold text-white">شبکه‌های اجتماعی</h3>
              <button onClick={addSocial} className={addBtn}>
                <AdminIcon name="plus" className="h-3.5 w-3.5" /> افزودن
              </button>
            </div>
            <div className="space-y-2">
              {content.footer.socials.map((s, i) => (
                <div key={i} className="flex items-center gap-2">
                  <select value={s.icon} onChange={(e) => setSocial(i, "icon", e.target.value)} className="h-10 w-28 shrink-0 rounded-lg border border-white/10 bg-[#0d0d15] px-2 text-sm text-white outline-none focus:border-[#3a64f2]">
                    {socialOptions.map((o) => (
                      <option key={o} value={o} className="bg-[#15151f]">{o}</option>
                    ))}
                  </select>
                  <input value={s.label} onChange={(e) => setSocial(i, "label", e.target.value)} className={rowInput} placeholder="عنوان" />
                  <input value={s.href} onChange={(e) => setSocial(i, "href", e.target.value)} dir="ltr" className={rowInputLtr} placeholder="#" />
                  <button onClick={() => removeSocial(i)} className={iconBtn}>
                    <AdminIcon name="trash" className="h-4 w-4" />
                  </button>
                </div>
              ))}
            </div>
          </Card>
        </div>
      )}
    </div>
  );
}
