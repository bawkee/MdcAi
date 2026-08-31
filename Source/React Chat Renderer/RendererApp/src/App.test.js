import { render, screen, fireEvent, act } from '@testing-library/react';
import App from './App';
import { mockMessages, setMessagesPayload, hideCaretPayload } from './mockData';
import { getWebView, resetWebView, emitWebMessage } from './testUtils';

// CodeHighlighter pulls in starry-night (top-level await) + DOMParser + clipboard;
// that pipeline is heavy and irrelevant to App-level behaviour, so stub it out.
// The markup it normally produces is already present in the mock Content fields.
jest.mock('./components/highlighter', () => {
    const React = require('react');
    return {
        __esModule: true,
        default: ({ code }) => React.createElement('div', { dangerouslySetInnerHTML: { __html: code } })
    };
});

// App posts { Name: "Ready" } at import time (module side effect) — capture it
// before beforeEach wipes recorded calls.
const moduleLoadCalls = getWebView().postMessage.mock.calls.slice();

beforeEach(() => {
    resetWebView();
});

afterEach(() => {
    document.body.innerHTML = '';
});

describe('host handshake', () => {
    it('posts Ready when the app module loads', () => {
        // postMessage.mock.calls is an array of call-argument arrays. The module load also
        // emits a diagnostic LogInfo, so filter to the Ready handshake specifically.
        const readyCalls = moduleLoadCalls.filter(call => call[0].Name === 'Ready');
        expect(readyCalls).toEqual([[{ Name: 'Ready' }]]);
    });
});

describe('rendering SetMessages', () => {
    it('labels user vs assistant (provider · model) vs legacy system', () => {
        render(<App />);
        act(() => emitWebMessage(setMessagesPayload()));

        // Two user messages
        expect(screen.getAllByText('You')).toHaveLength(2);

        // Assistant messages carry their provider/model identity
        expect(screen.getByText('OpenAI · gpt-4o')).toBeInTheDocument();
        expect(screen.getByText('OpenRouter · anthropic/claude-3-5-sonnet')).toBeInTheDocument();
        expect(screen.getByText('deepseek/deepseek-chat')).toBeInTheDocument();

        // Legacy system role (no model info) falls back to a plain System label
        expect(screen.getByText('System')).toBeInTheDocument();

        // CSS role classes are applied for styling
        expect(document.querySelectorAll('.chat-item.user')).toHaveLength(2);
        expect(document.querySelectorAll('.chat-item.assistant')).toHaveLength(4);
        expect(document.querySelector('.chat-item.system')).toBeTruthy();
    });

    it('handles string-encoded SetMessages exactly like real PostWebMessageAsJson', () => {
        render(<App />);
        act(() =>
            emitWebMessage(JSON.stringify(setMessagesPayload([mockMessages[0], mockMessages[1]])))
        );

        expect(screen.getByText('You')).toBeInTheDocument();
        expect(screen.getByText('OpenAI · gpt-4o')).toBeInTheDocument();
    });

    it('renders with no messages when Data is empty', () => {
        render(<App />);
        act(() => emitWebMessage(setMessagesPayload([])));

        expect(document.querySelectorAll('.chat-item')).toHaveLength(0);
    });

    it('shows the version badge only for multi-version messages', () => {
        render(<App />);
        act(() => emitWebMessage(setMessagesPayload()));

        const badge = screen.getByText('2 / 2');
        expect(badge).toBeInTheDocument();
        expect(badge).toHaveAttribute('title', 'Version of the edited message');
        // Single-version messages render no badge at all
        expect(screen.queryByText('1 / 1')).toBeNull();
        expect(screen.queryByText('1 / 2')).toBeNull();
    });
});

