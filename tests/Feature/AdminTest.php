<?php

use App\Enums\Role;
use App\Models\Comment;
use App\Models\Report;
use App\Models\Review;
use App\Models\User;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Http\Client\Factory as HttpFactory;
use Illuminate\Support\Facades\Http;
use Inertia\Testing\AssertableInertia as Assert;

uses(RefreshDatabase::class);

beforeEach(function () {
    $this->withoutVite();

    Http::fake([ContentIndex::URL => Http::response([
        'tags' => ['mod' => []],
        'listings' => [[
            'id' => 'Rockets',
            'published_at' => '2026-01-01T00:00:00Z',
            'updated_at' => '2026-01-01T00:00:00Z',
            'authored' => ['type' => 'mod', 'name' => 'Rocket Pack'],
            'releases' => [],
        ]],
    ])]);
});

describe('access', function () {
    it('lets moderators and admins in, and nobody else', function () {
        $this->get(route('admin.dashboard'))->assertRedirect(route('login'));
        $this->actingAs(User::factory()->create())->get(route('admin.dashboard'))->assertForbidden();

        foreach ([Role::Moderator, Role::Admin] as $role) {
            $staff = User::factory()->create(['role' => $role]);

            foreach ([route('admin.dashboard'), route('admin.reports'), route('admin.posts', 'reviews'), route('admin.posts', 'comments'), route('admin.users')] as $url) {
                $this->actingAs($staff)->get($url)->assertOk();
            }
        }
    });

    it('lets only admins change roles, and never their own', function () {
        $user = User::factory()->create();
        $moderator = User::factory()->create(['role' => Role::Moderator]);
        $admin = User::factory()->create(['role' => Role::Admin]);

        $this->actingAs($moderator)->patch(route('admin.users.role', $user), ['role' => 'admin'])->assertForbidden();
        $this->actingAs($admin)->patch(route('admin.users.role', $admin), ['role' => null])->assertForbidden();
        $this->actingAs($admin)->patch(route('admin.users.role', $user), ['role' => 'owner'])->assertSessionHasErrors('role');

        $this->actingAs($admin)->patch(route('admin.users.role', $user), ['role' => 'moderator'])->assertRedirect();
        expect($user->fresh()->role)->toBe(Role::Moderator);

        $this->actingAs($admin)->patch(route('admin.users.role', $user), ['role' => null])->assertRedirect();
        expect($user->fresh()->role)->toBeNull();
    });

    it('shares the open report count with staff only', function () {
        Report::factory()->count(2)->create();

        $this->actingAs(User::factory()->create())->get(route('home'))
            ->assertInertia(fn (Assert $page) => $page->where('openReports', null));
        $this->actingAs(User::factory()->create(['role' => Role::Moderator]))->get(route('home'))
            ->assertInertia(fn (Assert $page) => $page->where('openReports', 2));
    });
});

describe('reports', function () {
    it('takes one report per person on someone else\'s post', function () {
        $reporter = User::factory()->create();
        $comment = Comment::factory()->create();
        $report = ['type' => 'comment', 'id' => $comment->id, 'reason' => 'spam', 'details' => 'Ads'];

        $this->actingAs($reporter)->post(route('reports.store'), $report)->assertRedirect();
        $this->actingAs($reporter)->post(route('reports.store'), [...$report, 'reason' => 'abuse'])->assertRedirect();

        expect(Report::sole())
            ->user_id->toBe($reporter->id)
            ->reason->toBe('spam')
            ->details->toBe('Ads')
            ->reportable->is($comment)->toBeTrue();

        $this->actingAs($comment->user)->post(route('reports.store'), $report)->assertForbidden();
        $this->actingAs($reporter)->post(route('reports.store'), [...$report, 'reason' => 'boring'])->assertSessionHasErrors('reason');
    });

    it('offers to report others\' posts on the mod page, not your own', function () {
        $viewer = User::factory()->create();
        Comment::factory()->create();
        Comment::factory()->for($viewer)->create(['created_at' => now()->addMinute()]);

        $this->actingAs($viewer)->get(route('mods.show', 'Rockets'))->assertInertia(fn (Assert $page) => $page
            ->where('comments.items.0.mine', true)
            ->where('comments.items.0.canReport', false)
            ->where('comments.items.1.canReport', true));
    });

    it('lists open reports for moderators to dismiss', function () {
        $moderator = User::factory()->create(['role' => Role::Moderator]);
        $report = Report::factory()->create(['details' => 'Ads']);

        $this->actingAs($moderator)->get(route('admin.reports'))->assertInertia(fn (Assert $page) => $page
            ->component('Admin/Reports')
            ->where('reports.data.0.reason', 'Spam')
            ->where('reports.data.0.post.mod.name', 'Rocket Pack')
            ->where('reports.data.0.post.url', route('mods.show', ['id' => 'Rockets', 'tab' => 'comments']).'#comment-'.$report->reportable_id));

        $this->actingAs($moderator)->patch(route('admin.reports.resolve', $report))->assertRedirect();

        expect($report->fresh())->resolved_at->not->toBeNull()->resolved_by->toBe($moderator->id);
        $this->actingAs($moderator)->get(route('admin.reports'))->assertInertia(fn (Assert $page) => $page->has('reports.data', 0));
        $this->actingAs($moderator)->get(route('admin.reports', ['status' => 'resolved']))->assertInertia(fn (Assert $page) => $page->has('reports.data', 1));
    });

    it('resolves a post\'s open reports when a moderator deletes it', function () {
        $moderator = User::factory()->create(['role' => Role::Moderator]);
        $review = Review::factory()->create();
        $report = Report::factory()->create(['reportable_type' => $review->getMorphClass(), 'reportable_id' => $review->id]);

        $this->actingAs($moderator)->delete(route('reviews.destroy', $review))->assertRedirect();

        expect(Review::count())->toBe(0)
            ->and($report->fresh())->resolved_by->toBe($moderator->id);
    });
});

