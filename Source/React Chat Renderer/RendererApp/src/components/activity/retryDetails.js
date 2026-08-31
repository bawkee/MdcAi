import ActivityRow from './activityRow';

// Retry activity: scheduled/started/cancelled/completed state with sanitized failure detail.
// Durable timestamps/state are authoritative; any countdown is render-time (DSH proposal §7.3).
const RetryDetails = ({ details, title, summary, pill, status, expanded, onToggle }) => {
    const retry = details?.Retry;
    return (
        <ActivityRow
            icon='↻'
            title={title || (retry ? `Retry ${retry.AttemptNumber}/${retry.MaxAttempts}` : 'Retry')}
            summary={summary}
            pill={pill}
            status={status}
            expandable={!!(retry?.Reason)}
            expanded={expanded}
            onToggle={onToggle}>
            {retry && (
                <div className='retry-detail'>
                    {retry.FailureCategory && <div className='retry-row'>failure: {retry.FailureCategory}</div>}
                    {retry.DelaySource && <div className='retry-row'>delay source: {retry.DelaySource}</div>}
                    {retry.Reason && <pre className='retry-reason'>{retry.Reason}</pre>}
                    <div className='retry-row'>status: {retry.Status}</div>
                </div>
            )}
        </ActivityRow>
    );
};

export default RetryDetails;
