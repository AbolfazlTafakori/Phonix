export type Category = {
  title: string;
  icon: string;
  href: string;
  iconClass?: string;
};

export type Product = {
  name: string;
  image: string;
  logo: string | null;
  href: string;
};

export type BlogPost = {
  tag: string;
  title: string;
  date: string;
  image: string;
};

export const categories: Category[] = [
  { title: "کارت های اعتباری", icon: "/figma/e67d98d153b9caf9a7453da98a1c85ae776bd4bb.webp", href: "/products", iconClass: "translate-y-3 translate-x-4" },
  { title: "گرافیک طراحی و تدوین", icon: "/figma/cat-graphic.webp", href: "/products" },
  { title: "فیلم سریال استریم ویدئویی", icon: "/figma/cat-film.webp", href: "/products" },
  { title: "موسیقی", icon: "/figma/cat-music.webp", href: "/products", iconClass: "scale-125 translate-y-4" },
  { title: "محصولات بیشتر", icon: "/figma/cat-more.webp", href: "/products" },
  { title: "شبکه های اجتماعی و ارتباطات", icon: "/figma/cat-social.webp", href: "/products" },
  { title: "بازی و سرگرمی", icon: "/figma/cat-games.webp", href: "/products" },
  { title: "صرافی ارز دیجیتال", icon: "/figma/cat-exchange.webp", href: "/products" },
];

export const products: Product[] = [
  { name: "Wise", image: "/figma/prod-wise.webp", logo: "/figma/logo-wise.webp", href: "#" },
  { name: "Freelancer", image: "/figma/prod-freelancer.webp", logo: "/figma/logo-freelancer.webp", href: "#" },
  { name: "Binance", image: "/figma/prod-binance.webp", logo: "/figma/logo-binance.webp", href: "#" },
  { name: "Spotify", image: "/figma/prod-spotify.webp", logo: null, href: "#" },
  { name: "Bybit", image: "/figma/prod-bybit.webp", logo: "/figma/logo-bybit.webp", href: "#" },
  { name: "Apple Music", image: "/figma/prod-applemusic.webp", logo: "/figma/logo-applemusic.webp", href: "#" },
  { name: "Canva", image: "/figma/prod-canva.webp", logo: "/figma/logo-canva.webp", href: "#" },
  { name: "Netflix", image: "/figma/prod-netflix.webp", logo: "/figma/logo-netflix.webp", href: "#" },
];

export const blogPosts: BlogPost[] = [
  {
    tag: "Sercurity | 10 min read",
    title: "Lorem ipsum dolor sit amet consectetur. Pretium amet facilisis.",
    date: "August 4. 2023",
    image: "/figma/blog-1.webp",
  },
  {
    tag: "Sercurity | 10 min read",
    title: "Lorem ipsum dolor sit amet consectetur. Pretium amet facilisis.",
    date: "August 4. 2023",
    image: "/figma/blog-2.webp",
  },
  {
    tag: "Sercurity | 10 min read",
    title: "Lorem ipsum dolor sit amet consectetur. Pretium amet facilisis.",
    date: "August 4. 2023",
    image: "/figma/blog-3.webp",
  },
];

export const footerLinks = [
  { label: "فروشگاه", href: "/products" },
  { label: "سبد خرید", href: "#" },
  { label: "تماس با ما", href: "#" },
  { label: "قوانین و مقررات", href: "#" },
  { label: "حساب کاربری من", href: "/account" },
];

export const navLinks = [
  { label: "خانه", href: "/", hasMenu: false },
  { label: "همه محصولات", href: "/products", hasMenu: false },
];
