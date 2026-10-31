import { describe, expect, it, vi } from 'vite-plus/test';
import { t } from './i18n';

vi.mock('@inertiajs/vue3', () => ({
    usePage: () => ({
        props: {
            translations: { 'by :authors': 'von :authors', Mods: 'Mods (de)' },
        },
    }),
}));

describe('t', () => {
    it('shows the live translation, with its placeholders filled', () => {
        expect(t('Mods')).toBe('Mods (de)');
        expect(t('by :authors', { authors: 'Maxi' })).toBe('von Maxi');
    });

    it('shows the English text when there is no translation', () => {
        expect(t('Log in')).toBe('Log in');
        expect(t(':count mods', { count: 3 })).toBe('3 mods');
    });
});
