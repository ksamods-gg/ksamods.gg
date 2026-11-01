import { ref } from 'vue';

// The visitor's cookie choices, kept in a cookie of their own for six months. Strictly necessary
// cookies need no consent; every other category starts off and stays off until the visitor turns
// it on. Changing the choice later is the footer's "Cookie settings".

export type Consent = { statistics: boolean };

const COOKIE = 'ksamods_consent';
const MAX_AGE = 60 * 60 * 24 * 182;

// Null until the visitor chose, and on the server, which never knows the choice.
export const consent = ref<Consent | null>(null);

// The banner, reopened by the footer's "Cookie settings".
export const consentOpen = ref(false);

export function readConsent(): Consent | null {
    const match = document.cookie.match(/(?:^|; )ksamods_consent=([^;]*)/);

    if (!match) {
        return null;
    }

    try {
        const value = JSON.parse(decodeURIComponent(match[1]));

        return { statistics: value.statistics === true };
    } catch {
        return null;
    }
}

export function saveConsent(choice: Consent): void {
    const withdrawn = consent.value?.statistics && !choice.statistics;

    document.cookie = `${COOKIE}=${encodeURIComponent(JSON.stringify(choice))}; Max-Age=${MAX_AGE}; Path=/; SameSite=Lax${location.protocol === 'https:' ? '; Secure' : ''}`;
    consent.value = choice;

    // Withdrawn: Google Analytics' cookies go, and a reload unloads its script.
    if (withdrawn) {
        for (const name of document.cookie
            .split('; ')
            .map((c) => c.split('=')[0])) {
            if (name === '_ga' || name.startsWith('_ga_') || name === '_gid') {
                for (const domain of ['', `; Domain=.${location.hostname}`]) {
                    document.cookie = `${name}=; Max-Age=0; Path=/${domain}`;
                }
            }
        }

        location.reload();
    }
}

// Loads Google Analytics, once, after the visitor allowed statistics. Page views of Inertia's
// navigations are counted by GA4's history-change measurement.
let googleLoaded = false;

export function loadGoogleAnalytics(id: string): void {
    if (googleLoaded) {
        return;
    }

    googleLoaded = true;
    const w = window as unknown as {
        dataLayer: unknown[];
        gtag: (...args: unknown[]) => void;
    };
    w.dataLayer = w.dataLayer || [];
    w.gtag = function () {
        // gtag queues its arguments object itself, as Google's snippet does.
        // eslint-disable-next-line prefer-rest-params
        w.dataLayer.push(arguments);
    };
    w.gtag('js', new Date());
    w.gtag('config', id);

    const script = document.createElement('script');
    script.async = true;
    script.src = `https://www.googletagmanager.com/gtag/js?id=${encodeURIComponent(id)}`;
    document.head.appendChild(script);
}
