<?php

namespace App\Http\Controllers;

use App\Models\Comment;
use App\Support\CommentNotifier;
use App\Support\ContentIndex;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\Rule;

class CommentController extends Controller
{
    /**
     * A top-level comment, or a reply to a comment (parent_id) or to a review (review_id).
     */
    public function store(Request $request, string $id): RedirectResponse
    {
        abort_unless(ContentIndex::has($id), 404);

        $validated = $request->validate([
            'body' => ['required', 'string', 'max:2000'],
            'parent_id' => [
                'nullable', 'integer', 'prohibits:review_id',
                Rule::exists('comments', 'id')->where('listing_id', $id)->whereNull('deleted_at'),
            ],
            'review_id' => ['nullable', 'integer', Rule::exists('reviews', 'id')->where('listing_id', $id)],
        ]);

        $parent = $request->filled('parent_id') ? Comment::findOrFail($request->integer('parent_id')) : null;

        // Replies nest under the comment they answer, and stay in the review thread it belongs to.
        $comment = new Comment([
            'listing_id' => $id,
            'body' => $validated['body'],
            'parent_id' => $parent?->id,
            'review_id' => $parent->review_id ?? $validated['review_id'] ?? null,
        ]);
        $comment->user()->associate($request->user())->save();
        CommentNotifier::notify($comment);

        return back();
    }

    /**
     * Only the author edits a comment; an admin can only delete it.
     */
    public function update(Request $request, Comment $comment): RedirectResponse
    {
        abort_unless($comment->user_id === $request->user()->id, 403);

        // Input arrives trimmed, and blank as null, by the framework's default middleware.
        $comment->update($request->validate(['body' => ['required', 'string', 'max:2000']]));

        return back();
    }

    /**
     * A comment with replies stays as a "deleted" placeholder so its thread keeps making sense.
     */
    public function destroy(Request $request, Comment $comment): RedirectResponse
    {
        abort_unless($request->user()->canDelete($comment), 403);

        $comment->replies()->exists() ? $comment->delete() : $comment->forceDelete();

        return back();
    }
}
