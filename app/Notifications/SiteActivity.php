<?php

namespace App\Notifications;

use App\Models\User;
use Illuminate\Bus\Queueable;
use Illuminate\Notifications\Messages\BroadcastMessage;
use Illuminate\Notifications\Notification;

/**
 * Something happened to a mod, vehicle or save the user owns or follows: a new release, a listing
 * merged into or closed from the index, a release's archive.org copy, a takedown. It opens `url`.
 */
class SiteActivity extends Notification
{
    use Queueable;

    /**
     * @param  'release'|'share_merged'|'share_closed'|'share_mirrored'|'share_taken_down'  $kind
     */
    public function __construct(
        public readonly string $kind,
        public readonly string $name,
        public readonly string $url,
        public readonly ?string $version = null,
        public readonly ?string $icon = null,
    ) {}

    /**
     * @return list<string>
     */
    public function via(User $notifiable): array
    {
        return ['database', 'broadcast'];
    }

    /**
     * @return array<string, mixed>
     */
    public function toArray(User $notifiable): array
    {
        return [
            'kind' => $this->kind,
            'subject' => ['name' => $this->name, 'icon' => $this->icon],
            'version' => $this->version,
            'url' => $this->url,
        ];
    }

    public function toBroadcast(User $notifiable): BroadcastMessage
    {
        return new BroadcastMessage($this->toArray($notifiable));
    }
}
