import { expect, test } from "bun:test";
import { renderToStaticMarkup } from "react-dom/server";
import { Prose } from "./markdown";

const render = (markdown: string) =>
  renderToStaticMarkup(<Prose>{markdown}</Prose>);

test("renders headings, emphasis and lists", () => {
  const html = render("### Title\n\n**bold** and *italic*\n\n- one\n- two");
  expect(html).toContain("<h3>Title</h3>");
  expect(html).toContain("<strong>bold</strong>");
  expect(html).toContain("<em>italic</em>");
  expect(html).toContain("<li>one</li>");
});

test("keeps angle-bracket placeholders in code spans", () => {
  // StarMap documents paths like mods/<ModName>/. Enabling raw HTML would
  // parse those as unknown tags and silently drop them.
  const html = render("Put mods in `mods/<ModName>/` please");
  expect(html).toContain("&lt;ModName&gt;");
});

test.each([
  ["a script tag", "<script>alert(1)</script>"],
  ["an image handler", '<img src=x onerror="alert(1)">'],
  ["an iframe", '<iframe src="https://evil.example"></iframe>'],
  ["a style block", "<style>body{display:none}</style>"],
])("leaves %s inert", (_label, hostile) => {
  const html = render(`Intro text\n\n${hostile}\n\nOutro`);
  // It must survive only as escaped text, never as live markup. Asserting on
  // the raw substring would pass on the escaped copy, so check the tag opener.
  for (const tag of ["script", "img", "iframe", "style"]) {
    expect(html).not.toContain(`<${tag}`);
  }
  expect(html).toContain("&lt;");
  // The surrounding content still renders.
  expect(html).toContain("Intro text");
  expect(html).toContain("Outro");
});

test("does not emit javascript: links", () => {
  const html = render("[click](javascript:alert(1))");
  expect(html).not.toContain('href="javascript:');
});