describe('thinking block (reasoning rendering)', () => {
    it('collapsed by default: shows the Thinking label + one-line preview, no body', () => {
        render(<App />);
        act(() => emitWebMessage(setMessagesPayload()));

        // The reasoning-bearing message renders a Thinking toggle
        const toggles = screen.getAllByText('Thinking');
        expect(toggles.length).toBeGreaterThan(0);

        // One-liner preview is visible; the full reasoning body is NOT
        expect(screen.getByText('The answer is the left door.')).toBeInTheDocument();
        expect(document.querySelector('.thinking-body')).toBeNull();
    });

    it('expands on click and collapses again', () => {
        render(<App />);
        act(() => emitWebMessage(setMessagesPayload()));

        const toggle = screen.getByText('The answer is the left door.').closest('.thinking-toggle');
        expect(toggle).toHaveAttribute('aria-expanded', 'false');

        // Expand: the whole reasoning body appears (text lives in nested <p>s, so
        // assert on the container's text content, not getByText)
        fireEvent.click(toggle);
        expect(toggle).toHaveAttribute('aria-expanded', 'true');
        const body = document.querySelector('.thinking-block.expanded .thinking-body');
        expect(body).toBeTruthy();
        expect(body.textContent).toContain('First I need to parse the riddle carefully.');

        // Collapse: body gone again, preview back
        fireEvent.click(toggle);
        expect(toggle).toHaveAttribute('aria-expanded', 'false');
        expect(document.querySelector('.thinking-body')).toBeNull();
    });

    it('renders nothing when a message carries no reasoning', () => {
        render(<App />);
        act(() => emitWebMessage(setMessagesPayload([mockMessages[0], mockMessages[1]])));

        // These two (user + plain assistant) have no Reasoning/ReasoningPreview
        expect(screen.queryByText('Thinking')).toBeNull();
    });

    it('treats null preview gracefully (label only)', () => {
        render(<App />);
        act(() => emitWebMessage(setMessagesPayload([{
            ...mockMessages[1],
            Reasoning: '<p>just thinking...</p>',
            ReasoningPreview: null
        }])));

        const toggle = screen.getByText('Thinking').closest('.thinking-toggle');
        expect(toggle).toBeTruthy();
        fireEvent.click(toggle);
        const body = document.querySelector('.thinking-body');
        expect(body).toBeTruthy();
        expect(body.textContent).toContain('just thinking...');
    });
});

describe('UI interactions', () => {
    it('clicking a chat item posts SetSelection and marks it active', () => {
        render(<App />);
        act(() => emitWebMessage(setMessagesPayload()));

        fireEvent.click(screen.getByText('OpenAI · gpt-4o').closest('.chat-item'));

        expect(getWebView().postMessage).toHaveBeenCalledWith({ Name: 'SetSelection', Data: 1 });
        expect(document.querySelectorAll('.chat-item')[1]).toHaveClass('active');
        expect(document.querySelectorAll('.chat-item')[0]).not.toHaveClass('active');
    });

    it('does not re-post SetSelection when clicking the already-selected message', () => {
        render(<App />);
        act(() => emitWebMessage(setMessagesPayload()));

        const itemOne = screen.getByText('OpenAI · gpt-4o').closest('.chat-item');
        fireEvent.click(itemOne);

        const callsAfterFirst = getWebView().postMessage.mock.calls.length;

        // Same message again: selection unchanged -> no redundant host round-trip
        fireEvent.click(itemOne);
        expect(getWebView().postMessage.mock.calls.length).toBe(callsAfterFirst);

        // And the selection is still marked active
        expect(itemOne).toHaveClass('active');
    });

    it('posts IsScrollToBottom when the scroll position changes state', () => {
        render(<App />);

        Object.defineProperty(window, 'scrollY', { value: 100, configurable: true, writable: true });
        Object.defineProperty(document.documentElement, 'scrollHeight', {
            value: 5000,
            configurable: true
        });

        // First change: scrolled down -> not at bottom
        act(() => window.dispatchEvent(new Event('scroll')));
        expect(getWebView().postMessage).toHaveBeenCalledWith({
            Name: 'IsScrollToBottom',
            Data: false
        });

        const callCount = getWebView().postMessage.mock.calls.length;

        // Same position again: no spam
        act(() => window.dispatchEvent(new Event('scroll')));
        expect(getWebView().postMessage.mock.calls.length).toBe(callCount);

        // Back at the bottom: posts the change
        Object.defineProperty(window, 'scrollY', { value: 0, configurable: true, writable: true });
        Object.defineProperty(document.documentElement, 'scrollHeight', {
            value: 768,
            configurable: true
        });
        act(() => window.dispatchEvent(new Event('scroll')));
        expect(getWebView().postMessage).toHaveBeenCalledWith({
            Name: 'IsScrollToBottom',
            Data: true
        });
    });

    it('HideCaret removes #caret markers after the delay', () => {
        jest.useFakeTimers();
        try {
            render(<App />);

            const caret = document.createElement('span');
            caret.id = 'caret';
            document.body.appendChild(caret);

            act(() => emitWebMessage(hideCaretPayload()));

            // The caret is still around before the 1s delay elapses
            expect(document.getElementById('caret')).toBeTruthy();

            act(() => jest.advanceTimersByTime(1100));

            expect(document.getElementById('caret')).toBeNull();
        } finally {
            jest.useRealTimers();
        }
    });
});