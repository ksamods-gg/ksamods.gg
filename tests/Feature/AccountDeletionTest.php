<?php

use App\Models\Comment;
use App\Models\Report;
use App\Models\Review;
use App\Models\User;
use App\Notifications\CommentActivity;
use Illuminate\Foundation\Testing\RefreshDatabase;
use Illuminate\Support\Str;

uses(RefreshDatabase::class);

it('deletes the account with its posts, and signs out', function () {
    $user = User::factory()->create();
    $other = User::factory()->create();
    Review::factory()->for($user)->create();
    $comment = Comment::factory()->for($user)->create();
    $reply = Comment::factory()->for($other)->create(['parent_id' => $comment->id]);
    $kept = Comment::factory()->for($other)->create();

    $this->actingAs($user)->delete(route('account.destroy'))->assertRedirect(route('home'));

    $this->assertGuest();
    expect(User::find($user->id))->toBeNull()
        ->and(Review::count())->toBe(0)
        ->and(Comment::withTrashed()->pluck('id')->all())->toBe([$kept->id])
        ->and(Comment::withTrashed()->find($reply->id))->toBeNull();
});

it('deletes the notifications that quote the user\'s comments, and resolves reports about them', function () {
    $user = User::factory()->create();
    $other = User::factory()->create();
    $comment = Comment::factory()->for($user)->create();
    $notification = fn (int $commentId) => $other->notifications()->create([
        'id' => (string) Str::uuid(),
        'type' => CommentActivity::class,
        'data' => ['comment_id' => $commentId],
    ]);
    $notification($comment->id);
    $unrelated = $notification(Comment::factory()->create()->id);
    $report = Report::factory()->create(['reportable_type' => $comment->getMorphClass(), 'reportable_id' => $comment->id]);

    $this->actingAs($user)->delete(route('account.destroy'));

    expect($other->notifications()->pluck('id')->all())->toBe([$unrelated->id])
        ->and($report->fresh()->resolved_at)->not->toBeNull();
});

it('cannot be used by a guest', function () {
    $this->delete(route('account.destroy'))->assertRedirect(route('login'));
});
