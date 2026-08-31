// Shared compact activity row chrome (DSH proposal §7.3): icon, title, dot, one-line
// ellipsized summary, and a non-shrinking right-aligned pill. The whole row is a keyboard
// target when expandable; disclosure state is renderer-local keyed by stable id (the parent
// passes expanded + onToggle so an UpsertTranscriptItem replacement keeps it).
const ActivityRow = ({
    icon = '▪',
    title,
    summary,
    pill,
    status = 'completed',
    expandable = false,
    expanded = false,
    onToggle,
    children
}) => (
    <div className={`activity-row activity-${status || 'completed'}`}>
        <div
            className='activity-row-main'
            role={expandable ? 'button' : undefined}
            aria-expanded={expandable ? expanded : undefined}
            tabIndex={expandable ? 0 : undefined}
            onClick={expandable ? onToggle : undefined}
            onKeyDown={expandable ? (e) => {
                if (e.key === 'Enter' || e.key === ' ')
                    onToggle(e);
            } : undefined}>
            <span className='activity-row-icon' aria-hidden='true'>{icon}</span>
            <span className='activity-row-title'>{title}</span>
            {summary && (
                <>
                    <span className='activity-row-dot' aria-hidden='true'>·</span>
                    <span className='activity-row-summary'>{summary}</span>
                </>
            )}
            {pill && <span className='activity-row-pill'>{pill}</span>}
        </div>
        {expanded && children && (
            <div className='activity-detail'>
                {children}
            </div>
        )}
    </div>
);

export default ActivityRow;
