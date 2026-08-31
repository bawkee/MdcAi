// Versioned transcript reducer (DSH proposal §7.5). The host sends:
//  - v2 "SetMessages": { ContractVersion, ConversationId, Revision, Items[] } — authoritative
//    snapshot, resetting state.
//  - v2 "UpsertTranscriptItem": { ConversationId, BaseRevision, Item } — add/replace one item by
//    stable id; ignored when its revision is older than the current snapshot revision.
// Legacy "SetMessages" ({ Messages: [...] }) still works and is normalized to items.
//
// Disclosure/selection state is keyed by stable item id so streaming replaces never reset it.

const snapshotKey = (data) => `${data?.ConversationId ?? 'c'}@${data?.Revision ?? 0}`;

// Normalize a (possibly legacy) SetMessages payload into the items array.
export function normalizeSetMessages(data) {
    if (!data)
        return { items: [], revision: 0 };

    if (Array.isArray(data.Items))
        return { items: data.Items, revision: data.Revision ?? 0 };

    // Legacy: Messages: [WebViewChatMessageDto] -> message items, flatten reasoning into
    // a thinking item for reasoning-bearing assistant messages (mirrors the C# projector).
    const legacyItems = [];
    for (const m of (data.Messages || [])) {
        legacyItems.push({
            Id: `message:${m.Id}`,
            Kind: 'message',
            Revision: 0,
            Message: m
        });
    }
    return { items: legacyItems, revision: 0 };
}

// Merge a full snapshot into the current state. Returns a NEW state dict keyed by item id.
export function applySnapshot(state, data) {
    const { items } = normalizeSetMessages(data);
    const next = {};
    for (const item of items)
        next[item.Id] = item;
    return {
        items: next,
        order: items.map(i => i.Id),
        revision: data?.Revision ?? 0,
        conversationId: data?.ConversationId,
        key: snapshotKey(data)
    };
}

// Merges one upserted item (id may be new or replace an existing one).
export function applyUpsert(state, payload) {
    if (!payload || !payload.Item || !payload.Item.Id)
        return state;

    const item = payload.Item;
    const itemRev = item.Revision ?? 0;

    // Stale delta: the item's revision is older than the current snapshot revision,
    // or it was based on an older snapshot than what the renderer already holds.
    // A late streaming callback must never resurrect a node after a fresh snapshot.
    if (itemRev < state.revision)
        return state;
    if ((payload.BaseRevision ?? 0) < state.revision)
        return state;

    const items = { ...state.items, [item.Id]: item };
    const order = state.order.includes(item.Id)
        ? state.order
        : [...state.order, item.Id];

    return {
        items,
        order,
        revision: Math.max(state.revision, payload.BaseRevision ?? 0, itemRev),
        conversationId: state.conversationId,
        key: state.key
    };
}

// Truthy when the payload carries the v2 transcript contract.
export function isTranscriptPayload(data) {
    return data && Array.isArray(data.Items);
}

// Post-setup: the renderer's stable id list for selection/disclosure cleanup.
export function itemOrder(state) {
    return state?.order ?? [];
}

const transcriptReducer = { applySnapshot, applyUpsert, normalizeSetMessages, isTranscriptPayload, itemOrder };

export default transcriptReducer;
