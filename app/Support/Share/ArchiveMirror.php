<?php

namespace App\Support\Share;

use App\Models\ShareRelease;
use GuzzleHttp\Psr7\Utils;
use Illuminate\Http\Client\ConnectionException;
use Illuminate\Http\Client\PendingRequest;
use Illuminate\Http\Client\RequestException;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Sleep;
use Illuminate\Support\Str;
use RuntimeException;
use Throwable;

/**
 * Internet Archive mirrors of shared releases (RFC 0094, Mirrors; spec/share-host.md rule 19).
 * Each release becomes an archive.org item of its own through archive.org's S3-like API
 * (https://archive.org/developers/ias3.html), so it stays installable if ksamods.gg stops.
 */
class ArchiveMirror
{
    private const UPLOAD = 'https://s3.us.archive.org/';

    private const DOWNLOAD = 'https://archive.org/download/';

    public static function enabled(): bool
    {
        return filled(config('share.mirror.access_key')) && filled(config('share.mirror.secret_key'));
    }

    /**
     * The item's identifier: lowercase letters, digits, - and _, at most 80 characters, unique
     * per release. The hash keeps two releases apart whose ids differ only in what is folded.
     */
    public static function identifier(ShareRelease $release): string
    {
        $share = $release->share;
        $slug = fn (string $text, int $length) => Str::limit(trim((string) preg_replace('/[^a-z0-9_-]+/', '_', strtolower($text)), '_-'), $length, '');

        return 'ksamods-'.$share->type.'-'.$slug($share->listing_id, 40).'-'.$slug($release->version, 16)
            .'-'.substr(sha1("{$share->type}/{$share->listing_id}/{$release->version}"), 0, 8);
    }

    public static function file(ShareRelease $release): string
    {
        return "{$release->share->listing_id}-{$release->version}.zip";
    }

    public static function url(ShareRelease $release): string
    {
        return self::DOWNLOAD.self::identifier($release).'/'.rawurlencode(self::file($release));
    }

    /**
     * Uploads the release's archive, and returns its download URL once archive.org serves it.
     *
     * @throws RequestException|RuntimeException
     */
    public static function upload(ShareRelease $release): string
    {
        $share = $release->share;
        $stream = DataFiles::disk()->readStream($release->path) ?? throw new RuntimeException("The archive {$release->path} is gone.");
        $kind = $share->type === 'vehicle' ? 'vehicle' : 'save game';

        $metadata = [
            'mediatype' => 'data',
            'collection' => config('share.mirror.collection'),
            'title' => "{$share->name} {$release->version} (Kitten Space Agency {$kind})",
            'description' => "{$share->abstract} A mirror of the {$kind} {$share->listing_id}, version {$release->version}, shared on ksamods.gg: {$share->url()}",
            'subject' => "Kitten Space Agency;{$kind};game content",
            'date' => $release->created_at->toDateString(),
            'licenseurl' => self::licenseUrl($share->license),
            'source' => $share->url(),
        ];

        $headers = [
            'x-archive-auto-make-bucket' => '1',
            // Only the original is wanted; no derived formats.
            'x-archive-queue-derive' => '0',
            'x-archive-size-hint' => (string) $release->size,
            // The stream from the share disk has no known length, and an S3 PUT is not sent in chunks.
            'Content-Length' => (string) $release->size,
            'Content-Type' => 'application/zip',
        ];

        foreach (array_filter($metadata) as $name => $value) {
            $headers["x-archive-meta-{$name}"] = self::header((string) $value);
        }

        foreach ($share->authors as $index => $author) {
            $headers[sprintf('x-archive-meta%02d-creator', $index + 1)] = self::header($author);
        }

        $body = Utils::streamFor($stream);

        try {
            self::s3()->withHeaders($headers)
                ->withBody($body, 'application/zip')
                ->timeout(600)
                ->put(self::identifier($release).'/'.rawurlencode(self::file($release)))
                ->throw();
        } finally {
            $body->close();
        }

        self::waitUntilServed($release);

        return self::url($release);
    }

    /**
     * Removes the mirror of a release that was taken down (RFC 0094, Takedowns).
     *
     * @throws RequestException
     */
    public static function delete(ShareRelease $release): void
    {
        $response = self::s3()->withHeaders(['x-archive-cascade-delete' => '1'])
            ->delete(self::identifier($release).'/'.rawurlencode(self::file($release)));

        if (! $response->notFound()) {
            $response->throw();
        }
    }

    /**
     * archive.org accepts an upload before it serves it. The release list only names the mirror
     * once a download works, since the index checks every mirror's bytes once.
     */
    private static function waitUntilServed(ShareRelease $release): void
    {
        for ($attempt = 1; $attempt <= 40; $attempt++) {
            $response = self::client()->head(self::url($release));

            if ($response->successful() && (int) $response->header('Content-Length') === $release->size) {
                return;
            }

            Sleep::for(15)->seconds();
        }

        throw new RuntimeException('archive.org has not served '.self::url($release).' after 10 minutes.');
    }

    /**
     * A metadata header, in the uri() form archive.org takes for anything but plain ASCII.
     */
    private static function header(string $value): string
    {
        $value = str_replace(["\r", "\n"], ' ', $value);

        return preg_match('/^[\x20-\x7E]*$/', $value) === 1 ? $value : 'uri('.rawurlencode($value).')';
    }

    /**
     * The Creative Commons deed of an SPDX id, when it is one.
     */
    private static function licenseUrl(string $license): ?string
    {
        if ($license === 'CC0-1.0') {
            return 'https://creativecommons.org/publicdomain/zero/1.0/';
        }

        return preg_match('/^CC-(BY(?:-SA|-ND|-NC|-NC-SA|-NC-ND)?)-(\d\.\d)$/', $license, $match) === 1
            ? 'https://creativecommons.org/licenses/'.strtolower($match[1]).'/'.$match[2].'/'
            : null;
    }

    private static function s3(): PendingRequest
    {
        return self::client()->baseUrl(self::UPLOAD)
            ->withHeaders(['Authorization' => 'LOW '.config('share.mirror.access_key').':'.config('share.mirror.secret_key')])
            // archive.org answers 503 SlowDown when busy, and asks clients to back off and retry.
            ->retry(3, 5000, fn (Throwable $error) => $error instanceof ConnectionException
                || ($error instanceof RequestException && in_array($error->response->status(), [429, 500, 502, 503], true)), throw: false);
    }

    /**
     * archive.org asks every automated client to name itself (https://archive.org/developers/bots.html).
     */
    private static function client(): PendingRequest
    {
        return Http::withUserAgent('ksamods.gg/1.0 (+https://ksamods.gg; share host mirror)')->timeout(30);
    }
}
