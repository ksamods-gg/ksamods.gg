<?php

use App\Models\Comment;
use App\Models\Review;
use App\Models\User;
use App\Notifications\CommentActivity;
use App\Support\ContentIndex;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Facades\Http;
use Illuminate\Support\Facades\Notification;
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
            'authored' => ['type' => 'mod', 'name' => 'Better Rockets'],
            'releases' => [],
        ]],
    ])]);
});

function comment(User $as, string $body, array $target = []): void
{
    test()->actingAs($as)->post(route('comments.store', 'Rockets'), ['body' => $body, ...$target])->assertSessionHasNoErrors();
}

describe('who is told', function () {
    beforeEach(fn () => Notification::fake());

    it('tells the author of the comment that was replied to, live and stored', function () {
        $parent = Comment::factory()->create();

        comment(User::factory()->create(['name' => 'Bill']), 'Thanks!', ['parent_id' => $parent->id]);

        Notification::assertSentTo($parent->user, CommentActivity::class, fn (CommentActivity $n, array $channels) => $n->kind === 'reply'
            && $channels === ['database', 'broadcast']
            && $n->toArray($parent->user)['mod'] === ['id' => 'Rockets', 'name' => 'Better Rockets']
            && $n->toArray($parent->user)['actor']['name'] === 'Bill');
    });

    it('tells the author of a review about a reply to it', function () {
        $review = Review::factory()->create();

        comment(User::factory()->create(), 'Agreed', ['review_id' => $review->id]);

        Notification::assertSentTo($review->user, CommentActivity::class, fn (CommentActivity $n) => $n->kind === 'review_reply'
            && $n->toArray($review->user)['tab'] === 'reviews');
    });

    it('tells people mentioned by name, GitHub or Discord username, in any case', function () {
        $jeb = User::factory()->create(['name' => 'Jebediah Kerman']);
        $bill = User::factory()->create(['name' => 'Bill', 'github_login' => 'bill-k']);
        $val = User::factory()->create(['name' => 'Val', 'discord_username' => 'valentina_']);
        foreach ([$jeb, $bill, $val] as $user) {
            Comment::factory()->for($user)->create();
        }

        comment(User::factory()->create(), '@jebediah kerman and @BILL-K, also @valentina_: look.');

        foreach ([$jeb, $bill, $val] as $user) {
            Notification::assertSentTo($user, CommentActivity::class, fn (CommentActivity $n) => $n->kind === 'mention');
        }
    });

    it('does not mention a longer name, an email address, or someone who never wrote on the mod', function () {
        $bob = User::factory()->create(['name' => 'Bob']);
        Comment::factory()->for($bob)->create();
        $stranger = User::factory()->create(['name' => 'Stranger']);

        comment(User::factory()->create(), '@Bobby, mail bob@Bob.com, and @Stranger');

        Notification::assertNotSentTo([$bob, $stranger], CommentActivity::class);
    });

    it('tells nobody about their own comment, and nobody twice', function () {
        $author = User::factory()->create(['name' => 'Jeb']);
        $parent = Comment::factory()->for($author)->create();

        comment($author, 'Talking to myself @Jeb', ['parent_id' => $parent->id]);
        Notification::assertNothingSent();

        comment(User::factory()->create(), 'Hi @Jeb', ['parent_id' => $parent->id]);
        Notification::assertSentToTimes($author, CommentActivity::class, 1);
        Notification::assertSentTo($author, CommentActivity::class, fn (CommentActivity $n) => $n->kind === 'reply');
    });
});

describe('the notifications page', function () {
    it('lists them, and opening one marks it read and goes to the comment', function () {
        $user = User::factory()->create();
        $parent = Comment::factory()->for($user)->create();
        comment(User::factory()->create(['name' => 'Bill']), 'Nice one', ['parent_id' => $parent->id]);
        $reply = Comment::latest('id')->first();

        $this->actingAs($user)->get(route('home'))->assertInertia(fn (Assert $page) => $page->where('unreadNotifications', 1));

        $this->actingAs($user)->get(route('notifications'))->assertInertia(fn (Assert $page) => $page
            ->component('Notifications/Index')
            ->where('notifications.0.read', false)
            ->where('notifications.0.data.kind', 'reply')
            ->where('notifications.0.data.excerpt', 'Nice one'));

        $id = $user->notifications()->sole()->id;
        $this->actingAs($user)->get(route('notifications.open', $id))
            ->assertRedirect(route('mods.show', ['id' => 'Rockets', 'tab' => 'comments'])."#comment-{$reply->id}");

        expect($user->fresh()->unreadNotifications()->count())->toBe(0);
    });

    it('marks them all read', function () {
        $user = User::factory()->create();
        $parent = Comment::factory()->for($user)->create();
        comment(User::factory()->create(), 'One', ['parent_id' => $parent->id]);
        comment(User::factory()->create(), 'Two', ['parent_id' => $parent->id]);

        $this->actingAs($user)->post(route('notifications.read-all'))->assertRedirect();

        expect($user->fresh()->unreadNotifications()->count())->toBe(0);
    });

    it('opens only your own notifications', function () {
        $user = User::factory()->create();
        $parent = Comment::factory()->for($user)->create();
        comment(User::factory()->create(), 'Hello', ['parent_id' => $parent->id]);

        $this->actingAs(User::factory()->create())
            ->get(route('notifications.open', $user->notifications()->sole()->id))
            ->assertNotFound();
    });

    it('opens the mod page on the tab a link names', function () {
        $this->get(route('mods.show', ['id' => 'Rockets', 'tab' => 'comments']))
            ->assertInertia(fn (Assert $page) => $page->where('tab', 'comments'));
        $this->get(route('mods.show', ['id' => 'Rockets', 'tab' => 'nonsense']))
            ->assertInertia(fn (Assert $page) => $page->where('tab', null));
    });
});
