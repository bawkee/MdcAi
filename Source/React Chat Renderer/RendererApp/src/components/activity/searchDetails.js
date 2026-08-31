import ActivityRow from './activityRow';

// Search (grep/glob) card: grouped by file with collapsible groups, line numbers, retained/total
// counts, pre-cap total (DSH proposal §7.3).
const SearchDetails = ({ details, title, summary, pill, status, expanded, onToggle }) => {
    const search = details?.Search;
    const files = search?.Files || [];
    return (
        <ActivityRow
            icon='⌕'
            title={`Search · ${search?.Query || ''}`}
            summary={search?.TotalMatches != null
                ? `${search.TotalMatches} matches in ${files.length} file${files.length === 1 ? '' : 's'}`
                : summary}
            pill={pill || (search?.Truncated ? 'truncated' : undefined)}
            status={status}
            expandable={files.length > 0}
            expanded={expanded}
            onToggle={onToggle}>
            {files.length > 0 && (
                <div className='search-detail'>
                    {files.slice(0, 6).map((file, fi) => (
                        <details className='search-file' key={`${file.Path}-${fi}`} open={fi === 0}>
                            <summary className='search-file-label'>
                                {file.Path} · {file.Matches?.length ?? 0}
                            </summary>
                            <div className='search-matches'>
                                {(file.Matches || []).slice(0, 12).map((m, mi) => (
                                    <div className='search-match' key={`${file.Path}-${m.LineNumber}-${mi}`}>
                                        <span className='search-line-no'>{m.LineNumber}</span>
                                        <span className='search-line-text'>{m.Text}</span>
                                    </div>
                                ))}
                                {(file.Matches?.length || 0) > 12 && (
                                    <div className='search-omitted'>… more matches in this file</div>
                                )}
                            </div>
                        </details>
                    ))}
                    {files.length > 6 && <div className='search-omitted'>… {files.length - 6} more files</div>}
                </div>
            )}
        </ActivityRow>
    );
};

export default SearchDetails;
