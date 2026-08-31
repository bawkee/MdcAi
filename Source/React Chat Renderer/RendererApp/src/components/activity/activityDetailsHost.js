import GenericDetails from './genericDetails';
import ReadDetails from './readDetails';
import SearchDetails from './searchDetails';
import TerminalDetails from './terminalDetails';
import DiffDetails from './diffDetails';
import RetryDetails from './retryDetails';
import ThinkingDetails from './thinkingDetails';
import ContextDetails from './contextDetails';

// Dispatches on the closed PresentationKind vocabulary (DSH proposal §7.4). The default
// always renders a bounded generic input/output view, so one missing registration can never
// hide a call. Tool wire names select presenters on the C# side, not here.
const ActivityDetailsHost = ({
    activity,
    expanded,
    onToggle,
}) => {
    const kind = activity?.PresentationKind || 'generic';
    const common = {
        details: activity.Details,
        title: activity.Title,
        summary: activity.Summary,
        pill: activity.Pill,
        status: activity.Status,
        expanded,
        onToggle
    };

    switch (kind) {
        case 'read':
            return <ReadDetails {...common} />;
        case 'search':
            return <SearchDetails {...common} />;
        case 'terminal':
            return <TerminalDetails {...common} />;
        case 'diff':
            return <DiffDetails {...common} />;
        case 'thinking':
            return <ThinkingDetails {...common} />;
        case 'context':
            return <ContextDetails {...common} />;
        case 'retry':
            return <RetryDetails {...common} />;
        default:
            return <GenericDetails {...common} />;
    }
};

export default ActivityDetailsHost;
