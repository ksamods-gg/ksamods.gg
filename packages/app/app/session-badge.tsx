"use client";

import { useEffect, useState } from "react";
import Link from "next/link";
import { Button } from "@/components/ui/button";
import { signOut, useSession } from "@/lib/auth-client";
import { orpc } from "@/lib/orpc-browser";

export function SessionBadge() {
  const { data: session, isPending } = useSession();
  // Asked of the server rather than read off session.user.role, because a
  // break-glass admin has no role column set. Cosmetic only: the adminOnly
  // procedures are the real gate.
  const [isAdmin, setIsAdmin] = useState(false);

  useEffect(() => {
    if (!session) return setIsAdmin(false);
    orpc.amIAdmin()
      .then(setIsAdmin)
      .catch(() => setIsAdmin(false));
  }, [session]);

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
