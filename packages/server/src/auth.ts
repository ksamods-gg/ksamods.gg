import { betterAuth } from 'better-auth'
import { prismaAdapter } from 'better-auth/adapters/prisma'
import { admin, openAPI } from 'better-auth/plugins'
import { steamOpenID } from 'better-auth-steam'
import { db } from './db'

const secret = process.env.BETTER_AUTH_SECRET
if (!secret) throw new Error('Missing required env var: BETTER_AUTH_SECRET')

const discordId = process.env.DISCORD_CLIENT_ID
const discordSecret = process.env.DISCORD_CLIENT_SECRET
const githubId = process.env.GITHUB_CLIENT_ID
const githubSecret = process.env.GITHUB_CLIENT_SECRET
const steamApiKey = process.env.STEAM_API_KEY

// Break-glass bootstrap. Discord snowflakes rather than internal user ids: a
// user id is an opaque cuid that only exists after signup, while a Discord id
// is knowable up front, so a fresh database can always be given an arbiter.
const adminDiscordIds = (process.env.ADMIN_DISCORD_IDS ?? '')
  .split(',')
  .map((id) => id.trim())
  .filter(Boolean)

/**
 * The role column is the normal path and costs no query. The Discord list is
 * the fallback, and needs one indexed lookup because a Discord id lives on the
 * account row, not the user.
 */
export async function isAdmin(user: { id: string; role?: string | null }) {
  if (user.role?.split(',').includes('admin')) return true
  if (adminDiscordIds.length === 0) return false

  const account = await db.account.findFirst({
    where: { userId: user.id, providerId: 'discord', accountId: { in: adminDiscordIds } },
    select: { id: true },
  })
  return account !== null
}

// Providers register only when configured, so local dev works with email alone.
// Anything missing is named on boot rather than failing at the redirect.
const missing = [
  !discordId || !discordSecret ? 'discord' : null,
  !githubId || !githubSecret ? 'github' : null,
  !steamApiKey ? 'steam' : null,
].filter(Boolean)
if (missing.length) console.warn(`[auth] disabled providers (no credentials): ${missing.join(', ')}`)

export const auth = betterAuth({
  database: prismaAdapter(db, { provider: 'postgresql' }),
  secret,
  baseURL: process.env.BETTER_AUTH_URL ?? 'http://localhost:3000',
  // The host is already api.ksamods.gg, so /api/auth would say it twice.
  // The client sets the same basePath; the two have to agree or every
  // auth call 404s.
  basePath: '/auth',
  trustedOrigins: [process.env.APP_URL ?? 'http://localhost:3001'],

  emailAndPassword: { enabled: true },

  socialProviders: {
    ...(discordId && discordSecret
      ? { discord: { clientId: discordId, clientSecret: discordSecret } }
      : {}),
    ...(githubId && githubSecret
      ? { github: { clientId: githubId, clientSecret: githubSecret } }
      : {}),
  },

  // Steam returns no email, so a Steam identity can never match an existing
  // account by email, and linking has to be allowed across differing emails.
  account: { accountLinking: { allowDifferentEmails: true } },

  // Registered unconditionally: it contributes user.steamId, so making it
  // conditional would make the generated Prisma schema depend on the env.
  // No adminUserIds: the plugin's own endpoints take internal user ids, which
  // the Discord break-glass list cannot supply. They authorize off the role
  // column instead, so promote a bootstrap admin with setRole once signed in.
  // openAPI documents the auth HTTP routes themselves, separately from the
  // oRPC document. The bundled reference page is disabled because it cannot
  // filter routes; the server serves a filtered copy at /docs/auth instead.
  plugins: [
    steamOpenID({ apiKey: steamApiKey ?? '' }),
    admin(),
    openAPI({ disableDefaultReference: true }),
  ],
})
