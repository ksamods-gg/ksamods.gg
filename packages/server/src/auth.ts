import { betterAuth } from 'better-auth'
import { prismaAdapter } from 'better-auth/adapters/prisma'
import { admin } from 'better-auth/plugins'
import { steamOpenID } from 'better-auth-steam'
import { db } from './db'

const secret = process.env.BETTER_AUTH_SECRET
if (!secret) throw new Error('Missing required env var: BETTER_AUTH_SECRET')

const discordId = process.env.DISCORD_CLIENT_ID
const discordSecret = process.env.DISCORD_CLIENT_SECRET
const githubId = process.env.GITHUB_CLIENT_ID
const githubSecret = process.env.GITHUB_CLIENT_SECRET
const steamApiKey = process.env.STEAM_API_KEY

// Break-glass bootstrap: these ids are admin regardless of the role column, so
// a fresh database always has an arbiter. Everyone else is promoted via setRole.
const adminUserIds = (process.env.ADMIN_USER_IDS ?? '')
  .split(',')
  .map((id) => id.trim())
  .filter(Boolean)

/** The admin plugin stores role as a comma separated list. */
export function isAdmin(user: { id: string; role?: string | null }) {
  return adminUserIds.includes(user.id) || (user.role?.split(',').includes('admin') ?? false)
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
  plugins: [steamOpenID({ apiKey: steamApiKey ?? '' }), admin({ adminUserIds })],
})
