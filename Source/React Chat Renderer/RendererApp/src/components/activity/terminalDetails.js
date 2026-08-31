import ActivityRow from './activityRow';

// Terminal card: exact script, cwd, running/exit/signal dot, separate stdout/stderr, ANSI as
// safe text (never injected), sticky exit pill, truncation note (DSH proposal §7.3).
const TerminalDetails = ({ details, title, summary, pill, status, expanded, onToggle }) => {
    const term = details?.Terminal;
    const hasOutput = !!(term?.Stdout || term?.Stderr);
    const running = term?.Running;

    return (
        <ActivityRow
            icon='>_'
            title={term?.Script ? `Pwsh · ${firstLine(term.Script)}` : title}
            summary={running ? 'running…' : summary}
            pill={pill || (term?.ExitCode != null ? `exit ${term.ExitCode}` : undefined)}
            status={running ? 'running' : status}
            expandable={hasOutput}
            expanded={expanded}
            onToggle={onToggle}>
            {hasOutput && (
                <div className='terminal-detail'>
                    {term.Script && <pre className='terminal-script'>{term.Script}</pre>}
                    {term.WorkingDirectory && <div className='terminal-cwd'>cwd: {term.WorkingDirectory}</div>}
                    {term.Stdout && <pre className='terminal-stdout'>{term.Stdout}</pre>}
                    {term.Stderr && <pre className='terminal-stderr'>{term.Stderr}</pre>}
                    {term.Truncated && <div className='terminal-truncated'>output truncated</div>}
                </div>
            )}
        </ActivityRow>
    );
};

const firstLine = (s) => (s || '').split('\n')[0];

export default TerminalDetails;
