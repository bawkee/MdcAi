import ActivityRow from './activityRow';

// Context activity: exactly the content the model received (framed/source-tagged), never a
// paraphrase. Shows source kind/path, hash, and loaded/replaced/removed/baseline/delta state
// (DSH proposal §7.3 / §8.4).
const ContextDetails = ({ details, title, summary, status, expanded, onToggle }) => {
    const ctx = details?.Context;
    return (
        <ActivityRow
            icon='❯'
            title={ctx?.SourcePath || ctx?.SourceKind || title}
            summary={summary}
            status={status}
            expandable={!!(ctx?.Content)}
            expanded={expanded}
            onToggle={onToggle}>
            {ctx && (
                <div className='context-detail'>
                    {ctx.SourceKind && <div className='context-row'>source: {ctx.SourceKind}</div>}
                    {ctx.SourcePath && <div className='context-row'>path: {ctx.SourcePath}</div>}
                    {ctx.Hash && <div className='context-row'>sha256: {ctx.Hash}</div>}
                    {ctx.State && <div className='context-row'>state: {ctx.State}</div>}
                    {ctx.Content != null && <pre className='context-content'>{ctx.Content}</pre>}
                </div>
            )}
        </ActivityRow>
    );
};

export default ContextDetails;
