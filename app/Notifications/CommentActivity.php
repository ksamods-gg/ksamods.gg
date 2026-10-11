<?php

namespace App\Notifications;

use App\Models\Comment;
use App\Models\User;
use Illuminate\Bus\Queueable;
use Illuminate\Notifications\Messages\BroadcastMessage;
use Illuminate\Notifications\Notification;
use Illuminate\Support\Str;

/**
 * Someone replied to the user's comment or review, or mentioned them with @.
 *
 * Stored in the database straight away; the live push over Reverb goes through the queue, so
 * posting a comment never waits on, or fails with, the WebSocket server.
 */
class CommentActivity extends Notification
{
    use Queueable;

    /**
     * @param  'reply'|'review_reply'|'mention'  $kind
     */
    public function __construct(
        public readonly string $kind,
        public readonly Comment $comment,
        public readonly string $modName,
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
            // Only a public name and picture, as on the mod page.
            'actor' => ['name' => $this->comment->user->name, 'avatar' => $this->comment->user->avatar],
            'mod' => ['id' => $this->comment->listing_id, 'name' => $this->modName],
            'excerpt' => Str::limit($this->comment->body, 140),
            'comment_id' => $this->comment->id,
            'tab' => $this->comment->review_id === null ? 'comments' : 'reviews',
        ];
    }

    public function toBroadcast(User $notifiable): BroadcastMessage
    {
        return new BroadcastMessage($this->toArray($notifiable));
    }
}
