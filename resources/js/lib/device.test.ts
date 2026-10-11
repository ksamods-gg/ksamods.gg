import { describe, expect, it } from 'vite-plus/test';
import { isMobileDevice } from './device';

describe('isMobileDevice', () => {
    it.each([
        [
            'an iPhone',
            'Mozilla/5.0 (iPhone; CPU iPhone OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148 Safari/604.1',
            true,
        ],
        [
            'an iPad in mobile mode',
            'Mozilla/5.0 (iPad; CPU OS 18_0 like Mac OS X) AppleWebKit/605.1.15 Mobile/15E148',
            true,
        ],
        [
            'an Android phone',
            'Mozilla/5.0 (Linux; Android 14; Pixel 8) AppleWebKit/537.36 Chrome/131.0 Mobile Safari/537.36',
            true,
        ],
        [
            'an Android tablet',
            'Mozilla/5.0 (Linux; Android 14; SM-X710) AppleWebKit/537.36 Chrome/131.0 Safari/537.36',
            true,
        ],
        [
            'Firefox on Android',
            'Mozilla/5.0 (Android 14; Mobile; rv:131.0) Gecko/131.0 Firefox/131.0',
            true,
        ],
        [
            'Windows',
            'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/131.0 Safari/537.36',
            false,
        ],
        [
            'Linux',
            'Mozilla/5.0 (X11; Linux x86_64) Gecko/20100101 Firefox/131.0',
            false,
        ],
        [
            'macOS',
            'Mozilla/5.0 (Macintosh; Intel Mac OS X 14_6) AppleWebKit/605.1.15 Version/18.0 Safari/605.1.15',
            false,
        ],
        ['a server without a browser', '', false],
    ])('%s', (_, userAgent, mobile) => {
        expect(isMobileDevice(userAgent)).toBe(mobile);
    });
});
