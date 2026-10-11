<?php

namespace App\Http\Controllers;

use App\Models\Review;
use App\Support\ContentIndex;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;

class ReviewController extends Controller
{
    /**
     * Creates or replaces the user's one review of a listing.
     */
    public function save(Request $request, string $id): RedirectResponse
    {
        abort_unless(ContentIndex::has($id), 404);

        $validated = $request->validate([
            'rating' => ['required', 'integer', 'between:1,5'],
            'body' => ['nullable', 'string', 'max:2000'],
        ]);

        $review = Review::where('user_id', $request->user()->id)->where('listing_id', $id)->firstOrNew();
        $review->forceFill(['user_id' => $request->user()->id, 'listing_id' => $id])
            ->fill(['rating' => $validated['rating'], 'body' => $validated['body'] ?? null])
            ->save();

        return back();
    }

    public function destroy(Request $request, Review $review): RedirectResponse
    {
        abort_unless($request->user()->canDelete($review), 403);

        $review->delete();

        return back();
    }
}
