// Links that open Borea, the KSA content manager. They mirror what Borea's parser accepts
// (src/Borea.Core/Links/BoreaLink.cs) and return null for anything it would refuse.

export const BOREA_RELEASES_URL =
    'https://github.com/KSAModding/Borea/releases';

const MAX_LENGTH = 2048;

// Borea's ModIds: 1 to 64 ASCII letters, digits, '.', '_' or '-', first and last a letter or digit.
const ID = /^[A-Za-z0-9](?:[A-Za-z0-9._-]{0,62}[A-Za-z0-9])?$/;

// Compared without case to the part of the id before its first dot.
const RESERVED = new Set([
    'core',
    'con',
    'prn',
    'aux',
    'nul',
    ...[1, 2, 3, 4, 5, 6, 7, 8, 9].flatMap((n) => [`com${n}`, `lpt${n}`]),
]);

// Borea's ModVersion: strict SemVer, each core number without leading zeros and within an int.
const CORE = /^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)$/;
const IDENTIFIERS = /^[0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*$/;
const INT_MAX = 2147483647;

export function isValidBoreaId(id: string): boolean {
    return ID.test(id) && !RESERVED.has(id.split('.')[0].toLowerCase());
}

export function isValidBoreaVersion(version: string): boolean {
    const [rest, build, ...extra] = version.split('+');

    if (extra.length > 0 || (build !== undefined && !IDENTIFIERS.test(build))) {
        return false;
    }

    const dash = rest.indexOf('-');
    const core = dash < 0 ? rest : rest.slice(0, dash);
    const preRelease = dash < 0 ? undefined : rest.slice(dash + 1);
    const numbers = CORE.exec(core);

    return (
        numbers !== null &&
        numbers.slice(1).every((n) => Number(n) <= INT_MAX) &&
        (preRelease === undefined ||
            (IDENTIFIERS.test(preRelease) &&
                // A numeric pre-release identifier has no leading zero.
                !preRelease.split('.').some((part) => /^0\d+$/.test(part))))
    );
}

// Every valid id and version is already unreserved ASCII, so neither needs percent-encoding.
function link(path: string): string | null {
    return path.length <= MAX_LENGTH ? `borea://${path}` : null;
}

export function boreaModLink(id: string): string | null {
    return isValidBoreaId(id) ? link(`mod/${id}`) : null;
}

export function boreaPackLink(id: string): string | null {
    return isValidBoreaId(id) ? link(`pack/${id}`) : null;
}

export function boreaInstallLink(id: string, version?: string): string | null {
    if (!isValidBoreaId(id)) {
        return null;
    }

    if (version === undefined) {
        return link(`install/${id}`);
    }

    return isValidBoreaVersion(version)
        ? link(`install/${id}?version=${version}`)
        : null;
}

// Borea registers its scheme on Windows and Linux only. Android reports Linux, but runs no Borea.
export function boreaSupported(userAgent = navigator.userAgent): boolean {
    return /Windows|Linux/.test(userAgent) && !/Android/.test(userAgent);
}
