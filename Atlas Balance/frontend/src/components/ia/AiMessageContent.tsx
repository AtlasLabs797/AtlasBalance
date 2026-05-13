import { Fragment, type ReactNode } from 'react';

type AiMessageBlock =
  | { type: 'paragraph'; text: string }
  | { type: 'list'; ordered: boolean; items: string[] }
  | { type: 'facts'; headers: string[]; rows: string[][] };

interface AiMessageContentProps {
  content: string;
}

const TABLE_SEPARATOR_CELL = /^:?-{3,}:?$/;

export function AiMessageContent({ content }: AiMessageContentProps) {
  const blocks = parseAiMessageBlocks(content);

  return (
    <div className="ai-chat-message-content">
      {blocks.map((block, index) => renderBlock(block, index))}
    </div>
  );
}

function parseAiMessageBlocks(content: string): AiMessageBlock[] {
  const lines = content.replace(/\r\n/g, '\n').split('\n');
  const blocks: AiMessageBlock[] = [];
  let paragraphLines: string[] = [];
  let listItems: string[] = [];
  let listOrdered = false;

  const flushParagraph = () => {
    const text = normalizeCopy(paragraphLines.join(' '));
    if (text) {
      blocks.push({ type: 'paragraph', text });
    }
    paragraphLines = [];
  };

  const flushList = () => {
    if (listItems.length > 0) {
      blocks.push({ type: 'list', ordered: listOrdered, items: listItems });
    }
    listItems = [];
    listOrdered = false;
  };

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index].trim();

    if (!line) {
      flushParagraph();
      flushList();
      continue;
    }

    if (isMarkdownTableStart(lines, index)) {
      flushParagraph();
      flushList();

      const headers = splitMarkdownRow(lines[index]).map(cleanPlainText);
      const rows: string[][] = [];
      index += 2;

      while (index < lines.length && splitMarkdownRow(lines[index]).length > 1 && lines[index].includes('|')) {
        if (!isMarkdownTableSeparator(lines[index])) {
          rows.push(splitMarkdownRow(lines[index]).map(cleanPlainText));
        }
        index += 1;
      }

      index -= 1;
      if (headers.length > 0 && rows.length > 0) {
        blocks.push({ type: 'facts', headers, rows });
      }
      continue;
    }

    const bulletMatch = /^[-*]\s+(.+)$/.exec(line);
    const orderedMatch = /^\d+[.)]\s+(.+)$/.exec(line);
    if (bulletMatch || orderedMatch) {
      flushParagraph();
      const ordered = Boolean(orderedMatch);
      if (listItems.length > 0 && listOrdered !== ordered) {
        flushList();
      }
      listOrdered = ordered;
      listItems.push(normalizeCopy((orderedMatch ?? bulletMatch)?.[1] ?? ''));
      continue;
    }

    flushList();
    paragraphLines.push(line);
  }

  flushParagraph();
  flushList();

  return blocks.length > 0 ? blocks : [{ type: 'paragraph', text: '' }];
}

function renderBlock(block: AiMessageBlock, index: number): ReactNode {
  if (block.type === 'paragraph') {
    return <p key={`paragraph-${index}`}>{renderInline(block.text, `paragraph-${index}`)}</p>;
  }

  if (block.type === 'list') {
    const ListTag = block.ordered ? 'ol' : 'ul';
    return (
      <ListTag key={`list-${index}`}>
        {block.items.map((item, itemIndex) => (
          <li key={`list-${index}-${itemIndex}`}>{renderInline(item, `list-${index}-${itemIndex}`)}</li>
        ))}
      </ListTag>
    );
  }

  return (
    <dl key={`facts-${index}`} className="ai-chat-facts">
      {block.rows.map((row, rowIndex) => (
        <div className="ai-chat-facts-row" key={`facts-${index}-${rowIndex}`}>
          {block.headers.map((header, cellIndex) => (
            <div className="ai-chat-fact" key={`facts-${index}-${rowIndex}-${header}-${cellIndex}`}>
              <dt>{header || `Dato ${cellIndex + 1}`}</dt>
              <dd>{row[cellIndex] || '-'}</dd>
            </div>
          ))}
        </div>
      ))}
    </dl>
  );
}

function renderInline(text: string, keyPrefix: string): ReactNode[] {
  const nodes: ReactNode[] = [];
  const source = normalizeInlineText(text);
  const pattern = /(\*\*([^*]+)\*\*|__([^_]+)__|`([^`]+)`)/g;
  let lastIndex = 0;
  let matchIndex = 0;
  let match: RegExpExecArray | null;

  while ((match = pattern.exec(source)) !== null) {
    if (match.index > lastIndex) {
      nodes.push(cleanLooseMarkdown(source.slice(lastIndex, match.index)));
    }

    const boldText = match[2] ?? match[3];
    const codeText = match[4];
    if (boldText) {
      nodes.push(<strong key={`${keyPrefix}-strong-${matchIndex}`}>{cleanLooseMarkdown(boldText)}</strong>);
    } else if (codeText) {
      nodes.push(<code key={`${keyPrefix}-code-${matchIndex}`}>{codeText}</code>);
    }

    lastIndex = pattern.lastIndex;
    matchIndex += 1;
  }

  if (lastIndex < source.length) {
    nodes.push(cleanLooseMarkdown(source.slice(lastIndex)));
  }

  return nodes.map((node, index) => (
    <Fragment key={`${keyPrefix}-inline-${index}`}>{node}</Fragment>
  ));
}

function isMarkdownTableStart(lines: string[], index: number): boolean {
  const header = splitMarkdownRow(lines[index] ?? '');
  return header.length > 1 && isMarkdownTableSeparator(lines[index + 1] ?? '');
}

function isMarkdownTableSeparator(line: string): boolean {
  const cells = splitMarkdownRow(line);
  return cells.length > 1 && cells.every((cell) => TABLE_SEPARATOR_CELL.test(cell.replace(/\s/g, '')));
}

function splitMarkdownRow(line: string): string[] {
  const trimmed = line.trim();
  if (!trimmed.includes('|')) {
    return [];
  }

  return trimmed
    .replace(/^\|/, '')
    .replace(/\|$/, '')
    .split('|')
    .map((cell) => cell.trim());
}

function normalizeCopy(value: string): string {
  return normalizeInlineText(value).replace(/\s+/g, ' ').trim();
}

function normalizeInlineText(value: string): string {
  return value
    .replace(/\u00a0/g, ' ')
    .replace(/^#{1,6}\s+/, '')
    .replace(/\[([^\]]+)\]\([^)]+\)/g, '$1')
    .trim();
}

function cleanPlainText(value: string): string {
  return cleanLooseMarkdown(normalizeInlineText(value)).trim();
}

function cleanLooseMarkdown(value: string): string {
  return value
    .replace(/__([^_]+)__/g, '$1')
    .replace(/\*\*/g, '')
    .replace(/[*`]/g, '');
}
