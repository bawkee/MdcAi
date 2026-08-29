import { useState } from 'react';
import CodeHighlighter from './highlighter';

// Collapsible "thinking" block, dsh-style: a one-liner label showing the last
// line of the model's reasoning (the payload's ReasoningPreview); clicking it
// expands a blockquote-styled container with the whole (pre-rendered HTML)
// reasoning text, clicking again collapses it. Rendered only for messages that
// carry actual reasoning content (models with thinking).
const ThinkingBlock = ({ reasonHtml, preview }) => {
    const [expanded, setExpanded] = useState(false);

    if (!reasonHtml)
        return null;

    return (
        <div className={`thinking-block${expanded ? ' expanded' : ''}`}>
            <button
                type='button'
                className='thinking-toggle'
                aria-expanded={expanded}
                onClick={() => setExpanded(!expanded)}>
                <span className='thinking-chevron'>▸</span>
                <span className='thinking-label'>Thinking</span>
                {preview && <span className='thinking-preview'>{preview}</span>}
            </button>
            {expanded && (
                <div className='thinking-body'>
                    <CodeHighlighter code={reasonHtml} />
                </div>
            )}
        </div>
    );
};

export default ThinkingBlock;