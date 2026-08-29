import { getAuthorLabel, getEffortLabel, getRoleClass } from './messageMeta';

describe('getRoleClass', () => {
    it('normalizes known roles', () => {
        expect(getRoleClass('user')).toBe('user');
        expect(getRoleClass('assistant')).toBe('assistant');
        expect(getRoleClass('system')).toBe('system');
        expect(getRoleClass('USER')).toBe('user');
        expect(getRoleClass(' Assistant ')).toBe('assistant');
    });

    it('falls back to unknown for missing/weird roles', () => {
        expect(getRoleClass(undefined)).toBe('unknown');
        expect(getRoleClass(null)).toBe('unknown');
        expect(getRoleClass('')).toBe('unknown');
        expect(getRoleClass('tool')).toBe('unknown');
    });
});

describe('getAuthorLabel', () => {
    it('labels user messages as You', () => {
        expect(getAuthorLabel({ Role: 'user', Model: 'gpt-4o', Provider: 'OpenAI' })).toBe('You');
    });

    it('shows provider · model for assistant messages', () => {
        const label = getAuthorLabel({ Role: 'assistant', Model: 'gpt-4o', Provider: 'OpenAI' });
        expect(label).toBe('OpenAI · gpt-4o');
    });

    it('shows the openrouter style id with its provider', () => {
        const label = getAuthorLabel({
            Role: 'assistant',
            Model: 'anthropic/claude-3-5-sonnet',
            Provider: 'OpenRouter'
        });
        expect(label).toBe('OpenRouter · anthropic/claude-3-5-sonnet');
    });

    it('shows just the model when provider is absent', () => {
        expect(getAuthorLabel({ Role: 'assistant', Model: 'deepseek/deepseek-chat' })).toBe(
            'deepseek/deepseek-chat'
        );
    });

    it('falls back to Assistant for legacy assistant payloads without model info', () => {
        expect(getAuthorLabel({ Role: 'assistant' })).toBe('Assistant');
    });

    it('falls back to System for legacy system payloads without model info', () => {
        expect(getAuthorLabel({ Role: 'system' })).toBe('System');
    });

    it('is case-insensitive on the role', () => {
        expect(getAuthorLabel({ Role: 'ASSISTANT', Model: 'gpt-4o', Provider: 'OpenAI' })).toBe(
            'OpenAI · gpt-4o'
        );
    });
});

describe('getEffortLabel', () => {
    it('formats the effort when the message carries per-message effort', () => {
        expect(getEffortLabel({ Role: 'assistant', Effort: 'medium' })).toBe('Effort: medium');
        expect(getEffortLabel({ Role: 'assistant', Effort: 'high' })).toBe('Effort: high');
    });

    it('returns null for user messages and messages without effort', () => {
        expect(getEffortLabel({ Role: 'user', Effort: 'medium' })).toBe(null);
        expect(getEffortLabel({ Role: 'assistant' })).toBe(null);
        expect(getEffortLabel({ Role: 'assistant', Effort: null })).toBe(null);
        expect(getEffortLabel({ Role: 'assistant', Effort: '' })).toBe(null);
        expect(getEffortLabel({})).toBe(null);
        expect(getEffortLabel(undefined)).toBe(null);
    });
});