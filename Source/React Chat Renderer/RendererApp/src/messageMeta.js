// Small pure helpers for turning a message DTO into display metadata.
// Kept out of App.js so the label logic is trivially unit-testable.

// The role string used for CSS classing. Safe against missing/unknown roles.
export function getRoleClass(role) {
    const normalized = String(role || '').toLowerCase().trim();
    return normalized === 'user' || normalized === 'assistant' || normalized === 'system'
        ? normalized
        : 'unknown';
}

// The author label shown in .chat-item-info-role:
//   user      -> "You"
//   assistant/system -> "<Provider> · <Model>" when we know the model,
//                       falling back to "Assistant"/"System" for legacy payloads
//                       that never carried model info.
export function getAuthorLabel(item) {
    const role = getRoleClass(item?.Role);

    if (role === 'user')
        return 'You';

    const provider = item?.Provider;
    const model = item?.Model;

    if (model)
        return [provider, model].filter(Boolean).join(' · ');

    return role === 'assistant' ? 'Assistant' : 'System';
}

// The reasoning-effort label shown in .chat-item-info-effort:
//   "Effort: <level>" when the message carries per-message effort provenance,
//   null for user messages, effort-less models and legacy payloads (nothing rendered).
export function getEffortLabel(item) {
    const effort = item?.Effort;
    if (!effort || getRoleClass(item?.Role) === 'user')
        return null;
    return `Effort: ${effort}`;
}