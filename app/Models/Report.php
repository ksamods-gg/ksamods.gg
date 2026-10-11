<?php

namespace App\Models;

use Database\Factories\ReportFactory;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Attributes\Scope;
use Illuminate\Database\Eloquent\Builder;
use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\MorphTo;
use Illuminate\Support\Carbon;

/**
 * A user's report of a review or comment, waiting for a moderator until resolved.
 *
 * @property int $id
 * @property int $user_id
 * @property string $reportable_type
 * @property int $reportable_id
 * @property string $reason
 * @property string|null $details
 * @property Carbon|null $resolved_at
 * @property int|null $resolved_by
 * @property Carbon $created_at
 * @property Carbon $updated_at
 * @property-read User $user
 * @property-read Review|Comment|null $reportable
 */
#[Fillable(['reason', 'details'])]
class Report extends Model
{
    /** @use HasFactory<ReportFactory> */
    use HasFactory;

    public const REASONS = [
        'spam' => 'Spam',
        'abuse' => 'Harassment or abuse',
        'off-topic' => 'Off-topic',
        'other' => 'Something else',
    ];

    /**
     * @return BelongsTo<User, $this>
     */
    public function user(): BelongsTo
    {
        return $this->belongsTo(User::class);
    }

    /**
     * The reported review or comment; null once it is deleted.
     *
     * @return MorphTo<Model, $this>
     */
    public function reportable(): MorphTo
    {
        return $this->morphTo();
    }

    /**
     * @param  Builder<self>  $query
     */
    #[Scope]
    protected function open(Builder $query): void
    {
        $query->whereNull('resolved_at');
    }

    /**
     * @return array<string, string>
     */
    protected function casts(): array
    {
        return ['resolved_at' => 'datetime'];
    }
}
