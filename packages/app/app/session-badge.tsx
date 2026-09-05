"use client";

import Link from "next/link";
import { Button } from "@/components/ui/button";
import { signOut, useSession } from "@/lib/auth-client";

export function SessionBadge() {
  const { data: session, isPending } = useSession();

  if (isPending) return null;

  if (!session)
    return (
      // Renders an <a>, so Base UI must not assume native button semantics.
      <Button
        render={<Link href="/sign-in" />}
        nativeButton={false}
        variant="outline"
        size="sm"
      >
        Sign in
      </Button>
    );

  // Cosmetic only. The oRPC procedures are the real gate.
  const isAdmin = (session.user as { role?: string | null }).role
    ?.split(",")
    .includes("admin");

  return (
    <div className="flex items-center gap-3">
      {isAdmin && (
        <Button
          render={<Link href="/admin" />}
          nativeButton={false}
          variant="ghost"
          size="sm"
        >
          Admin
        </Button>
      )}
      <Button
        render={<Link href="/account" />}
        nativeButton={false}
        variant="ghost"
        size="sm"
      >
        {session.user.name || session.user.email}
      </Button>
      <Button
        variant="outline"
        size="sm"
        onClick={() => signOut().then(() => window.location.reload())}
      >
        Sign out
      </Button>
    </div>
  );
}
