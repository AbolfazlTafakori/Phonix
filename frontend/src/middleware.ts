import { NextRequest, NextResponse } from "next/server";

const BASE = process.env.NEXT_PUBLIC_API_URL ?? "http://localhost:5228";

let cache = { on: false, at: 0 };

async function maintenanceOn(): Promise<boolean> {
  if (Date.now() - cache.at < 15000) return cache.on;
  try {
    const res = await fetch(`${BASE}/api/advanced-settings`, { cache: "no-store" });
    const data = await res.json();
    cache = { on: Boolean(data.maintenanceMode), at: Date.now() };
  } catch {
    cache = { on: false, at: Date.now() };
  }
  return cache.on;
}

export async function middleware(req: NextRequest) {
  const { pathname } = req.nextUrl;

  // admin, api, the maintenance page itself, and static files always pass through
  if (
    pathname.startsWith("/admin") ||
    pathname.startsWith("/api") ||
    pathname.startsWith("/maintenance") ||
    pathname.includes(".")
  ) {
    return NextResponse.next();
  }

  if (await maintenanceOn()) {
    const url = req.nextUrl.clone();
    url.pathname = "/maintenance";
    return NextResponse.rewrite(url);
  }

  return NextResponse.next();
}

export const config = {
  matcher: ["/((?!_next).*)"],
};
