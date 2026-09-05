import { createORPCClient } from '@orpc/client'
import { RPCLink } from '@orpc/client/fetch'
import { headers } from 'next/headers'
import type { Client } from 'server/router'

/**
 * Server-side client. A server component has no cookie jar of its own, so the
 * caller's cookie header is forwarded to make the RPC run as the signed-in user.
 * Use lib/orpc-browser.ts from client components instead.
 */
const link = new RPCLink({
  url: `${process.env.NEXT_PUBLIC_SERVER_URL ?? 'http://localhost:3000'}/rpc`,
  headers: async () => {
    try {
      const cookie = (await headers()).get('cookie')
      return cookie ? { cookie } : {}
    } catch {
      // Outside a request scope, such as a build-time prerender. No session.
      return {}
    }
  },
})

export const orpc: Client = createORPCClient(link)
