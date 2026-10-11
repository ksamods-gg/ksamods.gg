import { describe, expect, it } from 'vite-plus/test';
import {
    boreaInstallLink,
    boreaModLink,
    boreaPackLink,
    boreaSupported,
} from './borea';

describe('valid links', () => {
    it('builds each kind', () => {
        expect(boreaModLink('MeasureTools')).toBe('borea://mod/MeasureTools');
        expect(boreaPackLink('beiks-flight-planning-essentials-pack')).toBe(
            'borea://pack/beiks-flight-planning-essentials-pack',
        );
        expect(boreaInstallLink('MeasureTools')).toBe(
            'borea://install/MeasureTools',
        );
        expect(boreaInstallLink('MeasureTools', '1.1.14')).toBe(
            'borea://install/MeasureTools?version=1.1.14',
        );
    });

    it('accepts ids with dots, dashes and underscores, and of 1 and 64 characters', () => {
        for (const id of [
            'ModMenu.ExampleMod',
            'KSA_SeaDragon',
            'KSP-Redux-OPM',
            'a',
            'a'.repeat(64),
        ]) {
            expect(boreaModLink(id)).toBe(`borea://mod/${id}`);
        }
    });

    it('accepts a name that only starts like a reserved one', () => {
        expect(boreaModLink('Corey')).toBe('borea://mod/Corey');
        expect(boreaModLink('COM10')).toBe('borea://mod/COM10');
    });

    it('accepts SemVer pre-releases and build metadata as they are', () => {
        for (const version of [
            '0.10.1-nightly.20261006',
            '1.0.0-rc.1',
            '1.0.0-0',
            '1.0.0+build.007',
            '2.0.0-beta-2+exp.sha.5114f85',
            `${2147483647}.0.0`,
        ]) {
            expect(boreaInstallLink('X', version)).toBe(
                `borea://install/X?version=${version}`,
            );
        }
    });
});

describe('refused links', () => {
    it.each([
        ['empty', ''],
        ['too long', 'a'.repeat(65)],
        ['starting with a dot', '.mod'],
        ['ending with a dash', 'mod-'],
        ['with a slash, which would add a path segment', 'a/b'],
        ['with a space', 'my mod'],
        ['with a fragment', 'mod#x'],
        ['with user info', 'me@mod'],
        ['with a port', 'mod:80'],
        ['with a query', 'mod?x=1'],
        ['with a percent escape', 'mod%20x'],
        ['with non-ASCII', 'módulo'],
        ['reserved', 'CON'],
        ['reserved in any case', 'core'],
        ['reserved before its first dot', 'nul.txt'],
        ['reserved com and lpt ports', 'LPT9'],
    ])('an id that is %s', (_, id) => {
        expect(boreaModLink(id)).toBeNull();
        expect(boreaPackLink(id)).toBeNull();
        expect(boreaInstallLink(id)).toBeNull();
        expect(boreaInstallLink(id, '1.0.0')).toBeNull();
    });

    it.each([
        ['empty', ''],
        ['with a leading v', 'v1.0.0'],
        ['with two numbers', '1.0'],
        ['with four numbers', '1.0.0.0'],
        ['with a leading zero', '01.0.0'],
        ['past an int', '2147483648.0.0'],
        ['with an empty pre-release', '1.0.0-'],
        ['with an empty pre-release identifier', '1.0.0-rc..1'],
        [
            'with a numeric pre-release identifier with a leading zero',
            '1.0.0-01',
        ],
        ['with empty build metadata', '1.0.0+'],
        ['with two build separators', '1.0.0+a+b'],
        ['with an ampersand, a second parameter', '1.0.0&x=1'],
        ['with a fragment', '1.0.0#x'],
        ['with a space', '1.0.0 beta'],
        ['too long for a link', `1.0.0-${'a'.repeat(2048)}`],
    ])('a version that is %s', (_, version) => {
        expect(boreaInstallLink('MeasureTools', version)).toBeNull();
    });
});

describe('platforms', () => {
    it.each([
        [
            'Windows',
            'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36',
            true,
        ],
        [
            'Linux',
            'Mozilla/5.0 (X11; Linux x86_64) Gecko/20100101 Firefox/131.0',
            true,
        ],
        [
            'macOS',
            'Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6) AppleWebKit/605.1.15',
            false,
        ],
        [
            'Android, which reports Linux',
            'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36',
            false,
        ],
        [
            'iOS',
            'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15',
            false,
        ],
    ])('%s', (_, userAgent, supported) => {
        expect(boreaSupported(userAgent)).toBe(supported);
    });
});
