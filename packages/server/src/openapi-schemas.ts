import { JSON_SCHEMA_INPUT_REGISTRY, JSON_SCHEMA_OUTPUT_REGISTRY } from '@orpc/zod/zod4'
import { z } from 'zod'
import { authoredDocument } from './authored'
import { type ContentIndex, contentIndexSchema } from './content-index-schema'

/**
 * JSON Schema for the upstream document, for documentation only.
 *
 * Built with io: 'input' rather than 'output' because output mode stamps
 * additionalProperties: false on every object, which would claim we reject
 * unknown upstream fields when in fact we preserve them. The schema has no
 * transforms, so the two modes are otherwise identical.
 */
function contentIndexJsonSchema() {
  const { $schema, ...schema } = z.toJSONSchema(contentIndexSchema, {
    io: 'input',
    unrepresentable: 'any',
  }) as Record<string, unknown>
  return {
    ...schema,
    description:
      'The upstream document, stored verbatim. Unknown upstream fields are preserved even though they are not described here.',
  }
}

/**
 * The snapshot payload is typed and documented but deliberately not validated.
 *
 * Running the real `contentIndexSchema` here would be wrong twice over: it is
 * built from `z.object`, so it would strip the unknown upstream keys the
 * snapshot exists to preserve, and it would re-validate roughly 50KB on the
 * hottest public route to re-check data that was already validated on ingest.
 * `z.custom` keeps the ContentIndex type and passes the value through untouched,
 * while the registry supplies the rich schema the docs need.
 */
const snapshotDataSchema = z.custom<ContentIndex>()
JSON_SCHEMA_OUTPUT_REGISTRY.add(snapshotDataSchema, contentIndexJsonSchema())

export const snapshotOutputSchema = z
  .object({
    snapshotVersion: z.number().int(),
    authoredCommit: z.string().describe('Commit of the authored source repository.'),
    generatedCommit: z
      .string()
      .describe('Commit of the generated release repository. Snapshots are keyed by this.'),
    listingCount: z.number().int(),
    fetchedAt: z.date(),
    data: snapshotDataSchema,
  })
  .nullable()
  .describe('The newest stored snapshot, or null before the first sync.')

/**
 * The authored document as a procedure input.
 *
 * Deliberately not validated by oRPC: `preview` exists to report every problem
 * with a document as field-keyed blockers, which it cannot do if the framework
 * rejects the payload before the handler runs. `submitListing` validates with
 * the real schema. The registry supplies the rich schema the docs need.
 */
const documentInput = z.custom<unknown>()
JSON_SCHEMA_INPUT_REGISTRY.add(documentInput, {
  ...(z.toJSONSchema(authoredDocument, {
    io: 'input',
    unrepresentable: 'any',
  }) as Record<string, unknown>),
  description:
    'A listing document as described by KSAModding/content-index schemas/authored.schema.json.',
})

export const documentInputSchema = documentInput