it('lists the newest reviews and comments, searchable, with their open reports', function () {
    $moderator = User::factory()->create(['role' => Role::Moderator]);
    $flagged = Review::factory()->create(['body' => 'Buy cheap gold', 'rating' => 1]);
    Review::factory()->create(['body' => 'Lovely rockets']);
    Report::factory()->create(['reportable_type' => $flagged->getMorphClass(), 'reportable_id' => $flagged->id]);

    $this->actingAs($moderator)->get(route('admin.posts', ['type' => 'reviews', 'q' => 'gold']))->assertInertia(fn (Assert $page) => $page
        ->component('Admin/Posts')
        ->has('posts.data', 1)
        ->where('posts.data.0.body', 'Buy cheap gold')
        ->where('posts.data.0.rating', 1)
        ->where('posts.data.0.openReports', 1));
});

it('finds users by any linked account, and tells moderators they cannot change roles', function () {
    $moderator = User::factory()->create(['role' => Role::Moderator]);
    User::factory()->create(['name' => 'Valentina', 'discord_username' => 'val.k']);
    User::factory()->create(['name' => 'Bob']);

    $this->actingAs($moderator)->get(route('admin.users', ['q' => 'val.k']))->assertInertia(fn (Assert $page) => $page
        ->component('Admin/Users')
        ->where('canChangeRoles', false)
        ->has('users.data', 1)
        ->where('users.data.0.name', 'Valentina'));
});

describe('dashboard', function () {
    it('counts what needs attention', function () {
        Report::factory()->create();
        Review::factory()->create();

        $this->actingAs(User::factory()->create(['role' => Role::Admin]))->get(route('admin.dashboard'))
            ->assertInertia(fn (Assert $page) => $page
                ->where('stats.openReports', 1)
                ->where('stats.reviews', 1)
                ->where('stats.comments', 1)
                ->where('stats.mods', 1));
    });

    it('refreshes the content index, keeping the cached copy if the fetch fails', function () {
        $admin = User::factory()->create(['role' => Role::Admin]);
        expect(ContentIndex::snapshot()['listings'])->toHaveCount(1);

        // A fresh fake: the one from beforeEach would otherwise keep answering first.
        Http::swap(new HttpFactory);
        Http::fake([ContentIndex::URL => Http::sequence()
            ->push(['tags' => [], 'listings' => []])
            ->pushStatus(500)
            ->pushStatus(500)
            ->pushStatus(500)]);

        $this->actingAs($admin)->post(route('admin.refresh-index'))->assertRedirect()->assertInertiaFlash('status', 'Content index refreshed: 0 listings.');
        expect(ContentIndex::snapshot()['listings'])->toBe([]);

        // The fetch retries twice before giving up.
        $this->actingAs($admin)->post(route('admin.refresh-index'))->assertSessionHasErrors('index');
        expect(ContentIndex::snapshot()['listings'])->toBe([]);
    });
});
