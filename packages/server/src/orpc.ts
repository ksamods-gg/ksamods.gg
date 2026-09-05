import { ORPCError, os } from '@orpc/server'
import { auth, isAdmin } from './auth'

/**
 * Only the request headers ride in the context, not a resolved session. The
 * public content-index route is the hottest path on the site and must not pay
 * for a session query it never reads; the authed base resolves lazily instead.
 */
export type RpcContext = { headers: Headers }

/**
 * Derived from the API call rather than auth.$Infer.Session, which collapses to
 * a plugin's own Session type depending on plugin order.
 */
export type Session = NonNullable<Awaited<ReturnType<typeof auth.api.getSession>>>

export const pub = os.$context<RpcContext>()

export const authed = pub.use(async ({ context, next }) => {
  const session = await auth.api.getSession({ headers: context.headers })
  if (!session) throw new ORPCError('UNAUTHORIZED')
  // banUser already deletes sessions, but a ban landing mid-request must not
  // still be able to write.
  if (session.user.banned) throw new ORPCError('FORBIDDEN', { message: 'Account suspended' })

  return next({ context: { session, user: session.user } })
})

/**
 * Authorization lives here, on the procedure. Any page-level redirect is UX
 * only and is not a substitute for this check.
 */
export const adminOnly = authed.use(async ({ context, next }) => {
  if (!(await isAdmin(context.user))) throw new ORPCError('FORBIDDEN')
  return next()
})
