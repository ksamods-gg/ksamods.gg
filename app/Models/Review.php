<?php

namespace App\Models;

use App\Models\Concerns\Reportable;
use Database\Factories\ReviewFactory;
use Illuminate\Database\Eloquent\Attributes\Fillable;
use Illuminate\Database\Eloquent\Factories\HasFactory;
use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Support\Carbon;

/**
 * One user's rating of a listing, with optional text. A user has at most one per listing.
 *
 * @property int $id
 * @property int $user_id
 * @property string $listing_id
 * @property int $rating
 * @property string|null $body
 * @property Carbon $created_at
 * @property Carbon $updated_at
 * @property-read User $user
 */
#[Fillable(['listing_id', 'rating', 'body'])]
class Review extends Model
{
    /** @use HasFactory<ReviewFactory> */
    use HasFactory, Reportable;

    /**
     * @return BelongsTo<User, $this>
     */
    public function user(): BelongsTo
    {
        return $this->belongsTo(User::class);
    }

    /**
     * @return array<string, string>
     */
    protected function casts(): array
    {
        return ['rating' => 'integer'];
    }
}
