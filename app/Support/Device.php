<?php

namespace App\Support;

/**
 * A browser, named from its user agent well enough for a person to recognise it: "Firefox on
 * Windows". Not for anything that must be exact.
 */
class Device
{
    public static function describe(?string $userAgent): string
    {
        $agent = (string) $userAgent;

        $browser = match (true) {
            str_contains($agent, 'Edg/') => 'Edge',
            str_contains($agent, 'OPR/') || str_contains($agent, 'Opera') => 'Opera',
            str_contains($agent, 'Firefox/') => 'Firefox',
            str_contains($agent, 'Chrome/') => 'Chrome',
            str_contains($agent, 'Safari/') => 'Safari',
            default => 'A browser',
        };

        $system = match (true) {
            str_contains($agent, 'Android') => 'Android',
            str_contains($agent, 'iPhone') || str_contains($agent, 'iPad') => 'iOS',
            str_contains($agent, 'Windows') => 'Windows',
            str_contains($agent, 'Mac OS X') || str_contains($agent, 'Macintosh') => 'macOS',
            str_contains($agent, 'Linux') => 'Linux',
            default => null,
        };

        return $system === null ? $browser : "{$browser} on {$system}";
    }

    /**
     * What counts as the same device: the browser and system, so a browser update is not new.
     */
    public static function fingerprint(?string $userAgent): string
    {
        return substr(sha1(self::describe($userAgent)), 0, 16);
    }
}
