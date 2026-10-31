<?php

namespace App\Jobs;

use App\Models\ShareRelease;
use App\Support\Share\ArchiveMirror;
use App\Support\Share\DataFiles;
use App\Support\Share\ShareHost;
use Illuminate\Contracts\Queue\ShouldQueue;
use Illuminate\Foundation\Queue\Queueable;
use Throwable;

/**
 * Lists a published release, in the order of rule 9: its Internet Archive mirror (rule 19),
 * then its release list entry with that mirror in it, then for a new listing or a new
 * dependency list the attestation and pull request. An entry is never edited (rule 10), so it
 * waits for the mirror. Runs from the queue when mirroring, since an upload takes a while, and
 * inline otherwise.
 */
class ListShareRelease implements ShouldQueue
{
    use Queueable;

    public int $tries = 6;

    // The upload, and archive.org taking up to ten minutes to serve it.
    public int $timeout = 1200;

    /** @var list<int> */
    public array $backoff = [60, 300, 900, 1800, 3600];

    public function __construct(public ShareRelease $release, public bool $propose, public bool $new) {}

    public function handle(): void
    {
        self::run($this->release, $this->propose, $this->new);
    }

    /**
     * @return string|null The listing pull request, when one was opened or updated.
     */
    public static function run(ShareRelease $release, bool $propose, bool $new, bool $mirror = true): ?string
    {
        $release->refresh();

        if ($release->taken_down_at !== null || $release->listed_at !== null) {
            return null;
        }

        if ($mirror && ArchiveMirror::enabled() && $release->mirror_url === null) {
            $release->forceFill(['mirror_url' => ArchiveMirror::upload($release)])->save();
        }

        $release->forceFill(['listed_at' => now()])->save();
        DataFiles::writeReleaseList($release->share);

        return $propose ? ShareHost::propose($release->share, $new) : null;
    }

    /**
     * archive.org kept failing: the release is listed without a mirror rather than not at all.
     */
    public function failed(Throwable $error): void
    {
        report($error);
        self::run($this->release, $this->propose, $this->new, mirror: false);
    }
}
