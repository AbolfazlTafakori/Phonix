import Background from "@/components/Background";
import Navbar from "@/components/Navbar";
import Hero from "@/components/Hero";
import Stats from "@/components/Stats";
import Categories from "@/components/Categories";
import BestSellers from "@/components/BestSellers";
import Blog from "@/components/Blog";
import Footer from "@/components/Footer";

export default function Home() {
  return (
    <div className="relative min-h-screen text-white">
      <Background />
      <Navbar />
      <main>
        <Hero />
        <Stats />
        <Categories />
        <BestSellers />
        <Blog />
        <Footer />
      </main>
    </div>
  );
}
