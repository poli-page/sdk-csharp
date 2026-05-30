// Minimal C# XML doc parser tailored to the subset of tags the .NET compiler
// emits: <summary>, <remarks>, <param>, <returns>, <exception>. Avoids pulling
// in an XML library — the input is well-formed by construction.

export interface XmlDocMember {
  readonly name: string;
  readonly summary: string;
  readonly remarks: string;
  readonly returns: string;
  readonly params: ReadonlyMap<string, string>;
  readonly exceptions: ReadonlyArray<{ cref: string; description: string }>;
}

const MEMBER_RE = /<member\s+name="([^"]+)"\s*>([\s\S]*?)<\/member>/g;
const SUMMARY_RE = /<summary\s*>([\s\S]*?)<\/summary>/;
const REMARKS_RE = /<remarks\s*>([\s\S]*?)<\/remarks>/;
const RETURNS_RE = /<returns\s*>([\s\S]*?)<\/returns>/;
const PARAM_RE = /<param\s+name="([^"]+)"\s*>([\s\S]*?)<\/param>/g;
const EXCEPTION_RE = /<exception\s+cref="([^"]+)"\s*>([\s\S]*?)<\/exception>/g;

export function parseXmlDocs(xml: string): XmlDocMember[] {
  const out: XmlDocMember[] = [];
  let m: RegExpExecArray | null;
  MEMBER_RE.lastIndex = 0;
  while ((m = MEMBER_RE.exec(xml)) !== null) {
    const name = m[1] ?? '';
    const body = m[2] ?? '';
    out.push({
      name,
      summary: clean(SUMMARY_RE.exec(body)?.[1]),
      remarks: clean(REMARKS_RE.exec(body)?.[1]),
      returns: clean(RETURNS_RE.exec(body)?.[1]),
      params: collectParams(body),
      exceptions: collectExceptions(body),
    });
  }
  return out;
}

function collectParams(body: string): Map<string, string> {
  const out = new Map<string, string>();
  let m: RegExpExecArray | null;
  PARAM_RE.lastIndex = 0;
  while ((m = PARAM_RE.exec(body)) !== null) {
    out.set(m[1] ?? '', clean(m[2]));
  }
  return out;
}

function collectExceptions(body: string): Array<{ cref: string; description: string }> {
  const out: Array<{ cref: string; description: string }> = [];
  let m: RegExpExecArray | null;
  EXCEPTION_RE.lastIndex = 0;
  while ((m = EXCEPTION_RE.exec(body)) !== null) {
    out.push({ cref: m[1] ?? '', description: clean(m[2]) });
  }
  return out;
}

function clean(s: string | undefined): string {
  if (!s) return '';
  // <see cref="X"/> -> `X`; <paramref name="x"/> -> `x`; <c>x</c> -> `x`.
  let t = s.replace(/<see\s+cref="([^"]+)"\s*\/>/g, (_, c) => '`' + stripPrefix(c) + '`');
  t = t.replace(/<see\s+langword="([^"]+)"\s*\/>/g, '`$1`');
  t = t.replace(/<paramref\s+name="([^"]+)"\s*\/>/g, '`$1`');
  t = t.replace(/<c>([^<]+)<\/c>/g, '`$1`');
  t = t.replace(/<para>/g, '\n\n').replace(/<\/para>/g, '');
  t = t.replace(/\s+/g, ' ').trim();
  return t;
}

function stripPrefix(cref: string): string {
  // T:Foo.Bar -> Foo.Bar; M:Foo.Bar.Baz -> Foo.Bar.Baz.
  return cref.replace(/^[A-Z]:/, '');
}

export function firstSentence(text: string): string {
  if (!text) return '';
  const m = text.match(/^(.+?[.!?])(?:\s|$)/);
  return (m?.[1] ?? text).trim();
}
