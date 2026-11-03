<?php

namespace App\Events;

use Illuminate\Broadcasting\Channel;
use Illuminate\Contracts\Broadcasting\ShouldBroadcast;

/**
 * A listing's new download total, so open pages show it at once. Public: the total is public on
 * the listing's page anyway. Queued, so counting a download never waits on Reverb.
 */
class ShareDownloadsChanged implements ShouldBroadcast
{
    public function __construct(public string $type, public string $id, public int $downloads) {}

    public function broadcastOn(): Channel
    {
        return new Channel('share-downloads');
    }

    public function broadcastAs(): string
    {
        return 'downloads';
    }
}
