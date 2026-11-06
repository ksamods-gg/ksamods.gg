<?php

namespace App\Jobs;

use App\Models\ShareRelease;
use App\Support\Share\ArchiveMirror;
use App\Support\Share\DataFiles;
use DateTimeInterface;
use Illuminate\Contracts\Queue\ShouldQueue;
use Illuminate\Foundation\Queue\Queueable;
use Illuminate\Support\Facades\Cache;

/**
 * Uploads a listed release to the Internet Archive (rule 19), and once archive.org serves it, adds
 * it to the release's entry. The watcher appends a mirror that appears after a release was stamped,
 * so the release and its pull request do not wait for the upload.
 */
class MirrorShareRelease implements ShouldQueue
{
    use Queueable;

    // A release deleted meanwhile (share:purge) has nothing left to mirror.
    public bool $deleteWhenMissingModels = true;

    // The upload of an archive of up to the size limit.
    public int $timeout = 900;

    // When the upload or a check fails.
    /** @var list<int> */
    public array $backoff = [60, 300, 900, 1800, 3600];

    public function __construct(public ShareRelease $release) {}

    /**
     * archive.org can take hours to serve an upload when it is busy.
     */
    public function retryUntil(): DateTimeInterface
    {
        return now()->addDays(2);
    }

    public function handle(): void
    {
        $this->release->refresh();

        if ($this->release->taken_down_at !== null || $this->release->mirror_url !== null) {
            return;
        }

        // Uploaded once; later attempts only look whether archive.org serves it yet, without
        // sending it again or holding a worker while it waits.
        $uploaded = 'share-mirror-uploaded:'.$this->release->id;
        $served = ArchiveMirror::served($this->release);

        if (! $served && ! Cache::has($uploaded)) {
            ArchiveMirror::upload($this->release);
            Cache::put($uploaded, true, now()->addDays(2));
            $served = ArchiveMirror::served($this->release);
        }

        if (! $served) {
            // The queue's release(), back in five minutes; not the property.
            $this->release(300);

            return;
        }

        $this->release->forceFill(['mirror_url' => ArchiveMirror::url($this->release)])->save();
        DataFiles::writeReleaseList($this->release->share);
        $this->release->share->notifyOwner('share_mirrored', $this->release->version);
    }
}
