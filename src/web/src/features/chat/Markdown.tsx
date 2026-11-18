import ReactMarkdown, { type Components } from "react-markdown";

const components: Components = {
  p: ({ children }) => <p className="my-2 first:mt-0 last:mb-0">{children}</p>,
  ul: ({ children }) => <ul className="my-2 list-disc space-y-1 pl-5">{children}</ul>,
  ol: ({ children }) => <ol className="my-2 list-decimal space-y-1 pl-5">{children}</ol>,
  strong: ({ children }) => <strong className="font-semibold">{children}</strong>,
  a: ({ href, children }) => (
    <a href={href} target="_blank" rel="noopener noreferrer nofollow" className="underline">
      {children}
    </a>
  ),
  code: ({ children }) => (
    <code className="rounded bg-muted px-1 font-mono text-sm">{children}</code>
  ),
  // Model text never adds pictures to the page (spec 06: movie data comes from cards, not from the model).
  img: () => null,
  h1: ({ children }) => <p className="my-2 font-semibold">{children}</p>,
  h2: ({ children }) => <p className="my-2 font-semibold">{children}</p>,
  h3: ({ children }) => <p className="my-2 font-semibold">{children}</p>,
};

/**
 * Assistant answers are markdown. Rendered without raw HTML (`skipHtml`): the text comes from a model that
 * read untrusted movie data, so it must not be able to inject markup. Link URLs are sanitized by react-markdown.
 */
export function Markdown({ text }: { text: string }) {
  return (
    <div className="text-sm leading-relaxed break-words">
      <ReactMarkdown skipHtml components={components}>
        {text}
      </ReactMarkdown>
    </div>
  );
}
