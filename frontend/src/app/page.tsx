import { getSiteContent } from "@/lib/content";
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

export default async function Home() {
  const content = await getSiteContent();
  return (
    <div className="home-light min-h-screen">
      <TopBar />
      <HomeHeader brand={content.brand} searchPlaceholder="جستجو در بین هزاران محصول..." />
      <main>
        <HomeHero />
        <TrustStats />
        <HomeCategories />
        <HomeBestSellers />
        <HomePromoBanners />
        <HomeWhyChoose />
        <HomeHowToBuy />
        <HomeReviews />
        <HomeBlog />
        <HomeFaq />
        <HomeNewsletter />
      </main>
      <HomeFooter brand={content.brand} />
    </div>
  );
}
