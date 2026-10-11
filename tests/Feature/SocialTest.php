<?php

use App\Enums\Role;
use App\Models\Comment;
use App\Models\Review;
use App\Models\User;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
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
            'authored' => ['type' => 'mod', 'name' => 'Rockets'],
            'releases' => [],
        ]],
    ])]);
});

describe('reviews', function () {
    it('keeps one review per user and mod, replacing it on a second save', function () {
        $user = User::factory()->create();

        $this->actingAs($user)->put(route('reviews.save', 'Rockets'), ['rating' => 4, 'body' => 'Good rockets.'])->assertRedirect();
        $this->actingAs($user)->put(route('reviews.save', 'Rockets'), ['rating' => 5, 'body' => ''])->assertRedirect();

        expect(Review::sole())
            ->user_id->toBe($user->id)
            ->rating->toBe(5)
            ->body->toBeNull();
    });

    it('rejects a rating outside 1 to 5', function (mixed $rating) {
        $this->actingAs(User::factory()->create())
            ->put(route('reviews.save', 'Rockets'), ['rating' => $rating])
            ->assertSessionHasErrors('rating');

        expect(Review::count())->toBe(0);
    })->with([0, 6, 'five', null]);

    it('cannot be written by a guest or about a mod that is not listed', function () {
        $this->put(route('reviews.save', 'Rockets'), ['rating' => 5])->assertRedirect(route('login'));
        $this->actingAs(User::factory()->create())->put(route('reviews.save', 'Nope'), ['rating' => 5])->assertNotFound();
    });

    it('can be deleted by its author or an admin, and nobody else', function () {
        $review = Review::factory()->create();

        $this->actingAs(User::factory()->create())->delete(route('reviews.destroy', $review))->assertForbidden();
        $this->actingAs($review->user)->delete(route('reviews.destroy', $review))->assertRedirect();
        expect(Review::count())->toBe(0);

        $another = Review::factory()->create();
        $this->actingAs(User::factory()->create(['role' => Role::Moderator]))->delete(route('reviews.destroy', $another))->assertRedirect();
        expect(Review::count())->toBe(0);
    });
});

describe('comments', function () {
    it('posts a comment as the signed-in user', function () {
        $user = User::factory()->create();

        $this->actingAs($user)->post(route('comments.store', 'Rockets'), ['body' => '  Works great.  ', 'user_id' => 999])->assertRedirect();

        expect(Comment::sole())->user_id->toBe($user->id)->body->toBe('Works great.');
    });

    it('rejects an empty or over-long comment', function (string $body) {
        $this->actingAs(User::factory()->create())
            ->post(route('comments.store', 'Rockets'), ['body' => $body])
            ->assertSessionHasErrors('body');
    })->with(['   ', str_repeat('a', 2001)]);

    it('lets only the author edit, and the author or an admin delete', function () {
        $comment = Comment::factory()->create();
        $admin = User::factory()->create(['role' => Role::Moderator]);

        $this->actingAs($admin)->patch(route('comments.update', $comment), ['body' => 'Rewritten'])->assertForbidden();
        $this->actingAs($comment->user)->patch(route('comments.update', $comment), ['body' => 'Fixed typo'])->assertRedirect();
        expect($comment->fresh()->body)->toBe('Fixed typo');

        $this->actingAs(User::factory()->create())->delete(route('comments.destroy', $comment))->assertForbidden();
        $this->actingAs($admin)->delete(route('comments.destroy', $comment))->assertRedirect();
        expect(Comment::count())->toBe(0);
    });

    it('limits how fast one user can post', function () {
        $user = User::factory()->create();

        foreach (range(1, 10) as $n) {
            $this->actingAs($user)->post(route('comments.store', 'Rockets'), ['body' => "Comment {$n}"])->assertRedirect();
        }

        $this->actingAs($user)->post(route('comments.store', 'Rockets'), ['body' => 'One too many'])->assertTooManyRequests();
    });
});

