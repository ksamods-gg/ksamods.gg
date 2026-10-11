<?php

namespace App\Http\Controllers;

use App\Enums\Role;
use App\Models\Comment;
use App\Models\Report;
use App\Models\Review;
use App\Models\User;
use App\Support\ContentIndex;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Validation\Rule;
use Inertia\Inertia;
use Inertia\Response;
use Throwable;

/**
 * The admin panel. Moderators and admins see all of it; only admins change roles.
 */
class AdminController extends Controller
{
    public function dashboard(): Response
    {
        return Inertia::render('Admin/Dashboard', [
            'stats' => [
                'users' => User::count(),
                'newUsers' => User::where('created_at', '>=', now()->subWeek())->count(),
                'reviews' => Review::count(),
                'comments' => Comment::count(),
                'openReports' => Report::open()->count(),
                'mods' => count(ContentIndex::snapshot()['listings']),
            ],
        ]);
    }

    /**
     * Fetches the content index now instead of waiting for the cache to expire.
     */
    public function refreshIndex(): RedirectResponse
    {
        try {
            $count = count(ContentIndex::refresh()['listings']);
        } catch (Throwable) {
            return back()->withErrors(['index' => 'The content index could not be fetched. The cached copy is still in use.']);
        }

        Inertia::flash('status', "Content index refreshed: {$count} listings.");

        return back();
    }

    public function reports(Request $request): Response
    {
        $resolved = $request->query('status') === 'resolved';
        $names = $this->modNames();

        return Inertia::render('Admin/Reports', [
            'status' => $resolved ? 'resolved' : 'open',
            'reports' => Report::with(['user', 'reportable.user'])
                ->when($resolved, fn ($query) => $query->whereNotNull('resolved_at')->latest('resolved_at'), fn ($query) => $query->open()->oldest())
                ->paginate(20)
                ->withQueryString()
                ->through(fn (Report $report) => [
                    'id' => $report->id,
                    'reason' => Report::REASONS[$report->reason] ?? $report->reason,
                    'details' => $report->details,
                    'reporter' => $report->user->name,
                    'created_at' => $report->created_at->toIso8601String(),
                    'resolved_at' => $report->resolved_at?->toIso8601String(),
                    // Null once the post is deleted.
                    'post' => $report->reportable ? $this->post($report->reportable, $names) : null,
                ]),
        ]);
    }

    public function resolveReport(Request $request, Report $report): RedirectResponse
    {
        $report->forceFill(['resolved_at' => now(), 'resolved_by' => $request->user()->id])->save();

        return back();
    }

    /**
     * The newest reviews or comments across every mod.
     */
    public function posts(Request $request, string $type): Response
    {
        $search = trim((string) $request->query('q'));
        $model = $type === 'reviews' ? Review::class : Comment::class;
        $names = $this->modNames();

        return Inertia::render('Admin/Posts', [
            'type' => $type,
            'search' => $search,
            'posts' => $model::with('user')
                ->withCount(['reports' => fn ($query) => $query->open()])
                ->when($search !== '', fn ($query) => $query->where('body', 'like', '%'.addcslashes($search, '%_\\').'%'))
                ->latest()
                ->latest('id')
                ->paginate(25)
                ->withQueryString()
                ->through(fn (Review|Comment $post) => [...$this->post($post, $names), 'openReports' => $post->reports_count]),
        ]);
    }

    public function users(Request $request): Response
    {
        $search = trim((string) $request->query('q'));
        $like = '%'.addcslashes($search, '%_\\').'%';

        return Inertia::render('Admin/Users', [
            'search' => $search,
            'canChangeRoles' => $request->user()->isAdmin(),
            'users' => User::withCount(['reviews', 'comments'])
                ->when($search !== '', fn ($query) => $query->where(fn ($query) => $query
                    ->where('name', 'like', $like)
                    ->orWhere('github_login', 'like', $like)
                    ->orWhere('discord_username', 'like', $like)))
                ->latest()
                ->latest('id')
                ->paginate(25)
                ->withQueryString()
                ->through(fn (User $user) => [
                    'id' => $user->id,
                    'name' => $user->name,
                    'avatar' => $user->avatar,
                    'github_login' => $user->github_login,
                    'discord_username' => $user->discord_username,
                    'role' => $user->role?->value,
                    'reviews' => $user->reviews_count,
                    'comments' => $user->comments_count,
                    'created_at' => $user->created_at?->toIso8601String(),
                ]),
        ]);
    }

    public function updateRole(Request $request, User $user): RedirectResponse
    {
        // Nobody demotes themselves by accident and locks the panel.
        abort_if($user->is($request->user()), 403);

        $validated = $request->validate(['role' => ['nullable', Rule::enum(Role::class)]]);
        $user->forceFill(['role' => $validated['role'] ?? null])->save();

        return back();
    }

    /**
     * What the panel shows of a review or comment, with a link to it on its mod's page.
     *
     * @param  array<string, string>  $names
     * @return array<string, mixed>
     */
    private function post(Review|Comment $post, array $names): array
    {
        $isReview = $post instanceof Review;
        // A reply in a review's thread lives on the Reviews tab.
        $tab = $isReview || $post->review_id !== null ? 'reviews' : 'comments';

        return [
            'type' => $isReview ? 'review' : 'comment',
            'id' => $post->id,
            'body' => $post->body,
            'rating' => $isReview ? $post->rating : null,
            'author' => ['name' => $post->user->name, 'avatar' => $post->user->avatar],
            'mod' => ['id' => $post->listing_id, 'name' => $names[$post->listing_id] ?? $post->listing_id],
            'url' => route('mods.show', ['id' => $post->listing_id, 'tab' => $tab]).'#'.($isReview ? 'review-' : 'comment-').$post->id,
            'created_at' => $post->created_at->toIso8601String(),
        ];
    }

    /**
     * @return array<string, string>
     */
    private function modNames(): array
    {
        return collect(ContentIndex::snapshot()['listings'])
            ->mapWithKeys(fn (array $listing) => [$listing['id'] => $listing['authored']['name']])
            ->all();
    }
}
