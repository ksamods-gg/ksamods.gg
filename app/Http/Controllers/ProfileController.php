<?php

namespace App\Http\Controllers;

use App\Models\Comment;
use App\Models\Review;
use App\Models\Share;
use App\Models\User;
use App\Support\ContentIndex;
use App\Support\ListingOwnership;
use App\Support\ModSocial;
use Illuminate\Http\RedirectResponse;
use Illuminate\Http\Request;
use Illuminate\Support\Str;
use Illuminate\Validation\Rule;
use Inertia\Inertia;
use Inertia\Response;

/**
 * Public profiles at /u/<username>: off until the player turns one on, and showing only the
 * sections they leave visible.
 */
class ProfileController extends Controller
{
    // The newest of each kind of post a profile shows.
    private const POSTS = 20;

    // Names a username may not take, for paths and for impersonation.
    private const RESERVED = ['admin', 'administrator', 'moderator', 'mod', 'mods', 'ksamods', 'support', 'settings', 'api', 'system', 'root', 'staff', 'help'];

    public function show(string $username): Response
    {
        $user = User::where('username', strtolower($username))->where('profile_enabled', true)->firstOrFail();
        $snapshot = ContentIndex::snapshot();
        $names = collect($snapshot['listings'])->pluck('authored.name', 'id');
        $shows = fn (string $section) => $user->showsOnProfile($section);

        $shares = fn (string $type) => Share::where('user_id', $user->id)->where('type', $type)
            ->whereHas('releases', fn ($query) => $query->whereNull('taken_down_at'))
            ->with(['releases' => fn ($query) => $query->whereNull('taken_down_at')])
            ->latest('updated_at')->get()
            ->map(fn (Share $share) => app(ShareController::class)->summary($share))->values();

        // The mods the player's last "My mods" visit found, and any in repositories of their own GitHub account.
        $login = strtolower((string) $user->github_login);
        $ratings = ModSocial::ratings();
        $mods = fn () => collect($snapshot['listings'])
            ->filter(fn (array $listing) => in_array($listing['id'], $user->mod_ids ?? [], true)
                || ($login !== '' && strtolower(Str::before((string) ListingOwnership::repository($listing['authored']), '/')) === $login))
            ->map(fn (array $listing) => [...app(ModController::class)->summary($listing), 'rating' => $ratings[$listing['id']] ?? null])
            ->sortByDesc('downloads')->values();

        $post = fn (Review|Comment $post) => [
            'id' => $post->id,
            'rating' => $post instanceof Review ? $post->rating : null,
            'body' => Str::limit((string) $post->body, 280),
            'mod' => ['id' => $post->listing_id, 'name' => $names[$post->listing_id] ?? $post->listing_id],
            'url' => route('mods.show', ['id' => $post->listing_id, 'tab' => $post instanceof Review || $post->review_id !== null ? 'reviews' : 'comments'])
                .'#'.($post instanceof Review ? 'review-' : 'comment-').$post->id,
            'created_at' => $post->created_at->toIso8601String(),
        ];

        return Inertia::render('Profiles/Show', [
            'profile' => [
                'name' => $user->name,
                'username' => $user->username,
                'avatar' => $user->avatar,
                'bio' => $user->bio,
                'links' => $user->profile_links ?? [],
                'github' => $user->profile_show_accounts ? $user->github_login : null,
                'discord' => $user->profile_show_accounts ? $user->discord_username : null,
                'joined' => $user->created_at?->toIso8601String(),
            ],
            'vehicles' => $shows('vehicles') ? $shares('vehicle') : null,
            'saves' => $shows('saves') ? $shares('save') : null,
            'mods' => $shows('mods') ? $mods() : null,
            'reviews' => $shows('reviews') ? Review::where('user_id', $user->id)->latest()->limit(self::POSTS)->get()->map($post) : null,
            'comments' => $shows('comments') ? Comment::where('user_id', $user->id)->latest()->limit(self::POSTS)->get()->map($post) : null,
            'tagNames' => app(ModController::class)->tagNames($snapshot),
        ]);
    }

    /**
     * The player's own settings for their profile.
     */
    public function edit(Request $request): Response
    {
        $user = $request->user();

        return Inertia::render('Settings/Profile', [
            'enabled' => $user->profile_enabled,
            // Suggested from GitHub or Discord until the player picks one.
            'username' => $user->username ?? $this->suggestedUsername($user),
            'bio' => $user->bio,
            'links' => $user->profile_links ?? [],
            'showAccounts' => $user->profile_show_accounts,
            'sections' => collect(User::PROFILE_SECTIONS)->mapWithKeys(fn (string $section) => [$section => $user->showsOnProfile($section)]),
            'url' => $user->profileUrl(),
        ]);
    }

    public function update(Request $request): RedirectResponse
    {
        $user = $request->user();
        // Usernames compare without case, the reserved ones too.
        $request->merge(['username' => is_string($request->input('username')) ? strtolower(trim($request->input('username'))) : null]);
        $validated = $request->validate([
            'enabled' => ['boolean'],
            'username' => [
                Rule::requiredIf((bool) $request->input('enabled')), 'nullable', 'string',
                'regex:/^[A-Za-z0-9](?:[A-Za-z0-9_-]{1,28}[A-Za-z0-9])$/',
                Rule::notIn(self::RESERVED),
                Rule::unique('users', 'username')->ignore($user->id),
            ],
            'bio' => ['nullable', 'string', 'max:300'],
            'links' => ['array', 'max:4'],
            'links.*' => ['nullable', 'url:http,https', 'max:200'],
            'showAccounts' => ['boolean'],
            'sections' => ['array'],
            'sections.*' => ['boolean'],
        ], [
            'username.regex' => 'A username is 3 to 30 letters, digits, - or _, starting and ending with a letter or digit.',
            'username.not_in' => 'That username is reserved.',
            'username.unique' => 'That username is taken.',
        ]);

        $user->forceFill([
            'profile_enabled' => (bool) ($validated['enabled'] ?? false),
            'username' => isset($validated['username']) ? strtolower($validated['username']) : $user->username,
            'bio' => trim((string) ($validated['bio'] ?? '')) ?: null,
            'profile_links' => array_values(array_filter($validated['links'] ?? [])),
            'profile_show_accounts' => (bool) ($validated['showAccounts'] ?? false),
            'profile_sections' => collect(User::PROFILE_SECTIONS)->mapWithKeys(fn (string $section) => [$section => (bool) ($validated['sections'][$section] ?? true)])->all(),
        ])->save();

        Inertia::flash('status', $user->profile_enabled ? 'Profile updated.' : 'Saved. Your profile is off, so nobody can see it.');

        return back();
    }

    /**
     * The GitHub or Discord name, as a username, when it is free.
     */
    private function suggestedUsername(User $user): ?string
    {
        $name = strtolower((string) preg_replace('/[^A-Za-z0-9_-]/', '', (string) ($user->github_login ?? $user->discord_username)));

        return preg_match('/^[a-z0-9](?:[a-z0-9_-]{1,28}[a-z0-9])$/', $name) === 1
            && ! in_array($name, self::RESERVED, true)
            && ! User::where('username', $name)->exists() ? $name : null;
    }
}
