"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "cn";
import { orpc } from "@/lib/orpc-browser";

const SECTIONS = [
  { href: "/admin/users", label: "Users" },
  { href: "/admin/claims", label: "Claims" },
  { href: "/admin/links", label: "Link issues" },
] as const;

export function AdminNav() {
  const pathname = usePathname();
  // Fetched here rather than passed down, so the count refreshes on every
  // admin page load instead of being frozen into the layout's payload.
  const [issues, setIssues] = useState(0);

  useEffect(() => {
    orpc.listings.admin
      .linkIssues()
      .then((rows) => setIssues(rows.length))
      .catch(() => setIssues(0));
  }, []);

  return (
    // A row that scrolls on small screens, a column beside the content above it.
    <nav
      aria-label="Admin sections"
      className="-mx-6 flex shrink-0 gap-1 overflow-x-auto px-6 md:mx-0 md:w-48 md:flex-col md:overflow-visible md:px-0"
    >
      {SECTIONS.map((section) => {
        const active = pathname === section.href;
        return (
          <Link
            key={section.href}
            href={section.href}
            aria-current={active ? "page" : undefined}
            className={cn(
              "flex items-center justify-between gap-2 rounded-md px-3 py-2 text-sm whitespace-nowrap transition-colors",
              active
                ? "bg-muted text-foreground font-medium"
                : "text-muted-foreground hover:bg-muted/60 hover:text-foreground",
            )}
          >
            {section.label}
            {section.href === "/admin/links" && issues > 0 && (
              <span className="bg-destructive text-destructive-foreground inline-flex h-5 min-w-5 items-center justify-center rounded-full px-1.5 text-xs font-medium">
                {issues}
              </span>
            )}
          </Link>
        );
      })}
    </nav>
  );
}
