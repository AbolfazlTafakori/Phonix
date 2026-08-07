import { getBlogPosts, getSiteContent } from "@/lib/content";
import TopBar from "@/components/home/TopBar";
import HomeHeader from "@/components/home/HomeHeader";
import HomeHero from "@/components/home/HomeHero";
import TrustStats from "@/components/home/TrustStats";
import HomeCategories from "@/components/home/HomeCategories";
import HomeBestSellers from "@/components/home/HomeBestSellers";
import HomePromoBanners from "@/components/home/HomePromoBanners";
import HomeWhyChoose from "@/components/home/HomeWhyChoose";
import HomeHowToBuy from "@/components/home/HomeHowToBuy";
import HomeReviews from "@/components/home/HomeReviews";
import HomeBlog from "@/components/home/HomeBlog";
import HomeFaq from "@/components/home/HomeFaq";
import { faqItems } from "@/components/home/homeFaqItems";
import HomeNewsletter from "@/components/home/HomeNewsletter";
import HomeFooter from "@/components/home/HomeFooter";
import FooterVisibility from "@/components/home/FooterVisibility";
import MobileTabBar from "@/components/home/MobileTabBar";
import Reveal from "@/components/Reveal";
import { jsonLdScript } from "@/lib/seo";

// Home content (hero, showcase, blog picks) is admin-editable, so it can't be baked in at build
// time — but rendering per request made every visitor and crawler wait on the API. Cached and
// refreshed in the background instead, matching the rest of the storefront.
export const revalidate = 60;

export default async function Home() {
  const [content, blogPosts] = await Promise.all([getSiteContent(), getBlogPosts()]);
  const faqLd = {
    "@context": "https://schema.org",
    "@type": "FAQPage",
    mainEntity: faqItems.map((f) => ({
      "@type": "Question",
      name: f.q,
      acceptedAnswer: { "@type": "Answer", text: f.a },
    })),
  };
  return (
    <div className="home-light min-h-screen pb-[60px] lg:pb-0">
      <script type="application/ld+json" dangerouslySetInnerHTML={{ __html: jsonLdScript(faqLd) }} />
      <TopBar />
      <HomeHeader brand={content.brand} searchPlaceholder="جستجو در فونیکس" />
      <main>
        <HomeHero />
        <TrustStats />
        <Reveal><HomeCategories /></Reveal>
        <Reveal><HomeBestSellers /></Reveal>
        <Reveal><HomePromoBanners /></Reveal>
        <Reveal><HomeWhyChoose /></Reveal>
        <Reveal><HomeHowToBuy /></Reveal>
        <Reveal><HomeReviews /></Reveal>
        <Reveal><HomeBlog posts={blogPosts} title={content.sections.blogTitle} /></Reveal>
        <Reveal><HomeFaq /></Reveal>
        <Reveal><HomeNewsletter /></Reveal>
      </main>
      <FooterVisibility><HomeFooter brand={content.brand} footer={content.footer} /></FooterVisibility>
      <MobileTabBar />
    </div>
  );
}
