import ActivityRow from './activityRow';

// Bounded generic fallback: plain input/output text/JSON. Unknown or future presentation
// kinds must degrade here - never break the transcript (DSH proposal §7.2).
const GenericDetails = ({ details, title, summary, status, pill, expanded, onToggle }) => {
    const gen = details?.Generic || {};
    return (
        <ActivityRow
            icon='▤'
            title={title}
            summary={summary}
            pill={pill}
            status={status}
            expandable={!!(gen.Input || gen.Output)}
            expanded={expanded}
            onToggle={onToggle}>
            {(gen.Input || gen.Output) && (
                <div className='generic-detail'>
                    {gen.Input != null && (
                        <div className='generic-input'>
                            <div className='generic-label'>Input</div>
                            <pre className='generic-pre'>{gen.Input}</pre>
                        </div>
                    )}
                    {gen.Output != null && (
                        <div className='generic-output'>
                            <div className='generic-label'>Output</div>
                            <pre className='generic-pre'>{gen.Output}</pre>
                        </div>
                    )}
                </div>
            )}
        </ActivityRow>
    );
};

export default GenericDetails;
