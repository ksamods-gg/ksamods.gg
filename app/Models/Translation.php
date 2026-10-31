<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Database\Eloquent\Relations\HasMany;

/**
 * One player's suggested translation of an English text of the site. Votes decide which
 * suggestion the site shows.
 *
 * @property int $id
 * @property int $language_id
 * @property string $source
 * @property string $source_hash
 * @property string $text
 * @property int|null $user_id
 * @property-read int|string|null $votes_total The sum of its votes, when loaded with withSum.
 * @property-read Language $language
 * @property-read User|null $user
 */
class Translation extends Model
{
    protected $guarded = [];

    /**
     * @return BelongsTo<Language, $this>
     */
    public function language(): BelongsTo
    {
        return $this->belongsTo(Language::class);
    }

    /**
     * @return BelongsTo<User, $this>
     */
    public function user(): BelongsTo
    {
        return $this->belongsTo(User::class);
    }

    /**
     * @return HasMany<TranslationVote, $this>
     */
    public function votes(): HasMany
    {
        return $this->hasMany(TranslationVote::class);
    }
}
