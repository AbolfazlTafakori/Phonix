import { NextResponse } from "next/server";
import { writeFile, mkdir } from "fs/promises";
import { existsSync } from "fs";
import path from "path";

export const runtime = "nodejs";

const allowed = ["image/png", "image/jpeg", "image/webp", "image/gif", "image/svg+xml"];
const maxBytes = 4 * 1024 * 1024;

// resolve the Next project's public dir regardless of the process cwd
function publicDir() {
  const cwd = process.cwd();
  const hasConfig = (dir: string) =>
    ["next.config.ts", "next.config.js", "next.config.mjs"].some((f) => existsSync(path.join(dir, f)));
  if (hasConfig(cwd)) return path.join(cwd, "public");
  const nested = path.join(cwd, "frontend");
  if (hasConfig(nested)) return path.join(nested, "public");
  return path.join(cwd, "public");
}

export async function POST(req: Request) {
  const form = await req.formData();
  const file = form.get("file");

  if (!(file instanceof File)) {
    return NextResponse.json({ error: "فایلی ارسال نشد" }, { status: 400 });
  }
  if (!allowed.includes(file.type)) {
    return NextResponse.json({ error: "فرمت تصویر مجاز نیست (PNG, JPG, WebP, GIF, SVG)" }, { status: 400 });
  }
  if (file.size > maxBytes) {
    return NextResponse.json({ error: "حجم تصویر باید کمتر از ۴ مگابایت باشد" }, { status: 400 });
  }

  const bytes = Buffer.from(await file.arrayBuffer());
  const ext = (file.name.split(".").pop() ?? "png").toLowerCase().replace(/[^a-z0-9]/g, "") || "png";
  const name = `${Date.now()}-${Math.random().toString(36).slice(2, 8)}.${ext}`;
  const dir = path.join(publicDir(), "uploads");

  await mkdir(dir, { recursive: true });
  await writeFile(path.join(dir, name), bytes);

  return NextResponse.json({ url: `/uploads/${name}` });
}
