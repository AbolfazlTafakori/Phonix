import type { Metadata } from "next";
import Script from "next/script";
import { getAdvancedSettings } from "@/lib/content";
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

export async function generateMetadata(): Promise<Metadata> {
  const s = await getAdvancedSettings();
  return {
    title: s.metaTitle || "فونیکس ورفای | Phoenix Verify",
    description:
      s.metaDescription ||
      "بزرگ‌ترین مرجع ارائه حساب‌های وریفای‌شده پلتفرم‌های محبوب. خرید امن، پشتیبانی آنلاین و بهترین تجربه خرید دیجیتال.",
    keywords: s.metaKeywords || undefined,
  };
}

export default async function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  const s = await getAdvancedSettings();
  // GA/GTM ids are alphanumeric + dash; strip anything else so the value can never
  // break out of the inline script string below.
  const analyticsId = s.analyticsId.replace(/[^A-Za-z0-9-]/g, "");
  return (
    <html
      lang="fa"
      dir="rtl"
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
        {children}

        {analyticsId && (
          <>
            <Script src={`https://www.googletagmanager.com/gtag/js?id=${analyticsId}`} strategy="afterInteractive" />
            <Script
              id="ga-init"
              strategy="afterInteractive"
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
            dangerouslySetInnerHTML={{ __html: s.customHeadScript.replace(/<\/?script[^>]*>/gi, "") }}
          />
        )}
      </body>
    </html>
  );
}
