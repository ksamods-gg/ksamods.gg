"use client";

import { useCallback, useEffect, useState } from "react";
import { MoreHorizontal } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from "@/components/ui/dialog";
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from "@/components/ui/dropdown-menu";
import { Input } from "@/components/ui/input";
import { Label } from "@/components/ui/label";
import { useSession } from "@/lib/auth-client";
import { orpc } from "@/lib/orpc-browser";

type AdminUser = {
  id: string;
  name: string;
  email: string;
  role: string | null;
  banned: boolean | null;
  banReason: string | null;
  createdAt: Date | string;
  providers: string[];
  breakGlass: boolean;
  claims: number;
  submissions: number;
};

const PROVIDER_LABELS: Record<string, string> = {
  discord: "Discord",
  github: "GitHub",
  steam: "Steam",
  credential: "Password",
};

const isAdmin = (user: AdminUser) =>
  user.breakGlass || (user.role?.split(",").includes("admin") ?? false);

const day = (value: Date | string) =>
  new Date(value).toISOString().slice(0, 10);

export default function AdminUsersPage() {
  const { data: session } = useSession();
  const [query, setQuery] = useState("");
  const [users, setUsers] = useState<AdminUser[] | null>(null);
  const [busy, setBusy] = useState<string | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [banning, setBanning] = useState<AdminUser | null>(null);
  const [reason, setReason] = useState("");

  const load = useCallback((q: string) => {
    orpc.users.admin
      .list(q ? { q } : {})
      .then((rows) => setUsers(rows as AdminUser[]))
      .catch(() => setUsers([]));
  }, []);

  // Debounced, so typing a name is one request rather than one per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => load(query.trim()), 250);
    return () => clearTimeout(timer);
  }, [query, load]);

  async function act(id: string, run: () => Promise<unknown>) {
    setBusy(id);
    setError(null);
    try {
      await run();
      load(query.trim());
    } catch (cause) {
      setError(cause instanceof Error ? cause.message : "That did not work.");
    } finally {
      setBusy(null);
    }
  }

  const setRole = (user: AdminUser, admin: boolean) =>
    act(user.id, () => orpc.users.admin.setRole({ userId: user.id, admin }));

  const unban = (user: AdminUser) =>
    act(user.id, () =>
      orpc.users.admin.setBan({ userId: user.id, banned: false }),
    );

  async function confirmBan() {
    if (!banning) return;
    const user = banning;
    setBanning(null);
    await act(user.id, () =>
      orpc.users.admin.setBan({
        userId: user.id,
        banned: true,
        reason: reason.trim() || undefined,
      }),
    );
    setReason("");
  }

  return (
    <>
      <div className="mb-6 space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight">Users</h1>
        <p className="text-muted-foreground text-sm">
          Everyone with an account, newest first. Suspending an account also
          signs it out everywhere.
        </p>
      </div>

      <Input
        className="mb-6 max-w-sm"
        type="search"
        placeholder="Search name or email"
        value={query}
        onChange={(event) => setQuery(event.target.value)}
        aria-label="Search users"
      />

      {error && (
        <p role="alert" className="text-destructive mb-4 text-sm">
          {error}
        </p>
      )}

      {users === null ? (
        <p className="text-muted-foreground text-sm">Loading...</p>
      ) : users.length === 0 ? (
        <div className="border-border text-muted-foreground rounded-lg border border-dashed py-16 text-center text-sm">
          {query ? "No users match that search." : "No users yet."}
        </div>
      ) : (
        <div className="border-border overflow-x-auto rounded-lg border">
          <table className="w-full text-sm">
            <thead className="bg-muted/50 text-muted-foreground">
              <tr>
                <th className="px-4 py-2.5 text-left font-medium">User</th>
                <th className="px-4 py-2.5 text-left font-medium">Sign-in</th>
                <th className="px-4 py-2.5 text-right font-medium">Claims</th>
                <th className="px-4 py-2.5 text-right font-medium">
                  Submissions
                </th>
                <th className="px-4 py-2.5 text-left font-medium">Joined</th>
                <th className="w-10 px-4 py-2.5">
                  <span className="sr-only">Actions</span>
                </th>
              </tr>
            </thead>
            <tbody>
              {users.map((user) => {
                const self = user.id === session?.user.id;
                const admin = isAdmin(user);

                return (
                  <tr
                    key={user.id}
                    className="border-border border-t align-middle"
                  >
                    <td className="px-4 py-3">
                      <div className="flex flex-wrap items-center gap-2">
                        <span className="font-medium">{user.name}</span>
                        {self && <Badge variant="outline">You</Badge>}
                        {admin && (
                          <Badge variant="secondary">
                            {user.breakGlass ? "Admin (Discord)" : "Admin"}
                          </Badge>
                        )}
                        {user.banned && (
                          <Badge variant="destructive">Suspended</Badge>
                        )}
                      </div>
                      <div className="text-muted-foreground">{user.email}</div>
                      {user.banned && user.banReason && (
                        <div className="text-muted-foreground text-xs">
                          {user.banReason}
                        </div>
                      )}
                    </td>
                    <td className="text-muted-foreground px-4 py-3">
                      {user.providers.length === 0
                        ? "None"
                        : user.providers
                            .map((id) => PROVIDER_LABELS[id] ?? id)
                            .join(", ")}
                    </td>
                    <td className="text-muted-foreground px-4 py-3 text-right tabular-nums">
                      {user.claims}
                    </td>
                    <td className="text-muted-foreground px-4 py-3 text-right tabular-nums">
                      {user.submissions}
                    </td>
                    <td className="text-muted-foreground px-4 py-3 whitespace-nowrap">
                      {day(user.createdAt)}
                    </td>
                    <td className="px-4 py-3">
                      <DropdownMenu>
                        <DropdownMenuTrigger
                          render={
                            <Button
                              variant="ghost"
                              size="icon"
                              disabled={self || busy === user.id}
                              aria-label={`Actions for ${user.name}`}
                            >
                              <MoreHorizontal />
                            </Button>
                          }
                        />
                        <DropdownMenuContent align="end">
                          {user.breakGlass ? (
                            // ADMIN_DISCORD_IDS is env, not a column, so the
                            // role toggle cannot revoke it. Saying so beats a
                            // menu item that appears to work and does nothing.
                            <DropdownMenuItem disabled>
                              Admin via ADMIN_DISCORD_IDS
                            </DropdownMenuItem>
                          ) : (
                            <DropdownMenuItem
                              onClick={() => setRole(user, !admin)}
                            >
                              {admin ? "Remove admin" : "Make admin"}
                            </DropdownMenuItem>
                          )}
                          <DropdownMenuSeparator />
                          {user.banned ? (
                            <DropdownMenuItem onClick={() => unban(user)}>
                              Restore account
                            </DropdownMenuItem>
                          ) : (
                            <DropdownMenuItem
                              className="text-destructive"
                              onClick={() => {
                                setReason("");
                                setBanning(user);
                              }}
                            >
                              Suspend account
                            </DropdownMenuItem>
                          )}
                        </DropdownMenuContent>
                      </DropdownMenu>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      <Dialog
        open={banning !== null}
        onOpenChange={(open) => !open && setBanning(null)}
      >
        <DialogContent className="sm:max-w-md">
          <DialogHeader>
            <DialogTitle>Suspend {banning?.name}</DialogTitle>
            <DialogDescription>
              They are signed out everywhere and cannot sign back in. Their
              claims and submissions are left alone.
            </DialogDescription>
          </DialogHeader>
          <div className="space-y-2">
            <Label htmlFor="ban-reason">Reason</Label>
            <Input
              id="ban-reason"
              value={reason}
              onChange={(event) => setReason(event.target.value)}
              placeholder="Optional, kept for your own record"
            />
          </div>
          <DialogFooter>
            <Button variant="outline" onClick={() => setBanning(null)}>
              Cancel
            </Button>
            <Button onClick={confirmBan}>Suspend</Button>
          </DialogFooter>
        </DialogContent>
      </Dialog>
    </>
  );
}
