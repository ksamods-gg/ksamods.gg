/** What the admin dashboard should show. */
export type AdminAccess =
  | "checking"
  | "allowed"
  | "denied"
  | "signed-out"
  | "unreachable";

/** The outcome of asking the server whether the caller is an admin. */
export type AdminCheck = "pending" | "admin" | "not-admin" | "failed";

/**
 * amIAdmin answers false for a non-admin and only rejects when the question
 * could not be answered at all, so "failed" must never render as "denied".
 * Collapsing the two turns an API that is down, or a session that expired
 * mid-page, into a permissions message, which sends you looking in the wrong
 * place for a problem that has nothing to do with your account.
 */
export function adminAccess(
  sessionPending: boolean,
  signedIn: boolean,
  check: AdminCheck,
): AdminAccess {
  if (sessionPending) return "checking";
  if (!signedIn) return "signed-out";

  switch (check) {
    case "admin":
      return "allowed";
    case "not-admin":
      return "denied";
    case "failed":
      return "unreachable";
    default:
      return "checking";
  }
}
