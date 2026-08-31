import { render, screen, fireEvent, act } from '@testing-library/react';
import App from './App';
import { resetWebView, emitWebMessage } from './testUtils';

// Stub the heavy highlighter pipeline like the main App test does.
jest.mock('./components/highlighter', () => {
    const React = require('react');
    return {
        __esModule: true,
        default: ({ code }) => React.createElement('div', { dangerouslySetInnerHTML: { __html: code } })
    };
});

beforeEach(() => resetWebView());
afterEach(() => { document.body.innerHTML = ''; });

const baseItem = (over = {}) => ({
    Id: 'msg-1',
    Kind: 'message',
    Message: {
        Id: 'msg-1',
        Role: 'user',
        Content: '<p>hi</p>',
        Version: 1,
        VersionCount: 1,
        CreatedTs: '2026-01-01T00:00:00',
        Origin: 'human'
    },
    ...over
});

const v2Snapshot = (items, revision = 1) => ({
    Name: 'SetMessages',
    Data: {
        ContractVersion: 2,
        ConversationId: 'c-1',
        Revision: revision,
        Items: items
    }
});

describe('v2 transcript SetMessages', () => {
    it('renders message and thinking + tool activities in order', () => {
        const items = [
            baseItem(),
            {
                Id: 'thinking:ai-1',
                Kind: 'activity',
                Activity: {
                    ActivityKind: 'thinking',
                    PresentationKind: 'thinking',
                    Status: 'completed',
                    Title: 'Thinking',
                    Summary: 'I should read it.',
                    Details: { Version: 1, Kind: 'thinking', Context: { Content: 'I should read it.' } }
                }
            },
            {
                Id: 'message:ai-1',
                Kind: 'message',
                Message: { Id: 'ai-1', Role: 'assistant', Content: '<p>…</p>', Origin: 'model' }
            },
            {
                Id: 'tool:call_1',
                Kind: 'activity',
                Activity: {
                    ActivityKind: 'tool',
                    PresentationKind: 'read',
                    Status: 'completed',
                    Title: 'Read File',
                    Summary: 'file a.txt: hello',
                    ToolCallId: 'call_1',
                    Details: {
                        Version: 1,
                        Kind: 'read',
                        Read: {
                            LocationId: 'loc-42',
                            Path: 'a.txt',
                            Lines: [{ Number: 1, Text: 'hello' }],
                            RetainedLineCount: 1,
                            TotalLineCount: 1,
                            Language: 'txt'
                        }
                    }
                }
            }
        ];

        render(<App />);
        act(() => emitWebMessage(v2Snapshot(items)));

        // Thinking activity row.
        expect(screen.getByText('Thinking')).toBeInTheDocument();
        // Read activity row - summary shows totals.
        expect(screen.getByText('1 of 1 lines')).toBeInTheDocument();
        expect(document.querySelectorAll('.activity-row')).toHaveLength(2);

        // Expand the read activity to reveal line text + copy button.
        fireEvent.click(screen.getByText('Read · a.txt').closest('.activity-row-main'));
        expect(document.querySelector('.read-line-text')).toHaveTextContent('hello');
        expect(screen.getByText('Copy')).toBeInTheDocument();
    });

    it('keeps expansion state across UpsertTranscriptItem replacement', () => {
        const msg = baseItem();
        const toolItem = {
            Id: 'tool:call_1',
            Kind: 'activity',
            Activity: {
                ActivityKind: 'tool',
                PresentationKind: 'generic',
                Title: 'Run Powershell',
                Status: 'running',
                ToolCallId: 'call_1',
                Details: { Version: 1, Kind: 'generic', Generic: { Input: '{"script":"1"}', Output: '' } }
            }
        };

        render(<App />);
        act(() => emitWebMessage(v2Snapshot([msg, toolItem])));

        const row = screen.getByText('Run Powershell').closest('.activity-row-main');
        fireEvent.click(row);
        expect(row).toHaveAttribute('aria-expanded', 'true');
        expect(document.querySelector('.generic-pre')).toHaveTextContent('{"script":"1"}');

        // Replacing the same tool item (a live status update) must preserve expansion.
        const updated = {
            ...toolItem,
            Revision: 2,
            Activity: {
                ...toolItem.Activity,
                Status: 'completed',
                Details: { ...toolItem.Activity.Details, Generic: { ...toolItem.Activity.Details.Generic, Output: 'ok' } }
            }
        };
        act(() => emitWebMessage({
            Name: 'UpsertTranscriptItem',
            Data: { Item: updated, BaseRevision: 1 }
        }));

        const rowAfter = screen.getByText('Run Powershell').closest('.activity-row-main');
        expect(rowAfter).toHaveAttribute('aria-expanded', 'true');
        expect(screen.getByText('ok')).toBeInTheDocument();
    });

    it('ignores stale deltas older than the snapshot revision', () => {
        const msg = baseItem();
        const toolItem = {
            Id: 'tool:call_1',
            Kind: 'activity',
            Activity: { PresentationKind: 'generic', Title: 'Run Powershell', Status: 'completed', Details: { Version: 1, Kind: 'generic' } }
        };

        render(<App />);
        act(() => emitWebMessage(v2Snapshot([msg], 5)));

        // A delta from an older base revision must be dropped.
        act(() => emitWebMessage({
            Name: 'UpsertTranscriptItem',
            Data: { Item: { ...toolItem, Revision: 2 }, BaseRevision: 2 }
        }));

        expect(screen.queryByText('Run Powershell')).toBeNull();
    });

    it('unknown presentation kind renders a bounded generic fallback', () => {
        const items = [baseItem(), {
            Id: 'tool:future',
            Kind: 'activity',
            Activity: {
                PresentationKind: 'some_future_kind',
                Title: 'Future Tool',
                Status: 'completed',
                Details: { Version: 99, Kind: 'some_future_kind', Generic: { Input: 'x', Output: 'y' } }
            }
        }];

        render(<App />);
        act(() => emitWebMessage(v2Snapshot(items)));

        expect(screen.getByText('Future Tool')).toBeInTheDocument();
        const row = screen.getByText('Future Tool').closest('.activity-row-main');
        fireEvent.click(row);
        expect(screen.getByText('x')).toBeInTheDocument();
        expect(screen.getByText('y')).toBeInTheDocument();
    });
});

describe('v2 transcript selection & revision', () => {
    it('selects by stable item id and posts SetSelection with the id', () => {
        const items = [baseItem({ Id: 'message:u', Message: { Id: 'u', Role: 'user', Content: '<p>a</p>' } }),
            baseItem({ Id: 'message:a', Message: { Id: 'a', Role: 'assistant', Content: '<p>b</p>' } })];

        render(<App />);
        act(() => emitWebMessage(v2Snapshot(items)));

        fireEvent.click(screen.getByText('a').closest('.chat-item'));
        // selection posts with the stable id, not an index
        expect(window.chrome.webview.postMessage).toHaveBeenCalledWith({ Name: 'SetSelection', Data: 'message:u' });
    });
});
