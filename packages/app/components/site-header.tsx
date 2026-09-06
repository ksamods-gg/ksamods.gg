import Link from "next/link";
import { SessionBadge } from "@/app/session-badge";
import { SITE_CONTAINER } from "@/lib/layout";
import { ThemeToggle } from "@/components/theme-toggle";

export function SiteHeader() {
  return (
    <header className="border-border bg-background/80 sticky top-0 z-50 border-b backdrop-blur">
      <div
        className={`${SITE_CONTAINER} flex h-14 items-center justify-between gap-4`}
      >
        <div className="flex items-center gap-6">
          <Link href="/" className="font-semibold tracking-tight">
            ksamods<span className="text-muted-foreground">.gg</span>
          </Link>
          <Link
            href="/mods"
            className="text-muted-foreground hover:text-foreground text-sm"
          >
            Mods
          </Link>
          <Link
            href="/mods/new"
            className="text-muted-foreground hover:text-foreground text-sm"
          >
            Submit
          </Link>
        </div>
        <div className="flex items-center gap-3">
          <SessionBadge />
          <ThemeToggle />
        </div>
      </div>
    </header>
  );
}
