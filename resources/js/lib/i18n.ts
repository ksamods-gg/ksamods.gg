import { usePage } from '@inertiajs/vue3';

// The site's community translations. Every text the site shows is written in English as
// t('English text') in a script, or $t('English text') in a template, and that English text is
// its key. A placeholder is written :name and filled from `replace`:
// t('by :authors', { authors }). The live translations of the page's language arrive once as
// the `translations` shared prop; a text without one shows in English.
export function t(
    text: string,
    replace: Record<string, string | number> = {},
): string {
    const translations = usePage().props.translations as
        | Record<string, string>
        | undefined;
    let result = translations?.[text] ?? text;

    for (const [name, value] of Object.entries(replace)) {
        result = result.replaceAll(`:${name}`, String(value));
    }

    return result;
}
