<?php

namespace App\Support;

use App\Models\Comment;
use App\Models\Review;
use App\Models\User;
use App\Notifications\CommentActivity;

/**
 * Tells people about a new comment: whoever it replies to, and whoever it mentions with @.
 */
class CommentNotifier
{
    public static function notify(Comment $comment): void
    {
        /** @var array<int, 'reply'|'review_reply'|'mention'> $kinds The reason per user id, one notification each. */
        $kinds = [];

        if ($comment->parent_id !== null) {
            $parent = Comment::find($comment->parent_id);

            if ($parent !== null) {
                $kinds[$parent->user_id] = 'reply';
            }
        } elseif ($comment->review_id !== null) {
            $review = Review::find($comment->review_id);

            if ($review !== null) {
                $kinds[$review->user_id] = 'review_reply';
            }
        }

        // Being replied to says more than being mentioned in that reply.
        foreach (self::mentioned($comment) as $user) {
            $kinds[$user->id] ??= 'mention';
        }

        unset($kinds[$comment->user_id]);

        if ($kinds === []) {
            return;
        }

        $modName = collect(ContentIndex::snapshot()['listings'])->firstWhere('id', $comment->listing_id)['authored']['name']
            ?? $comment->listing_id;

        foreach (User::whereIn('id', array_keys($kinds))->get() as $user) {
            $user->notify(new CommentActivity($kinds[$user->id], $comment, $modName));
        }
    }

    /**
     * The people the comment names with @, by display name or by GitHub or Discord username.
     *
     * Only people who already wrote on this mod can be mentioned: names are not unique and may
     * contain spaces, and this keeps a mention from reaching strangers elsewhere on the site.
     *
     * @return list<User>
     */
    public static function mentioned(Comment $comment): array
    {
        if (! str_contains($comment->body, '@')) {
            return [];
        }

        $participants = User::whereIn('id', Comment::where('listing_id', $comment->listing_id)->select('user_id'))
            ->orWhereIn('id', Review::where('listing_id', $comment->listing_id)->select('user_id'))
            ->get();

        return array_values($participants->filter(function (User $user) use ($comment) {
            foreach (array_filter([$user->name, $user->github_login, $user->discord_username]) as $handle) {
                // "@Name" on its own, so "@Bob" does not reach Bobby and an email address mentions nobody.
                if (preg_match('/(?<![\p{L}\p{N}_])@'.preg_quote($handle, '/').'(?![\p{L}\p{N}_])/iu', $comment->body)) {
                    return true;
                }
            }

            return false;
        })->all());
    }
}
