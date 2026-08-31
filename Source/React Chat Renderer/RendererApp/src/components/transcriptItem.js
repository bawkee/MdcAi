import CodeHighlighter from './highlighter';
import ThinkingBlock from './thinkingBlock';
import ActivityDetailsHost from './activity/activityDetailsHost';
import { getAuthorLabel, getEffortLabel, getRoleClass } from '../messageMeta';
import DateTime from './dateTime';
import ApprovalActions from './activity/approvalActions';

// Renders one transcript item: either a message (legacy/ordinary chat surface) or an activity
// (thinking/tool/context/retry/...). Disclosure/nested-fold state is renderer-local keyed by id.
const TranscriptItem = ({
    item,
    selected,
    expanded,
    onToggle,
    onSelect,
    forwardRef,
    conversationId,
    turnId,
}) => {
    if (!item)
        return null;

    if (item.Kind === 'turn_summary') {
        const s = item.TurnSummary || {};
        return (
            <div ref={forwardRef} onClick={onSelect} className='chat-item chat-item-turn-summary'>
                <div className='turn-summary-row'>
                    <span className='activity-row-icon' aria-hidden='true'>∑</span>
                    <span className='activity-row-title'>Turn usage</span>
                    <span className='activity-row-summary'>
                        {s.StepCount} step{s.StepCount === 1 ? '' : 's'} · {s.ToolCallCount} tool call{s.ToolCallCount === 1 ? '' : 's'}
                        {s.PromptTokens != null && <> · {s.PromptTokens} in / {s.CompletionTokens ?? 0} out</>}
                    </span>
                    {s.Outcome && <span className='activity-row-pill'>{s.Outcome}</span>}
                </div>
            </div>
        );
    }

    if (item.Kind === 'activity') {
        const activity = item.Activity || {};
        const isApproval = activity.Status === 'awaiting_approval' && activity.ToolCallId;
        return (
            <div
                ref={forwardRef}
                onClick={onSelect}
                className={`chat-item chat-item-activity ${selected ? 'active' : ''}`}>
                <ActivityDetailsHost
                    activity={activity}
                    expanded={expanded}
                    onToggle={onToggle} />
                {isApproval && (
                    <ApprovalActions activity={activity} conversationId={conversationId} turnId={turnId} />
                )}
            </div>
        );
    }

    // message item (or legacy)
    const msg = item.Message || item;
    return (
        <div
            ref={forwardRef}
            onClick={onSelect}
            className={`chat-item ${getRoleClass(msg.Role)} ${selected ? 'active' : ''}`}>
            <div className='chat-item-marque' />
            <div className='chat-item-info'>
                <span className='chat-item-info-role'>{getAuthorLabel(msg)}</span>
                {getEffortLabel(msg) && <span className='chat-item-info-effort'>{getEffortLabel(msg)}</span>}
                <span className='chat-item-info-createdts'>
                    sent <DateTime date={new Date(msg.CreatedTs)} />
                </span>
                {(msg.VersionCount || 0) > 1 && (
                    <span className='chat-item-info-version' title='Version of the edited message'>
                        {msg.Version} / {msg.VersionCount}
                    </span>
                )}
                {(msg.CompletionState === 'interrupted') && (
                    <span className='chat-item-info-interrupted'>interrupted</span>
                )}
            </div>
            <div className='chat-item-content'>
                <ThinkingBlock reasonHtml={msg.Reasoning} preview={msg.ReasoningPreview} />
                <CodeHighlighter code={msg.Content} />
            </div>
        </div>
    );
};

export default TranscriptItem;
