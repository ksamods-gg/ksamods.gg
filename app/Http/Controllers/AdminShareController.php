<?php

namespace App\Http\Controllers;

use App\Jobs\MirrorShareRelease;
use App\Models\Share;
use App\Models\ShareRelease;
use App\Support\Share\ArchiveMirror;
use App\Support\Share\ShareHost;
use Illuminate\Http\RedirectResponse;
use Illuminate\Support\Facades\Artisan;
use Illuminate\Support\Facades\DB;
use Illuminate\Support\Str;
use Inertia\Inertia;
use Inertia\Response;
use RuntimeException;
use Throwable;

/**
 * The share host's health, for admins: queue jobs that failed, releases still waiting for their
 * archive.org copy, and every listing with its pull request, with the fixes that used to need the
 * terminal.
 */
class AdminShareController extends Controller
{
    public function index(): Response
    {
        return Inertia::render('Admin/Shares', [
            'failedJobs' => DB::table('failed_jobs')->latest('failed_at')->limit(50)->get()->map(fn (object $job) => [
                'id' => $job->uuid,
                'name' => class_basename((string) (json_decode($job->payload, true)['displayName'] ?? 'Job')),
                'failed_at' => $job->failed_at,
                'error' => Str::limit(Str::before((string) $job->exception, "\n"), 300),
            ]),
            'mirroring' => ArchiveMirror::enabled(),
            'waitingMirrors' => ArchiveMirror::enabled()
                ? ShareRelease::with('share')->whereNull('mirror_url')->whereNull('taken_down_at')->latest('id')->get()->map(fn (ShareRelease $release) => [
                    'id' => $release->id,
                    'name' => $release->share->name,
                    'version' => $release->version,
                    'url' => $release->share->url(),
                    'created_at' => $release->created_at->toIso8601String(),
                ])
                : [],
            'listings' => Share::with('user')->withCount(['releases' => fn ($query) => $query->whereNull('taken_down_at')])
                ->latest('updated_at')->limit(100)->get()->map(fn (Share $share) => [
                    'id' => $share->id,
                    'listing_id' => $share->listing_id,
                    'type' => $share->type,
                    'name' => $share->name,
                    'owner' => $share->user?->name,
                    'url' => $share->url(),
                    'releases' => $share->releases_count,
                    'pull_request_url' => $share->pull_request_url,
                    'pull_request_state' => $share->pull_request_state,
                ]),
        ]);
    }

    public function retryJob(string $uuid): RedirectResponse
    {
        return $this->run(fn () => Artisan::call('queue:retry', ['id' => [$uuid]]), 'The job is back on the queue.');
    }

    public function forgetJob(string $uuid): RedirectResponse
    {
        return $this->run(fn () => Artisan::call('queue:forget', ['id' => $uuid]), 'The failed job is gone.');
    }

    /**
     * Opens the listing's pull request again, or pushes it to its open one.
     */
    public function propose(Share $share): RedirectResponse
    {
        return $this->run(
            fn () => ShareHost::propose($share, $share->pull_request_state !== 'merged') ?? throw new RuntimeException('GitHub could not be reached; a retry is queued.'),
            "Proposed {$share->name}.",
        );
    }

    public function mirror(ShareRelease $release): RedirectResponse
    {
        MirrorShareRelease::dispatch($release);

        return $this->done("Mirroring {$release->share->name} {$release->version} from the queue.");
    }

    public function purge(Share $share): RedirectResponse
    {
        return $this->run(function () use ($share) {
            if (ShareHost::listed($share)) {
                throw new RuntimeException("{$share->name} is in the index; take its releases down instead.");
            }

            ShareHost::purge($share);
        }, "Deleted {$share->name}.");
    }

    /**
     * Runs a fix, and says how it went.
     */
    private function run(callable $fix, string $success): RedirectResponse
    {
        try {
            $fix();
        } catch (Throwable $error) {
            return back()->withErrors(['shares' => $error->getMessage()]);
        }

        return $this->done($success);
    }

    private function done(string $message): RedirectResponse
    {
        Inertia::flash('status', $message);

        return back();
    }
}
