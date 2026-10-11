<?php

namespace App\Support;

use App\Models\Comment;
use App\Models\Review;
use App\Models\User;
use Illuminate\Support\Str;

/**
 * Reviews and comments, shaped for the mod pages. Comments nest: each lists its replies, which
 * list theirs, both in the discussion and under each review.
 */
class ModSocial
{
    // ponytail: the newest of each only; add "load more" once a mod gathers this many.
    private const SHOWN = 100;

    /**
     * Average rating and review count of every reviewed listing.
     *
     * @return array<string, array{average: float, count: int}>
     */
    public static function ratings(): array
    {
        return Review::query()
            ->selectRaw('listing_id, avg(rating) as average, count(*) as count')
            ->groupBy('listing_id')
            ->get()
            ->mapWithKeys(fn (Review $row) => [$row->listing_id => [
                'average' => round((float) $row->getAttribute('average'), 1),
                'count' => (int) $row->getAttribute('count'),
            ]])
            ->all();
    }

    /**
     * The newest reviews with text and comments across every mod, for the landing page.
     *
     * @param  array<string, string>  $modNames  Listing name by id; posts on unlisted mods are left out.
     * @return list<array<string, mixed>>
     */
    public static function recent(int $limit, array $modNames): array
    {
        $reviews = Review::with('user:id,name,avatar')->whereNotNull('body')->latest()->limit($limit)->get();
        $comments = Comment::with('user:id,name,avatar')->latest()->limit($limit)->get();

        return array_values($reviews->toBase()->merge($comments)
            ->filter(fn (Review|Comment $post) => isset($modNames[$post->listing_id]))
            ->sortByDesc(fn (Review|Comment $post) => $post->created_at)
            ->take($limit)
            ->map(fn (Review|Comment $post) => [
                'key' => ($post instanceof Review ? 'review-' : 'comment-').$post->id,
                'rating' => $post instanceof Review ? $post->rating : null,
                'excerpt' => Str::limit((string) $post->body, 160),
                'author' => ['name' => $post->user->name, 'avatar' => $post->user->avatar],
                'mod' => ['id' => $post->listing_id, 'name' => $modNames[$post->listing_id]],
                'ago' => $post->created_at->diffForHumans(),
                'url' => route('mods.show', [
                    'id' => $post->listing_id,
                    'tab' => $post instanceof Review || $post->review_id !== null ? 'reviews' : 'comments',
                ]).'#'.($post instanceof Review ? 'review-' : 'comment-').$post->id,
            ])
            ->all());
    }

    /**
     * A listing's reviews and discussion, with every comment loaded in one query.
     *
     * @return array{
     *     reviews: array{average: float|null, count: int, mine: array{rating: int, body: string|null}|null, items: list<array<string, mixed>>},
     *     comments: array{count: int, items: list<array<string, mixed>>},
     * }
     */
    public static function forListing(string $listingId, ?User $viewer): array
    {
        $reviews = Review::with('user:id,name,avatar')->where('listing_id', $listingId)->latest()->get();
        $mine = $viewer === null ? null : $reviews->firstWhere('user_id', $viewer->id);

        // Oldest first, so replies read as a conversation.
        $comments = Comment::withTrashed()->with('user:id,name,avatar')->where('listing_id', $listingId)->oldest()->get();
        $tops = $comments->whereNull('parent_id');
        $repliesTo = [];

        foreach ($comments as $comment) {
            if ($comment->parent_id !== null) {
                $repliesTo[$comment->parent_id][] = $comment;
            }
        }

        return [
            'reviews' => [
                'average' => $reviews->isEmpty() ? null : round($reviews->avg('rating'), 1),
                'count' => $reviews->count(),
                'mine' => $mine === null ? null : ['rating' => $mine->rating, 'body' => $mine->body],
                'items' => array_values($reviews->take(self::SHOWN)->map(fn (Review $review) => [
                    'rating' => $review->rating,
                    ...self::post($review, $viewer),
                    'replies' => self::nodes($tops->where('review_id', $review->id), $repliesTo, $viewer),
                ])->all()),
            ],
            'comments' => [
                'count' => $comments->whereNull('review_id')->whereNull('deleted_at')->count(),
                // The discussion's own top-level comments, newest first.
                'items' => array_slice(self::nodes($tops->whereNull('review_id')->reverse(), $repliesTo, $viewer), 0, self::SHOWN),
            ],
        ];
    }

    /**
     * The comments, each with its replies nested below it.
     *
     * @param  iterable<Comment>  $comments
     * @param  array<int, list<Comment>>  $repliesTo  Replies by the id of the comment they answer.
     * @return list<array<string, mixed>>
     */
    private static function nodes(iterable $comments, array $repliesTo, ?User $viewer): array
    {
        $nodes = [];

        foreach ($comments as $comment) {
            $replies = self::nodes($repliesTo[$comment->id] ?? [], $repliesTo, $viewer);

            // A deleted comment stays only while a reply below it, at any depth, still needs it.
            if (! $comment->trashed() || $replies !== []) {
                $nodes[] = [...self::post($comment, $viewer), 'replies' => $replies];
            }
        }

        return $nodes;
    }

    /**
     * What every review and comment shows: its text and author, and what the viewer may do with it.
     *
     * @return array<string, mixed>
     */
    private static function post(Review|Comment $post, ?User $viewer): array
    {
        // A deleted comment kept for its replies shows neither its text nor who wrote it.
        if ($post instanceof Comment && $post->trashed()) {
            return [
                'id' => $post->id,
                'body' => null,
                'author' => null,
                'created_at' => $post->created_at->toIso8601String(),
                'edited' => false,
                'deleted' => true,
                'mine' => false,
                'canDelete' => false,
                'canReport' => false,
            ];
        }

        return [
            'id' => $post->id,
            'body' => $post->body,
            // Only a public name and picture; nothing else about the author leaves the server.
            'author' => ['name' => $post->user->name, 'avatar' => $post->user->avatar],
            'created_at' => $post->created_at->toIso8601String(),
            'edited' => $post->updated_at->gt($post->created_at),
            'deleted' => false,
            'mine' => $viewer !== null && $post->user_id === $viewer->id,
            'canDelete' => $viewer !== null && $viewer->canDelete($post),
            'canReport' => $viewer !== null && $post->user_id !== $viewer->id,
        ];
    }
}
