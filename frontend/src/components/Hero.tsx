import { getHeroSlides } from "@/lib/content";
import HeroCarousel from "./HeroCarousel";

export default async function Hero() {
  const slides = await getHeroSlides();
  return <HeroCarousel slides={slides} />;
}
