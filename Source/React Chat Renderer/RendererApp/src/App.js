import './App.css';
import { useState, useEffect, useRef, useCallback } from 'react';
import AutoScrollComponent from './components/autoScroll';
import TranscriptItem from './components/transcriptItem';
import { isElementFullyVisible } from './util';
import { logInfo } from './logging';
import { RENDERER_VERSION } from './version';
import {
    applySnapshot,
    applyUpsert,
    isTranscriptPayload,
    itemOrder
} from './transcriptReducer';

function App() {
    // Two render paths:
    //  - data: legacy WebViewSetMessagesRequestDto { Messages: [...] } (tools-disabled chat)
    //  - transcript: v2 versioned snapshot { Items, Revision, ConversationId } (agentic)
    const [data, setData] = useState(null);
    const [transcript, setTranscript] = useState(null);
    const [selected, setSelected] = useState(null);      // legacy: index, v2: item id
    const [expandedById, setExpandedById] = useState({});
    const [autoScroll, setAutoScroll] = useState(true);
    const chatItemRefs = useRef({});
    const scrolledDownRef = useRef(true);
    const selectedRef = useRef(null);
    const expandedByIdRef = useRef({});

    // Track refs for callbacks that must not go stale inside the (once-registered) listener.
    useEffect(() => { selectedRef.current = selected; }, [selected]);
    useEffect(() => { expandedByIdRef.current = expandedById; }, [expandedById]);

    const toggleExpand = useCallback((itemId) => {
        setExpandedById(prev => ({ ...prev, [itemId]: !prev[itemId] }));
    }, []);

    const scrollToItem = useCallback((sel) => {
        const el = chatItemRefs.current[sel];
        if (el && !isElementFullyVisible(el)) {
            setAutoScroll(false);
            el.scrollIntoView({ behavior: 'smooth', block: 'nearest' });
        }
    }, []);

    const onSelectedChat = useCallback((sel, forward = true) => {
        if (sel === selectedRef.current)
            return;
        setSelected(sel);
        if (window.chrome.webview && forward)
            window.chrome.webview.postMessage({ Name: 'SetSelection', Data: sel });
        scrollToItem(sel);
    }, [scrollToItem]);

    // stable handle for the once-registered listener
    const onSelectedChatRef = useRef(onSelectedChat);
    useEffect(() => { onSelectedChatRef.current = onSelectedChat; }, [onSelectedChat]);

    useEffect(() => {
        const handleMessage = (e) => {
            let obj;
            if (typeof e.data === 'string')
                obj = JSON.parse(e.data);
            else
                obj = e.data;

            if (obj.Name === 'SetMessages') {
                if (isTranscriptPayload(obj.Data)) {
                    setTranscript(prev => applySnapshot(prev, obj.Data));
                } else {
                    setData(obj.Data);
                    setSelected(null);
                }
            } else if (obj.Name === 'UpsertTranscriptItem') {
                setTranscript(prev => applyUpsert(prev, obj.Data));
            } else if (obj.Name === 'HideCaret') {
                setTimeout(() => {
                    document.querySelectorAll('#caret').forEach(el => el.remove());
                }, 1000);
            } else if (obj.Name === 'SetSelection') {
                onSelectedChatRef.current(obj.Data, false);
            }
        };

        if (window.chrome.webview)
            window.chrome.webview.addEventListener('message', handleMessage);
        return () => {
            if (window.chrome.webview)
                window.chrome.webview.removeEventListener('message', handleMessage);
        };
    }, []);

    useEffect(() => {
        const handleScroll = () => {
            const scrolledDownNew =
                (window.innerHeight + window.scrollY) >= document.documentElement.scrollHeight - 5;
            const changed = scrolledDownRef.current !== scrolledDownNew;
            scrolledDownRef.current = scrolledDownNew;
            if (window.chrome.webview && changed)
                window.chrome.webview.postMessage({ Name: 'IsScrollToBottom', Data: scrolledDownNew });
        };
        window.addEventListener('scroll', handleScroll);
        return () => window.removeEventListener('scroll', handleScroll);
    }, []);

    // Prune disclosure entries whose ids left the authoritative snapshot (v2 only).
    useEffect(() => {
        if (!transcript) return;
        const live = new Set(itemOrder(transcript));
        setExpandedById(prev => {
            const stale = Object.keys(prev).filter(id => !live.has(id));
            if (stale.length === 0) return prev;
            const next = { ...prev };
            for (const id of stale) delete next[id];
            return next;
        });
    }, [transcript]);

    const renderV2 = !!transcript;
    const order = renderV2 ? itemOrder(transcript) : (data?.Messages || []);
    const conversationId = transcript?.conversationId;

    const renderItem = (item, key) => {
        const itemId = renderV2 ? item.Id : key;
        const isSelected = renderV2 ? (selected === item.Id) : (selected === key);
        return (
            <TranscriptItem
                key={itemId}
                item={item}
                selected={isSelected}
                expanded={expandedById[itemId]}
                onToggle={() => toggleExpand(itemId)}
                onSelect={() => onSelectedChat(renderV2 ? item.Id : key)}
                forwardRef={el => { chatItemRefs.current[renderV2 ? item.Id : key] = el; }}
                conversationId={conversationId}
                turnId={renderV2 ? item.TurnId : undefined}
            />
        );
    };

    return (
        <div className='App'>
            <AutoScrollComponent autoScroll={autoScroll} setAutoScroll={setAutoScroll}>
                <div className='chat-list'>
                    {renderV2
                        ? order.map(id => renderItem(transcript.items[id], id))
                        : order.map((item, index) => renderItem(item, index))
                    }
                </div>
            </AutoScrollComponent>
            <div className='renderer-version' title='Renderer version'>
                v{RENDERER_VERSION}
            </div>
        </div>
    );
}

const readyPing = { Name: 'Ready' };

if (window.chrome.webview)
    window.chrome.webview.postMessage(readyPing);

logInfo(`renderer v${RENDERER_VERSION} initialized`);

export default App;
