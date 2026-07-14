"use client";

import { useCallback, useEffect, useState } from "react";
import { api } from "@/lib/api";
import Img from "@/components/ui/Img";

// Manages one image-CAPTCHA challenge: fetches it, tracks the user's answer, and can refresh to a new image
// (call this after a failed submit so a consumed/expired challenge is replaced).
export function useCaptcha() {
  const [id, setId] = useState("");
  const [image, setImage] = useState("");
  const [text, setText] = useState("");
  const [loading, setLoading] = useState(true);

  const refresh = useCallback(async () => {
    setLoading(true);
    setText("");
    try {
      const c = await api.captcha.get();
      setId(c.id);
      setImage(c.image);
    } catch {
      setImage("");
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    refresh();
  }, [refresh]);

  return { id, image, text, setText, refresh, loading };
}

export type CaptchaState = ReturnType<typeof useCaptcha>;

const inputCls =
  "h-12 w-full rounded-xl border border-white/10 bg-[#0d0d15] px-4 text-sm text-white outline-none transition placeholder:text-white/35 focus:border-[#3e3af2] focus:ring-2 focus:ring-[#3e3af2]/20";

export function CaptchaField({ captcha }: { captcha: CaptchaState }) {
  return (
    <div className="mb-5">
      <label className="mb-2 block text-sm font-medium text-white/85">کد امنیتی تصویر</label>
      <div className="flex items-center gap-3">
        <div className="grid h-12 w-[150px] shrink-0 place-items-center overflow-hidden rounded-xl border border-white/10 bg-[#e9e9f2]">
          {captcha.loading || !captcha.image ? (
            <span className="text-xs text-black/40">…</span>
          ) : (
            // eslint-disable-next-line @next/next/no-img-element
            <Img src={captcha.image} alt="کد امنیتی" className="h-full w-full object-cover" sizes="(max-width: 640px) 100vw, (max-width: 1024px) 50vw, 33vw" />
          )}
        </div>
        <button
          type="button"
          onClick={captcha.refresh}
          title="تصویر جدید"
          className="grid h-12 w-12 shrink-0 place-items-center rounded-xl border border-white/10 text-white/60 transition hover:text-white"
        >
          ↻
        </button>
        <input
          value={captcha.text}
          onChange={(e) => captcha.setText(e.target.value)}
          dir="ltr"
          autoComplete="off"
          placeholder="کد داخل تصویر"
          className={`${inputCls} text-center tracking-[0.3em]`}
        />
      </div>
      <p className="mt-1.5 text-xs text-white/45">حروف و اعداد داخل تصویر را دقیقاً وارد کنید — بزرگ و کوچک بودن حروف مهم است.</p>
    </div>
  );
}
