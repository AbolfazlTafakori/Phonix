type Props = { name: string; className?: string };

const paths: Record<string, React.ReactNode> = {
  dashboard: (
    <>
      <rect x="3" y="3" width="7" height="9" rx="1.5" />
      <rect x="14" y="3" width="7" height="5" rx="1.5" />
      <rect x="14" y="12" width="7" height="9" rx="1.5" />
      <rect x="3" y="16" width="7" height="5" rx="1.5" />
    </>
  ),
  box: (
    <>
      <path d="M3 7l9-4 9 4-9 4-9-4Z" />
      <path d="M3 7v10l9 4 9-4V7" />
      <path d="M12 11v10" />
    </>
  ),
  cart: (
    <>
      <circle cx="9" cy="20" r="1.4" />
      <circle cx="18" cy="20" r="1.4" />
      <path d="M2 3h3l2.4 12.4a1.5 1.5 0 0 0 1.5 1.2h8.2a1.5 1.5 0 0 0 1.5-1.2L22 7H6" />
    </>
  ),
  users: (
    <>
      <circle cx="9" cy="8" r="3.2" />
      <path d="M2.5 20c0-3.6 2.9-5.5 6.5-5.5s6.5 1.9 6.5 5.5" />
      <path d="M16 5.2a3.2 3.2 0 0 1 0 6M17.5 14.6c2.6.5 4 2.3 4 5.4" />
    </>
  ),
  ticket: (
    <>
      <path d="M4 7a2 2 0 0 1 2-2h12a2 2 0 0 1 2 2v2a2 2 0 0 0 0 4v2a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2v-2a2 2 0 0 0 0-4V7Z" />
      <path d="M14 5v14" strokeDasharray="2 2" />
    </>
  ),
  wallet: (
    <>
      <rect x="3" y="6" width="18" height="13" rx="3" />
      <path d="M3 10h18" />
      <circle cx="17" cy="14" r="1.2" fill="currentColor" stroke="none" />
    </>
  ),
  settings: (
    <>
      <circle cx="12" cy="12" r="3" />
      <path d="M19.4 15a1.7 1.7 0 0 0 .3 1.9l.1.1a2 2 0 1 1-2.8 2.8l-.1-.1a1.7 1.7 0 0 0-2.9 1.2V21a2 2 0 1 1-4 0v-.1A1.7 1.7 0 0 0 7 19.2l-.1.1a2 2 0 1 1-2.8-2.8l.1-.1a1.7 1.7 0 0 0-1.2-2.9H3a2 2 0 1 1 0-4h.1A1.7 1.7 0 0 0 4.8 7l-.1-.1a2 2 0 1 1 2.8-2.8l.1.1a1.7 1.7 0 0 0 1.9.3H10a1.7 1.7 0 0 0 1-1.5V3a2 2 0 1 1 4 0v.1a1.7 1.7 0 0 0 2.9 1.2l.1-.1a2 2 0 1 1 2.8 2.8l-.1.1a1.7 1.7 0 0 0-.3 1.9V10a1.7 1.7 0 0 0 1.5 1H21a2 2 0 1 1 0 4h-.1a1.7 1.7 0 0 0-1.5 1Z" />
    </>
  ),
  chart: <path d="M4 20V10M10 20V4M16 20v-7M22 20H2" />,
  bell: (
    <>
      <path d="M6 9a6 6 0 0 1 12 0c0 7 2 8 2 8H4s2-1 2-8Z" />
      <path d="M10.5 21a2 2 0 0 0 3 0" />
    </>
  ),
  search: (
    <>
      <circle cx="11" cy="11" r="7" />
      <path d="M21 21l-4.3-4.3" />
    </>
  ),
  logout: (
    <>
      <path d="M15 4h3a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-3" />
      <path d="M10 17l-5-5 5-5M4 12h11" />
    </>
  ),
  plus: <path d="M12 5v14M5 12h14" />,
  menu: <path d="M4 6h16M4 12h16M4 18h16" />,
  home: (
    <>
      <path d="M3 11l9-8 9 8" />
      <path d="M5 10v10h14V10" />
    </>
  ),
  news: (
    <>
      <rect x="3" y="4" width="13" height="16" rx="2" />
      <path d="M16 8h3a1 1 0 0 1 1 1v9a2 2 0 0 1-2 2H6" />
      <path d="M7 8h5M7 12h5M7 16h3" />
    </>
  ),
  card: (
    <>
      <rect x="2" y="5" width="20" height="14" rx="2.5" />
      <path d="M2 10h20M6 15h4" />
    </>
  ),
  check: <path d="M5 12l4.5 4.5L19 7" />,
  chat: (
    <>
      <path d="M21 11.5a8.4 8.4 0 0 1-8.5 8.5 8.7 8.7 0 0 1-4-1L3 20l1-5.5a8.4 8.4 0 0 1-1-4A8.4 8.4 0 0 1 11.5 2 8.4 8.4 0 0 1 21 11.5Z" />
      <path d="M8 11h8M8 14h5" />
    </>
  ),
  star: <path d="M12 3l2.9 6 6.6.9-4.8 4.6 1.2 6.5L12 18.8 6.1 21l1.2-6.5L2.5 9.9 9.1 9 12 3Z" />,
  close: <path d="M6 6l12 12M18 6L6 18" />,
  columns: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2" />
      <path d="M3 15h18M9 15v5M15 15v5" />
    </>
  ),
  trash: (
    <>
      <path d="M4 7h16M9 7V5a1 1 0 0 1 1-1h4a1 1 0 0 1 1 1v2M6 7l1 13a1 1 0 0 0 1 1h8a1 1 0 0 0 1-1l1-13" />
    </>
  ),
  edit: (
    <>
      <path d="M12 20h9" />
      <path d="M16.5 3.5a2.1 2.1 0 0 1 3 3L7 19l-4 1 1-4 12.5-12.5Z" />
    </>
  ),
  image: (
    <>
      <rect x="3" y="4" width="18" height="16" rx="2.5" />
      <circle cx="8.5" cy="9.5" r="1.7" />
      <path d="M21 16l-5-5L5 21" />
    </>
  ),
  layout: (
    <>
      <rect x="3" y="3" width="18" height="18" rx="2.5" />
      <path d="M3 8h18M9 8v13" />
    </>
  ),
  cpu: (
    <>
      <rect x="6" y="6" width="12" height="12" rx="2" />
      <rect x="9.5" y="9.5" width="5" height="5" rx="1" />
      <path d="M9 2v3M15 2v3M9 19v3M15 19v3M2 9h3M2 15h3M19 9h3M19 15h3" />
    </>
  ),
  ram: (
    <>
      <rect x="2" y="7" width="20" height="10" rx="2" />
      <path d="M6 17v3M10 17v3M14 17v3M18 17v3M7 11h2M11 11h2M15 11h2" />
    </>
  ),
  disk: (
    <>
      <circle cx="12" cy="12" r="9" />
      <circle cx="12" cy="12" r="2.5" />
      <path d="M12 3v4M12 17v4" />
    </>
  ),
  activity: <path d="M3 12h4l3 8 4-16 3 8h4" />,
  grid: (
    <>
      <rect x="3" y="3" width="7" height="7" rx="1.5" />
      <rect x="14" y="3" width="7" height="7" rx="1.5" />
      <rect x="3" y="14" width="7" height="7" rx="1.5" />
      <rect x="14" y="14" width="7" height="7" rx="1.5" />
    </>
  ),
  tag: (
    <>
      <path d="M3 11.5V5a2 2 0 0 1 2-2h6.5a2 2 0 0 1 1.4.6l7.5 7.5a2 2 0 0 1 0 2.8l-6.6 6.6a2 2 0 0 1-2.8 0L3.6 13a2 2 0 0 1-.6-1.5Z" />
      <circle cx="7.5" cy="7.5" r="1.3" fill="currentColor" stroke="none" />
    </>
  ),
  shield: (
    <>
      <path d="M12 3l7 3v5c0 4.5-3 8-7 10-4-2-7-5.5-7-10V6l7-3Z" />
      <path d="m9 12 2 2 4-4" />
    </>
  ),
  mail: (
    <>
      <rect x="2.5" y="5" width="19" height="14" rx="2.5" />
      <path d="m3.5 7.5 7.3 5.2a2 2 0 0 0 2.4 0l7.3-5.2" />
    </>
  ),
  inbox: (
    <>
      <path d="M3 12.5V18a2 2 0 0 0 2 2h14a2 2 0 0 0 2-2v-5.5" />
      <path d="M3 12.5 5.6 5.3A2 2 0 0 1 7.5 4h9a2 2 0 0 1 1.9 1.3L21 12.5h-5a4 4 0 0 1-8 0H3Z" />
    </>
  ),
  send: (
    <>
      <path d="M21.5 2.5 10.8 13.2" />
      <path d="M21.5 2.5 14.8 21.5l-4-8.3-8.3-4 19-6.7Z" />
    </>
  ),
  reply: (
    <>
      <path d="M9 7 3.5 12 9 17" />
      <path d="M3.5 12h9a7 7 0 0 1 7 7v1.5" />
    </>
  ),
  forward: (
    <>
      <path d="m15 7 5.5 5-5.5 5" />
      <path d="M20.5 12h-9a7 7 0 0 0-7 7v1.5" />
    </>
  ),
  archive: (
    <>
      <rect x="2.5" y="4" width="19" height="4.5" rx="1.5" />
      <path d="M4.5 8.5V19a1.5 1.5 0 0 0 1.5 1.5h12a1.5 1.5 0 0 0 1.5-1.5V8.5" />
      <path d="M10 12.5h4" />
    </>
  ),
  paperclip: (
    <path d="M20 11.5 12.3 19.2a4.6 4.6 0 0 1-6.5-6.5l7.9-7.9a3.1 3.1 0 0 1 4.4 4.4l-7.9 7.9a1.6 1.6 0 0 1-2.2-2.2l7.2-7.2" />
  ),
  refresh: (
    <>
      <path d="M20.5 12a8.5 8.5 0 1 1-2.6-6.1" />
      <path d="M20.5 4v5h-5" />
    </>
  ),
  spam: (
    <>
      <path d="M12 3 3.5 6.5v5c0 5 3.6 8.4 8.5 9.5 4.9-1.1 8.5-4.5 8.5-9.5v-5L12 3Z" />
      <path d="M12 8v4.5" />
      <path d="M12 16h.01" />
    </>
  ),
};

export default function AdminIcon({ name, className }: Props) {
  return (
    <svg
      className={className}
      viewBox="0 0 24 24"
      fill="none"
      stroke="currentColor"
      strokeWidth="1.7"
      strokeLinecap="round"
      strokeLinejoin="round"
    >
      {paths[name] ?? paths.dashboard}
    </svg>
  );
}