describe('replies', function () {
    it('nests a reply to a reply under the reply it answers', function () {
        $root = Comment::factory()->create();
        $user = User::factory()->create();

        $this->actingAs($user)->post(route('comments.store', 'Rockets'), ['body' => 'First reply', 'parent_id' => $root->id]);
        $reply = Comment::latest('id')->first();
        $this->actingAs($user)->post(route('comments.store', 'Rockets'), ['body' => 'Reply to the reply', 'parent_id' => $reply->id]);

        expect(Comment::latest('id')->first())
            ->parent_id->toBe($reply->id)
            ->review_id->toBeNull();
    });

    it('nests replies under a review, keeping every one in that review\'s thread', function () {
        $review = Review::factory()->create();
        $user = User::factory()->create();

        $this->actingAs($user)->post(route('comments.store', 'Rockets'), ['body' => 'Agreed', 'review_id' => $review->id]);
        $reply = Comment::sole();
        $this->actingAs($user)->post(route('comments.store', 'Rockets'), ['body' => 'Me too', 'parent_id' => $reply->id]);

        expect(Comment::latest('id')->first())
            ->parent_id->toBe($reply->id)
            ->review_id->toBe($review->id);
    });

    it('refuses a reply to another mod\'s comment or review, or to both at once', function (Closure $target) {
        $this->actingAs(User::factory()->create())
            ->post(route('comments.store', 'Rockets'), ['body' => 'Hi', ...$target()])
            ->assertSessionHasErrors();

        expect(Comment::where('body', 'Hi')->exists())->toBeFalse();
    })->with([
        'another mod\'s comment' => [fn () => ['parent_id' => Comment::factory()->create(['listing_id' => 'Other'])->id]],
        'another mod\'s review' => [fn () => ['review_id' => Review::factory()->create(['listing_id' => 'Other'])->id]],
        'a deleted comment' => [function () {
            $comment = Comment::factory()->create();
            $comment->delete();

            return ['parent_id' => $comment->id];
        }],
        'both at once' => [fn () => [
            'parent_id' => Comment::factory()->create()->id,
            'review_id' => Review::factory()->create()->id,
        ]],
    ]);

    it('keeps a deleted comment with replies as a placeholder, and removes one without', function () {
        $root = Comment::factory()->create(['body' => 'Secret']);
        Comment::factory()->create(['parent_id' => $root->id, 'body' => 'A reply']);
        $lonely = Comment::factory()->create();

        $this->actingAs($root->user)->delete(route('comments.destroy', $root));
        $this->actingAs($lonely->user)->delete(route('comments.destroy', $lonely));

        expect(Comment::withTrashed()->find($lonely->id))->toBeNull()
            ->and(Comment::withTrashed()->find($root->id)->trashed())->toBeTrue();

        $this->get(route('mods.show', 'Rockets'))->assertInertia(fn (Assert $page) => $page
            ->has('comments.items', 1)
            ->where('comments.items.0.deleted', true)
            ->where('comments.items.0.body', null)
            ->where('comments.items.0.author', null)
            ->where('comments.items.0.replies.0.body', 'A reply')
            ->where('comments.count', 1))
            ->assertDontSee('Secret');
    });

    it('deletes a review\'s replies with it', function () {
        $review = Review::factory()->create();
        Comment::factory()->create(['review_id' => $review->id]);

        $this->actingAs($review->user)->delete(route('reviews.destroy', $review));

        expect(Comment::withTrashed()->count())->toBe(0);
    });

    it('shows replies as a tree, oldest first at each level', function () {
        $review = Review::factory()->create();
        $older = Comment::factory()->create(['review_id' => $review->id, 'body' => 'Older', 'created_at' => now()->subHour()]);
        Comment::factory()->create(['review_id' => $review->id, 'body' => 'Newer']);
        Comment::factory()->create(['review_id' => $review->id, 'parent_id' => $older->id, 'body' => 'Under older']);
        $root = Comment::factory()->create(['body' => 'Root']);
        $reply = Comment::factory()->create(['parent_id' => $root->id, 'body' => 'Reply']);
        $deeper = Comment::factory()->create(['parent_id' => $reply->id, 'body' => 'Deeper']);
        Comment::factory()->create(['parent_id' => $deeper->id, 'body' => 'Deepest']);

        $this->get(route('mods.show', 'Rockets'))->assertInertia(fn (Assert $page) => $page
            ->where('reviews.items.0.replies.0.body', 'Older')
            ->where('reviews.items.0.replies.0.replies.0.body', 'Under older')
            ->where('reviews.items.0.replies.1.body', 'Newer')
            ->has('comments.items', 1)
            ->where('comments.items.0.body', 'Root')
            ->where('comments.items.0.replies.0.body', 'Reply')
            ->where('comments.items.0.replies.0.replies.0.body', 'Deeper')
            ->where('comments.items.0.replies.0.replies.0.replies.0.body', 'Deepest')
            // The discussion's count leaves out comments on reviews.
            ->where('comments.count', 4));
    });

    it('keeps a deleted comment only while something live is below it, at any depth', function () {
        $root = Comment::factory()->create(['body' => 'Root']);
        $middle = Comment::factory()->create(['parent_id' => $root->id]);
        Comment::factory()->create(['parent_id' => $middle->id, 'body' => 'Still here']);
        $deadEnd = Comment::factory()->create(['parent_id' => $root->id]);
        Comment::factory()->create(['parent_id' => $deadEnd->id])->delete();
        $root->delete();
        $middle->delete();
        $deadEnd->delete();

        $this->get(route('mods.show', 'Rockets'))->assertInertia(fn (Assert $page) => $page
            ->has('comments.items', 1)
            ->where('comments.items.0.deleted', true)
            ->has('comments.items.0.replies', 1)
            ->where('comments.items.0.replies.0.deleted', true)
            ->where('comments.items.0.replies.0.replies.0.body', 'Still here'));
    });

    it('removes a comment, and the deleted placeholders below it, when nothing live replies to it', function () {
        $comment = Comment::factory()->create();
        Comment::factory()->create(['parent_id' => $comment->id])->delete();

        $this->actingAs($comment->user)->delete(route('comments.destroy', $comment));

        expect(Comment::withTrashed()->count())->toBe(0);
    });
});

