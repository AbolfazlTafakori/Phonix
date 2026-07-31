import type { Metadata, Viewport } from "next";
import { headers } from "next/headers";
import Script from "next/script";
import { getAdvancedSettings } from "@/lib/content";
import { SITE_URL, absoluteUrl } from "@/lib/seo";
import LiveChat from "@/components/LiveChat";
import {
  Vazirmatn,
  Bigshot_One,
  Archivo,
  Timmana,
  Unna,
  Walter_Turncoat,
  Almarai,
  Space_Grotesk,
} from "next/font/google";
import "./globals.css";

const vazirmatn = Vazirmatn({
  variable: "--font-vazir",
  subsets: ["arabic", "latin"],
  weight: ["400", "500", "600", "700", "800"],
});

const bigshot = Bigshot_One({
  variable: "--font-bigshot",
  subsets: ["latin"],
  weight: "400",
});

const archivo = Archivo({
  variable: "--font-archivo",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
});

const timmana = Timmana({
  variable: "--font-timmana",
  subsets: ["latin"],
  weight: "400",
});

const unna = Unna({
  variable: "--font-unna",
  subsets: ["latin"],
  weight: ["400", "700"],
});

const turncoat = Walter_Turncoat({
  variable: "--font-turncoat",
  subsets: ["latin"],
  weight: "400",
});

const almarai = Almarai({
  variable: "--font-almarai",
  subsets: ["arabic"],
  weight: ["400", "700", "800"],
});

const display = Space_Grotesk({
  variable: "--font-display",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
});

export const viewport: Viewport = {
  width: "device-width",
  initialScale: 1,
  colorScheme: "light dark",
  // Mobile browser chrome follows the theme; a single value left the address bar white in dark mode.
  themeColor: [
    { media: "(prefers-color-scheme: light)", color: "#f8f1ea" },
    { media: "(prefers-color-scheme: dark)", color: "#0e0e12" },
  ],
};

export async function generateMetadata(): Promise<Metadata> {
  const s = await getAdvancedSettings();
  const title = s.metaTitle || "فونیکس وریفای | Phoenix Verify";
  const description =
    s.metaDescription ||
    "بزرگ‌ترین مرجع ارائه حساب‌های وریفای‌شده پلتفرم‌های محبوب. خرید امن، پشتیبانی آنلاین و بهترین تجربه خرید دیجیتال.";
  return {
    metadataBase: new URL(SITE_URL),
    title: { default: title, template: "%s | Phoenix Verify" },
    description,
    keywords: s.metaKeywords || undefined,
    // "./" resolves to the current path against metadataBase, giving every page
    // a self-referencing canonical unless it overrides alternates itself.
    alternates: { canonical: "./" },
    openGraph: {
      type: "website",
      siteName: "Phoenix Verify",
      locale: "fa_IR",
      title,
      description,
      url: SITE_URL,
      images: [{ url: "/og-image.png", width: 1200, height: 630, alt: "Phoenix Verify" }],
    },
    twitter: {
      card: "summary_large_image",
      title,
      description,
      images: ["/og-image.png"],
    },
  };
}

export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const s = await getAdvancedSettings();
  // GA/GTM ids are alphanumeric + dash; strip anything else so the value can never
  // break out of the inline script string below.
  const analyticsId = s.analyticsId.replace(/[^A-Za-z0-9-]/g, "");
  // Stamped by middleware.ts onto every script below — CSP's script-src only trusts a nonce'd or 'self'-hosted
  // script now (no more 'unsafe-inline'), so every <Script> here needs this to still run.
  const nonce = (await headers()).get("x-nonce") ?? undefined;
  return (
    <html
      lang="fa"
      dir="rtl"
      suppressHydrationWarning
      className={[
        vazirmatn.variable,
        bigshot.variable,
        archivo.variable,
        timmana.variable,
        unna.variable,
        turncoat.variable,
        almarai.variable,
        display.variable,
        "antialiased",
      ].join(" ")}
    >
      <body suppressHydrationWarning>
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{
            __html: JSON.stringify({
              "@context": "https://schema.org",
              "@type": "Organization",
              name: "Phoenix Verify",
              alternateName: "فونیکس وریفای",
              url: SITE_URL,
              logo: absoluteUrl("/figma/logo-phoenix.webp"),
              contactPoint: {
                "@type": "ContactPoint",
                email: "support@phoenixverify.com",
                contactType: "customer support",
                availableLanguage: ["fa"],
              },
            }),
          }}
        />
        <script
          type="application/ld+json"
          dangerouslySetInnerHTML={{
            __html: JSON.stringify({
              "@context": "https://schema.org",
              "@type": "WebSite",
              name: "Phoenix Verify",
              url: SITE_URL,
              inLanguage: "fa",
              potentialAction: {
                "@type": "SearchAction",
                target: { "@type": "EntryPoint", urlTemplate: `${SITE_URL}/products?q={search_term_string}` },
                "query-input": "required name=search_term_string",
              },
            }),
          }}
        />
        {/* Theme boot: 'phonix-theme' is 'system' (default) | 'light' | 'dark'. In system mode the OS/browser
            preference wins and keeps winning — the matchMedia listener re-applies it if the OS flips while the
            page is open. Runs before paint so there is no flash of the wrong theme. */}
        {/* A plain tag rather than next/script: with `beforeInteractive` this ended up only in the RSC
            payload and was injected after hydration, so it never actually ran before paint — the very thing
            it exists to do. Written straight into the HTML it runs while the document is still parsing.
            `suppressHydrationWarning` is required because a browser hides a nonce once it has parsed the tag
            (the attribute reads back empty), so React would otherwise compare its own nonce against "" and
            report a hydration mismatch on every page. */}
        <script
          id="phonix-theme-init"
          nonce={nonce}
          suppressHydrationWarning
          dangerouslySetInnerHTML={{
            __html: `(function(){try{var mq=window.matchMedia&&window.matchMedia('(prefers-color-scheme: dark)');function mode(){try{return localStorage.getItem('phonix-theme')||'system';}catch(e){return 'system';}}function apply(){var m=mode();var dark=m==='dark'||(m==='system'&&!!(mq&&mq.matches));document.documentElement.classList.toggle('home-dark',dark);}apply();window.__phonixApplyTheme=apply;if(mq){var h=function(){if(mode()==='system')apply();};mq.addEventListener?mq.addEventListener('change',h):mq.addListener(h);}}catch(e){}})();`,
          }}
        />
        {children}

        <LiveChat />

        {analyticsId && (
          <>
            <Script src={`https://www.googletagmanager.com/gtag/js?id=${analyticsId}`} strategy="afterInteractive" nonce={nonce} />
            <Script
              id="ga-init"
              strategy="afterInteractive"
              nonce={nonce}
              dangerouslySetInnerHTML={{
                __html: `window.dataLayer=window.dataLayer||[];function gtag(){dataLayer.push(arguments);}gtag('js',new Date());gtag('config','${analyticsId}');`,
              }}
            />
          </>
        )}

        {s.customHeadScript && (
          <Script
            id="custom-script"
            strategy="afterInteractive"
            nonce={nonce}
            dangerouslySetInnerHTML={{ __html: s.customHeadScript.replace(/<\/?script[^>]*>/gi, "") }}
          />
        )}
      </body>
    </html>
  );
}
