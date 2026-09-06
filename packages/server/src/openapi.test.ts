import { expect, test } from 'bun:test'
import { generateAuthOpenAPIDocument, generateOpenAPIDocument } from './openapi'

type Operation = {
  summary?: string
  tags?: string[]
  responses?: Record<string, { content?: Record<string, { schema?: unknown }> }>
}

type Doc = {
  openapi: string
  info: { title: string; version: string }
  paths: Record<string, Record<string, Operation>>
  components?: { securitySchemes?: Record<string, unknown> }
}

const doc = (await generateOpenAPIDocument()) as Doc

test('generates a valid looking document', () => {
  expect(doc.openapi).toStartWith('3.')
  expect(doc.info.title).toBe('ksamods.gg API')
  expect(doc.components?.securitySchemes).toHaveProperty('sessionCookie')
})

test.each([
  ['get', '/health'],
  ['get', '/me/admin'],
  ['get', '/me/listings'],
  ['get', '/content-index/latest'],
  ['get', '/listings/claimed'],
  ['get', '/listings/{listingId}/maintainers'],
  ['post', '/listings/{listingId}/claim'],
])('documents %s %s', (method, path) => {
  expect(doc.paths[path]?.[method]).toBeDefined()
})

test('does not advertise the admin procedures', () => {
  // They are still served and still gated by adminOnly. Hiding them is a
  // documentation choice, never the authorization mechanism.
  expect(doc.paths['/admin/claims']).toBeUndefined()
  expect(doc.paths['/admin/claims/{claimId}/decide']).toBeUndefined()
  expect(Object.keys(doc.paths).filter((p) => p.startsWith('/admin/'))).toEqual([])
})

test('does not advertise the auth admin routes either', async () => {
  const authDoc = (await generateAuthOpenAPIDocument()) as { paths: Record<string, unknown> }
  const paths = Object.keys(authDoc.paths)
  expect(paths.filter((p) => p.startsWith('/admin/'))).toEqual([])
  // The rest of the auth surface is still documented.
  expect(paths).toContain('/sign-in/email')
  expect(paths).toContain('/steam/login')
  expect(paths.length).toBeGreaterThan(20)
})

test('every operation carries a summary and a tag', () => {
  // Route metadata is easy to forget when adding a procedure, and the failure
  // is silent: the operation still works but documents itself as an unlabelled
  // POST /procedure/path.
  for (const [path, operations] of Object.entries(doc.paths)) {
    for (const [method, operation] of Object.entries(operations)) {
      expect(operation.summary, `${method} ${path} has no summary`).toBeTruthy()
      expect(operation.tags?.length, `${method} ${path} has no tag`).toBeGreaterThan(0)
    }
  }
})

test('path parameters are declared where the route names them', () => {
  const claim = doc.paths['/listings/{listingId}/claim']?.post as
    | { parameters?: { name: string; in: string }[] }
    | undefined
  expect(claim?.parameters?.some((p) => p.name === 'listingId' && p.in === 'path')).toBe(true)
})

/** What oRPC emits when a procedure declares no output schema at all. */
const UNDESCRIBED = JSON.stringify({ anyOf: [{}, { not: {} }] })

test('every operation describes its 200 response', () => {
  for (const [path, operations] of Object.entries(doc.paths)) {
    for (const [method, operation] of Object.entries(operations)) {
      const schema = operation.responses?.['200']?.content?.['application/json']?.schema
      expect(schema, `${method} ${path} has no response schema`).toBeDefined()
      expect(JSON.stringify(schema), `${method} ${path} response is undescribed`).not.toBe(
        UNDESCRIBED,
      )
    }
  }
})

test('the snapshot response documents the nested index', () => {
  const schema = JSON.stringify(
    doc.paths['/content-index/latest']?.get?.responses?.['200']?.content?.['application/json']
      ?.schema,
  )
  for (const field of ['listings', 'authored', 'releases', 'sha256', 'game_min']) {
    expect(schema, `snapshot schema is missing ${field}`).toContain(field)
  }
  // The snapshot is stored verbatim, so the document must not claim unknown
  // upstream fields are rejected.
  expect(schema).not.toContain('"additionalProperties":false')
})
