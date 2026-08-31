import ActivityRow from './activityRow';

// Thinking activity: only reasoning actually returned by the provider, in a bounded wrapped
// text body. Never synthesize or claim hidden chain-of-thought (DSH proposal §7.3).
const ThinkingDetails = ({ details, title, summary, status, expanded, onToggle }) => {
    const content = details?.Context?.Content || details?.Generic?.Output || '';
    return (
        <ActivityRow
            icon='✳'
            title='Thinking'
            summary={summary}
            status={status}
            expandable={!!content}
            expanded={expanded}
            onToggle={onToggle}>
            {content && <div className='thinking-detail'>{content}</div>}
        </ActivityRow>
    );
};

export default ThinkingDetails;
