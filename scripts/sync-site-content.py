"""Pull the live shop's product and blog copy back into seo-content/.

Content is authored in the admin panel, so the live database — not this repo — is the source of
truth. This writes it out in the same formats the admin's .md importers read, giving a reviewable,
version-controlled copy of exactly what is published.

    python scripts/sync-site-content.py [--url https://phoenixverify.com] [--out seo-content]
"""
import argparse
import json
import pathlib
import re
import urllib.request

FA_DIGITS = str.maketrans("0123456789", "۰۱۲۳۴۵۶۷۸۹")

# Stable file stems, so a re-run overwrites the same file instead of leaving a second copy behind.
# Keyed by product id and verified against the live catalogue — an unknown id falls back to a slug
# of its name rather than silently landing in the wrong file.
PRODUCT_STEMS = {
    1: "00-netflix", 3: "15-canva-pro", 5: "05-apple-music", 9: "20-twitch-prime",
    10: "02-spotify", 11: "03-youtube-premium", 12: "11-duolingo", 13: "16-faceapp-pro",
    14: "12-grammarly", 15: "04-telegram-premium", 16: "13-elsa-speak", 17: "21-discord-nitro",
    18: "07-claude-ai", 19: "09-grok", 20: "01-chatgpt", 21: "08-gemini", 22: "14-mondly",
    23: "17-capcut-pro", 24: "22-chess-com", 25: "18-picsart-gold", 26: "23-calm",
    27: "19-captions-pro", 28: "10-kling-ai", 29: "24-expressvpn", 30: "25-nordvpn",
    31: "26-vyprvpn", 32: "27-proton", 33: "28-surfshark", 34: "29-cyberghost",
    35: "30-speedify", 36: "31-windscribe", 37: "32-hotspotshield", 38: "33-v2ray",
}


def get(url: str):
    with urllib.request.urlopen(url) as r:
        return json.load(r)


def slugify(name: str) -> str:
    return re.sub(r"[^\w؀-ۿ]+", "-", name).strip("-")[:40]


def write_products(base: str, out_dir: pathlib.Path) -> None:
    products = get(f"{base}/api/products")
    for p in sorted(products, key=lambda x: x["id"]):
        desc = (p.get("description") or "").strip()
        faq = p.get("faq") or []
        if not desc and not faq:
            continue
        stem = PRODUCT_STEMS.get(p["id"]) or f"{p['id']:02d}-{slugify(p['name'])}"
        parts = [f"# محتوای صفحه محصول: {p['name']}", "", "## بخش «توضیحات محصول»", "", desc]
        if faq:
            parts += ["", "---", "", "## بخش «سوالات متداول (FAQ)»", ""]
            for i, f in enumerate(faq, 1):
                parts += [f"**{str(i).translate(FA_DIGITS)}) {f['question']}**", f["answer"], ""]
        (out_dir / f"{stem}.md").write_text("\n".join(parts).rstrip() + "\n", encoding="utf-8")
        print(f"  product {p['id']:>3} words={len(desc.split()):>5} faq={len(faq):>3}  {stem}.md")


def write_blog(base: str, out_dir: pathlib.Path) -> None:
    posts = get(f"{base}/api/blog")
    out_dir.mkdir(parents=True, exist_ok=True)
    for i, b in enumerate(sorted(posts, key=lambda x: x["id"]), 1):
        header = [
            f"# {b['title']}",
            "",
            f"نامک: {b['slug']}",
            f"برچسب: {b['tag']}",
            f"تاریخ: {b['date']}",
            f"خلاصه: {b['excerpt']}",
            f"تصویر: {b['image']}",
            "",
            b["content"].strip(),
        ]
        stem = f"{i:02d}-{b['slug']}"
        (out_dir / f"{stem}.md").write_text("\n".join(header).rstrip() + "\n", encoding="utf-8")
        print(f"  blog {b['slug'][:34]:<34} words={len(b['content'].split()):>5}  {stem}.md")


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", default="https://phoenixverify.com")
    ap.add_argument("--out", default="seo-content")
    args = ap.parse_args()

    base = args.url.rstrip("/")
    out_dir = pathlib.Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)

    print("products:")
    write_products(base, out_dir)
    print("blog:")
    write_blog(base, out_dir / "blog")


if __name__ == "__main__":
    main()
