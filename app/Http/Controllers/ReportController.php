<?php

namespace App\Http\Controllers;

use App\Models\Comment;
use App\Models\Report;
use App\Models\Review;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\Rule;

class ReportController extends Controller
{
    /**
     * Reports a review or comment to the moderators. Reporting the same post twice changes nothing.
     */
    public function store(Request $request): RedirectResponse
    {
        $validated = $request->validate([
            'type' => ['required', Rule::in(['review', 'comment'])],
            'id' => ['required', 'integer'],
            'reason' => ['required', Rule::in(array_keys(Report::REASONS))],
            'details' => ['nullable', 'string', 'max:500'],
        ]);

        $post = $validated['type'] === 'review'
            ? Review::findOrFail($request->integer('id'))
            : Comment::findOrFail($request->integer('id'));

        abort_if($post->user_id === $request->user()->id, 403);

        if (! $post->reports()->where('user_id', $request->user()->id)->exists()) {
            $report = new Report(['reason' => $validated['reason'], 'details' => $validated['details'] ?? null]);
            $post->reports()->save($report->user()->associate($request->user()));
        }

        return back();
    }
}
