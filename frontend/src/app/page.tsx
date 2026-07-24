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
import HomeNewsletter from "@/components/home/HomeNewsletter";
import HomeFooter from "@/components/home/HomeFooter";
import MobileTabBar from "@/components/home/MobileTabBar";
import Reveal from "@/components/Reveal";

// Home content (hero, showcase, blog picks) is admin-editable, so render per request instead of
// baking the build-time snapshot into a static page.
export const dynamic = "force-dynamic";

export default async function Home() {
  const [content, blogPosts] = await Promise.all([getSiteContent(), getBlogPosts()]);
  return (
    <div className="home-light min-h-screen pb-[60px] lg:pb-0">
      <TopBar />
      <HomeHeader brand={content.brand} searchPlaceholder="جستجو در بین هزاران محصول..." />
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
      <HomeFooter brand={content.brand} />
      <MobileTabBar />
    </div>
  );
}
