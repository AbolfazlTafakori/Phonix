"use client";

import { useSiteContent } from "@/components/admin/useSiteContent";
import { Card, PageHeader, Spinner, Field, inputCls } from "@/components/admin/ui";
import AdminIcon from "@/components/admin/AdminIcon";

const socialOptions = ["twitter", "telegram", "instagram"];

export default function AdminFooterPage() {
  const { content, setContent, loading, error, saving, saved, save } = useSiteContent();

  const setFooter = (key: "aboutTitle" | "aboutText" | "linksTitle" | "copyright", value: string) =>
    setContent((c) => (c ? { ...c, footer: { ...c.footer, [key]: value } } : c));

  const setLink = (i: number, key: "label" | "href", value: string) =>
    setContent((c) => (c ? { ...c, footer: { ...c.footer, links: c.footer.links.map((l, idx) => (idx === i ? { ...l, [key]: value } : l)) } } : c));
  const addLink = () =>
    setContent((c) => (c ? { ...c, footer: { ...c.footer, links: [...c.footer.links, { label: "لینک جدید", href: "#" }] } } : c));
  const removeLink = (i: number) =>
    setContent((c) => (c ? { ...c, footer: { ...c.footer, links: c.footer.links.filter((_, idx) => idx !== i) } } : c));

  const setSocial = (i: number, key: "label" | "icon" | "href", value: string) =>
    setContent((c) => (c ? { ...c, footer: { ...c.footer, socials: c.footer.socials.map((s, idx) => (idx === i ? { ...s, [key]: value } : s)) } } : c));
  const addSocial = () =>
    setContent((c) => (c ? { ...c, footer: { ...c.footer, socials: [...c.footer.socials, { label: "شبکه جدید", icon: "twitter", href: "#" }] } } : c));
  const removeSocial = (i: number) =>
    setContent((c) => (c ? { ...c, footer: { ...c.footer, socials: c.footer.socials.filter((_, idx) => idx !== i) } } : c));

  return (
    <div>
      <PageHeader
        title="فوتر"
        desc="متن معرفی، لینک‌ها، شبکه‌های اجتماعی و کپی‌رایت"
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
        <div className="grid gap-6 lg:grid-cols-2">
          <Card className="min-w-0 p-6">
            <h3 className="mb-5 text-lg font-bold text-white">درباره</h3>
            <div className="grid gap-4">
              <Field label="عنوان بخش معرفی">
                <input value={content.footer.aboutTitle} onChange={(e) => setFooter("aboutTitle", e.target.value)} className={inputCls} />
              </Field>
              <Field label="متن معرفی">
                <textarea rows={5} value={content.footer.aboutText} onChange={(e) => setFooter("aboutText", e.target.value)} className={`${inputCls} h-auto py-3`} />
              </Field>
              <Field label="متن کپی‌رایت">
                <input value={content.footer.copyright} onChange={(e) => setFooter("copyright", e.target.value)} className={inputCls} />
              </Field>
            </div>
          </Card>

          <div className="min-w-0 space-y-6">
            <Card className="p-6">
              <div className="mb-4 flex items-center justify-between">
                <h3 className="text-lg font-bold text-white">لینک‌های مهم</h3>
                <button onClick={addLink} className="flex items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/80 transition hover:bg-white/5">
                  <AdminIcon name="plus" className="h-3.5 w-3.5" /> افزودن
                </button>
              </div>
              <Field label="عنوان ستون لینک‌ها" className="mb-4">
                <input value={content.footer.linksTitle} onChange={(e) => setFooter("linksTitle", e.target.value)} className={inputCls} />
              </Field>
              <div className="space-y-2">
                {content.footer.links.map((l, i) => (
                  <div key={i} className="flex items-center gap-2">
                    <input value={l.label} onChange={(e) => setLink(i, "label", e.target.value)} className="h-10 min-w-0 flex-1 rounded-lg border border-white/10 bg-[#0d0d15] px-3 text-sm text-white outline-none focus:border-[#3a64f2]" placeholder="عنوان" />
                    <input value={l.href} onChange={(e) => setLink(i, "href", e.target.value)} dir="ltr" className="h-10 min-w-0 flex-1 rounded-lg border border-white/10 bg-[#0d0d15] px-3 text-left text-sm text-white/70 outline-none focus:border-[#3a64f2]" placeholder="/link" />
                    <button onClick={() => removeLink(i)} className="grid h-10 w-9 shrink-0 place-items-center rounded-lg border border-white/10 text-white/50 transition hover:border-rose-500/50 hover:text-rose-400">
                      <AdminIcon name="trash" className="h-4 w-4" />
                    </button>
                  </div>
                ))}
              </div>
            </Card>

            <Card className="p-6">
              <div className="mb-4 flex items-center justify-between">
                <h3 className="text-lg font-bold text-white">شبکه‌های اجتماعی</h3>
                <button onClick={addSocial} className="flex items-center gap-1.5 rounded-lg border border-white/10 px-3 py-1.5 text-xs font-bold text-white/80 transition hover:bg-white/5">
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
                    <input value={s.label} onChange={(e) => setSocial(i, "label", e.target.value)} className="h-10 min-w-0 flex-1 rounded-lg border border-white/10 bg-[#0d0d15] px-3 text-sm text-white outline-none focus:border-[#3a64f2]" placeholder="عنوان" />
                    <input value={s.href} onChange={(e) => setSocial(i, "href", e.target.value)} dir="ltr" className="h-10 min-w-0 flex-1 rounded-lg border border-white/10 bg-[#0d0d15] px-3 text-left text-sm text-white/70 outline-none focus:border-[#3a64f2]" placeholder="#" />
                    <button onClick={() => removeSocial(i)} className="grid h-10 w-9 shrink-0 place-items-center rounded-lg border border-white/10 text-white/50 transition hover:border-rose-500/50 hover:text-rose-400">
                      <AdminIcon name="trash" className="h-4 w-4" />
                    </button>
                  </div>
                ))}
              </div>
            </Card>
          </div>
        </div>
      )}
    </div>
  );
}
