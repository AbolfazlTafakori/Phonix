import type { Metadata } from "next";
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

export const metadata: Metadata = {
  title: "فونیکس ورفای | Phoenix Verify",
  description:
    "بزرگ‌ترین مرجع ارائه حساب‌های وریفای‌شده پلتفرم‌های محبوب. خرید امن، پشتیبانی آنلاین و بهترین تجربه خرید دیجیتال.",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
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
      <body suppressHydrationWarning>{children}</body>
    </html>
  );
}