it('shows the reviews and comments on the mod page, with only public author details', function () {
    $author = User::factory()->create(['name' => 'Jeb', 'avatar' => 'https://example.com/jeb.png', 'email' => 'jeb@example.com']);
    $viewer = User::factory()->create();
    // Newest first: the viewer's review, then the author's older one.
    Review::factory()->for($author)->create(['rating' => 5, 'body' => 'Great', 'created_at' => now()->subDay()]);
    Review::factory()->for($viewer)->create(['rating' => 2, 'body' => null]);
    Comment::factory()->for($author)->create(['body' => 'Hello']);

    $this->actingAs($viewer)->get(route('mods.show', 'Rockets'))->assertInertia(fn (Assert $page) => $page
        ->where('reviews.average', 3.5)
        ->where('reviews.count', 2)
        ->where('reviews.mine', ['rating' => 2, 'body' => null])
        ->where('reviews.items.1.author', ['name' => 'Jeb', 'avatar' => 'https://example.com/jeb.png'])
        ->where('reviews.items.1.canDelete', false)
        ->where('reviews.items.0.mine', true)
        ->where('comments.count', 1)
        ->where('comments.items.0.body', 'Hello'))
        ->assertDontSee('jeb@example.com');
});

it('shows each mod\'s rating on the mod list', function () {
    Review::factory()->count(2)->sequence(['rating' => 4], ['rating' => 5])->create();

    $this->get(route('mods.index'))->assertInertia(fn (Assert $page) => $page
        ->where('mods.0.rating', ['average' => 4.5, 'count' => 2]));
});

it('makes and unmakes admins and moderators from the command line', function () {
    $user = User::factory()->create(['github_login' => 'jeb-gh']);

    $this->artisan('ksamods:admin', ['account' => 'jeb-gh'])->assertSuccessful();
    expect($user->fresh()->role)->toBe(Role::Admin);

    $this->artisan('ksamods:admin', ['account' => 'jeb-gh', '--moderator' => true])->assertSuccessful();
    expect($user->fresh()->role)->toBe(Role::Moderator);

    $this->artisan('ksamods:admin', ['account' => 'jeb-gh', '--revoke' => true])->assertSuccessful();
    expect($user->fresh()->role)->toBeNull();

    $this->artisan('ksamods:admin', ['account' => 'nobody'])->assertFailed();
});
