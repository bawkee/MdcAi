import ActivityRow from './activityRow';

// Compact proposed/applied diff card (DSH proposal §7.3): explicit state (proposed vs applied vs
// failed), file headers, -/+ lines, aggregate footer, copy that excludes chrome.
const DiffDetails = ({ details, title, summary, pill, status, expanded, onToggle, stateOverride }) => {
    const diff = details?.Diff;
    const state = stateOverride || diff?.State || 'proposed';

    const totalFiles = (diff?.FilesModified ?? 0) + (diff?.FilesAdded ?? 0) + (diff?.FilesRemoved ?? 0);
    const hasBody = !!(diff?.Diffs?.length);

    return (
        <ActivityRow
            icon={state === 'failed' ? '✕' : state === 'applied' ? '✓' : '✎'}
            title={diff?.Diffs?.[0]?.Path ? `Edit · ${diff.Diffs[0].Path}` : title}
            summary={state === 'failed' ? (diff?.PreconditionError || summary) : summary}
            pill={pill}
            status={status}
            expandable={hasBody}
            expanded={expanded}
            onToggle={onToggle}>
            {hasBody && (
                <div className='diff-detail'>
                    {diff.Diffs.map((f, i) => (
                        <div className='diff-file' key={`${f.Path}-${i}`}>
                            <div className='diff-file-header'>{f.Path}</div>
                            <pre className='diff-body'>
                                {renderDiff(f, state)}
                            </pre>
                        </div>
                    ))}
                    <div className='diff-footer'>
                        <span className={`diff-state diff-state-${state}`}>{state}</span>
                        <span>└ +{diff.FilesAdded ?? 0} −{diff.FilesRemoved ?? 0} · {totalFiles} file{totalFiles === 1 ? '' : 's'}</span>
                    </div>
                </div>
            )}
        </ActivityRow>
    );
};

function renderDiff(f, state) {
    // Proposed: show the intended replacement. Applied/failed: show old/new if present.
    const oldLines = (f.OldText ?? '').split('\n');
    const newLines = (f.NewText ?? '').split('\n');
    const lines = [];
    for (const l of oldLines)
        if (l.length > 0) lines.push(['-', l]);
    for (const l of newLines)
        if (l.length > 0) lines.push(['+', l]);
    if (!lines.length && state === 'proposed')
        return ''; // nothing to show
    return lines.map(([sign, text]) => `${sign} ${text}`).join('\n');
}

export default DiffDetails;
