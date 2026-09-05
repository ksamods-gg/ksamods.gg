import Markdown from "react-markdown";

/**
 * Renders markdown that came from the upstream content index, which we do not
 * control and therefore do not trust.
 *
 * Raw HTML stays disabled: react-markdown escapes it unless rehype-raw is
 * added, and it must not be. That keeps any injected markup inert, and it is
 * also what makes literal placeholders such as <ModName> render as text
 * instead of being parsed away as unknown tags.
 *
 * No remark-gfm: nothing upstream uses tables, task lists or strikethrough
 * today. Add it when something does.
 */
export function Prose({ children }: { children: string }) {
  return (
    <div className="prose prose-sm prose-neutral dark:prose-invert prose-headings:font-semibold prose-headings:tracking-tight prose-a:underline-offset-4 max-w-none">
      <Markdown>{children}</Markdown>
    </div>
  );
}
