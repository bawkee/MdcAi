import ActivityRow from './activityRow';

// Read card: exact line numbers/totals, language hint, copy, head/tail folding (DSH proposal §7.3).
const VIEWER_MAX = 8;

const ReadDetails = ({ details, title, summary, pill, status, expanded, onToggle }) => {
    const read = details?.Read;
    return (
        <ActivityRow
            icon='≡'
            title={read?.Path ? `Read · ${read.Path}` : title}
            summary={read?.TotalLineCount != null
                ? `${read.RetainedLineCount ?? read.Lines?.length ?? 0} of ${read.TotalLineCount} lines`
                : summary}
            pill={pill || (read?.Truncated ? 'truncated' : undefined)}
            status={status}
            expandable={!!(read?.Lines?.length)}
            expanded={expanded}
            onToggle={onToggle}>
            {read?.Lines?.length > 0 && (
                <div className='read-detail'>
                    <div className='read-meta'>
                        <span className='read-path'>{read.Path}</span>
                        {read.Language && <span className='read-lang'>{read.Language}</span>}
                        <button type='button' className='copy-btn'
                            onClick={() => navigator.clipboard?.writeText(
                                read.Lines.map(l => `${l.Number}: ${l.Text}`).join('\n'))}>
                            Copy
                        </button>
                    </div>
                    <div className='read-lines'>
                        {read.Lines.slice(0, VIEWER_MAX).map((l, i) => (
                            <div className='read-line' key={`${read.LocationId}-${l.Number}-${i}`}>
                                <span className='read-line-number'>{l.Number}</span>
                                <span className='read-line-text'>{l.Text}</span>
                            </div>
                        ))}
                        {(read.RetainedLineCount ?? read.Lines.length) > VIEWER_MAX && (
                            <div className='read-omitted'>
                                … {(read.RetainedLineCount ?? read.Lines.length) - VIEWER_MAX} more (tail omitted)
                            </div>
                        )}
                    </div>
                </div>
            )}
        </ActivityRow>
    );
};

export default ReadDetails;
