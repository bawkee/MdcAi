import { useState } from 'react';

// Inline approval actions: visible only for the exact pending call, disable immediately on
// click, and post approve/deny with conversation/turn/tool-call id + argument hash back to the
// host (DSH proposal §7.6). Uncontrolled here - the host validates and completes the pending
// request exactly once.
const ApprovalActions = ({ activity, conversationId, turnId }) => {
    const [clicked, setClicked] = useState(null);

    const post = (decision) => {
        setClicked(decision);
        if (window.chrome?.webview) {
            window.chrome.webview.postMessage({
                Name: decision === 'approve' ? 'ApproveToolCall' : 'DenyToolCall',
                Data: {
                    ConversationId: conversationId,
                    TurnId: turnId || activity.TurnId,
                    ToolCallId: activity.ToolCallId,
                    ArgumentHash: activity.ArgumentHash
                }
            });
        }
    };

    return (
        <div className='approval-actions'>
            <button
                type='button'
                className='approval-approve'
                disabled={clicked !== null}
                onClick={() => post('approve')}>
                Approve
            </button>
            <button
                type='button'
                className='approval-deny'
                disabled={clicked !== null}
                onClick={() => post('deny')}>
                Deny
            </button>
        </div>
    );
};

export default ApprovalActions;
