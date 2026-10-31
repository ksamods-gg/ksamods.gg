<?php

namespace App\Jobs;

use App\Models\ShareRelease;
use App\Support\Share\ArchiveMirror;
use App\Support\Share\DataFiles;
use Illuminate\Contracts\Queue\ShouldQueue;
use Illuminate\Foundation\Queue\Queueable;

/**
 * Uploads a listed release to the Internet Archive (rule 19), and once archive.org serves it, adds
 * it to the release's entry. The watcher appends a mirror that appears after a release was stamped,
 * so the release and its pull request do not wait for the upload.
 */
class MirrorShareRelease implements ShouldQueue
{
    use Queueable;

    public int $tries = 6;

    // A release deleted meanwhile (share:purge) has nothing left to mirror.
    public bool $deleteWhenMissingModels = true;

    // The upload, and archive.org taking up to ten minutes to serve it.
    public int $timeout = 1200;

    /** @var list<int> */
    public array $backoff = [60, 300, 900, 1800, 3600];

    public function __construct(public ShareRelease $release) {}

    public function handle(): void
    {
        $this->release->refresh();

        if ($this->release->taken_down_at !== null || $this->release->mirror_url !== null) {
            return;
        }

        $this->release->forceFill(['mirror_url' => ArchiveMirror::upload($this->release)])->save();
        DataFiles::writeReleaseList($this->release->share);
    }
}
