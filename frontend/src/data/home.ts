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
  { title: "کارت های اعتباری", icon: "/figma/e67d98d153b9caf9a7453da98a1c85ae776bd4bb.png", href: "/films", iconClass: "translate-y-3 translate-x-4" },
  { title: "گرافیک طراحی و تدوین", icon: "/figma/cat-graphic.png", href: "/films" },
  { title: "فیلم سریال استریم ویدئویی", icon: "/figma/cat-film.png", href: "/films" },
  { title: "موسیقی", icon: "/figma/cat-music.png", href: "/films", iconClass: "scale-125 translate-y-4" },
  { title: "محصولات بیشتر", icon: "/figma/cat-more.png", href: "/films" },
  { title: "شبکه های اجتماعی و ارتباطات", icon: "/figma/cat-social.png", href: "/films" },
  { title: "بازی و سرگرمی", icon: "/figma/cat-games.png", href: "/films" },
  { title: "صرافی ارز دیجیتال", icon: "/figma/cat-exchange.png", href: "/films" },
];

export const products: Product[] = [
  { name: "Wise", image: "/figma/prod-wise.png", logo: "/figma/logo-wise.png", href: "#" },
  { name: "Freelancer", image: "/figma/prod-freelancer.png", logo: "/figma/logo-freelancer.png", href: "#" },
  { name: "Binance", image: "/figma/prod-binance.png", logo: "/figma/logo-binance.png", href: "#" },
  { name: "Spotify", image: "/figma/prod-spotify.png", logo: null, href: "#" },
  { name: "Bybit", image: "/figma/prod-bybit.png", logo: "/figma/logo-bybit.png", href: "#" },
  { name: "Apple Music", image: "/figma/prod-applemusic.png", logo: "/figma/logo-applemusic.png", href: "#" },
  { name: "Canva", image: "/figma/prod-canva.png", logo: "/figma/logo-canva.png", href: "#" },
  { name: "Netflix", image: "/figma/prod-netflix.png", logo: "/figma/logo-netflix.png", href: "#" },
];

export const blogPosts: BlogPost[] = [
  {
    tag: "Sercurity | 10 min read",
    title: "Lorem ipsum dolor sit amet consectetur. Pretium amet facilisis.",
    date: "August 4. 2023",
    image: "/figma/blog-1.png",
  },
  {
    tag: "Sercurity | 10 min read",
    title: "Lorem ipsum dolor sit amet consectetur. Pretium amet facilisis.",
    date: "August 4. 2023",
    image: "/figma/blog-2.png",
  },
  {
    tag: "Sercurity | 10 min read",
    title: "Lorem ipsum dolor sit amet consectetur. Pretium amet facilisis.",
    date: "August 4. 2023",
    image: "/figma/blog-3.png",
  },
];

export const footerLinks = [
  { label: "فروشگاه", href: "/films" },
  { label: "سبد خرید", href: "#" },
  { label: "تماس با ما", href: "#" },
  { label: "قوانین و مقررات", href: "#" },
  { label: "حساب کاربری من", href: "/account" },
];

export const navLinks = [
  { label: "خانه", href: "/", hasMenu: false },
  { label: "محصولات", href: "/films", hasMenu: true },
];
