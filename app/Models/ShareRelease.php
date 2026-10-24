<?php

namespace App\Models;

use Illuminate\Database\Eloquent\Model;
use Illuminate\Database\Eloquent\Relations\BelongsTo;
use Illuminate\Support\Carbon;

/**
 * One release of a shared vehicle or save: one archive, whose url never changes (rule 25).
 *
 * @property int $id
 * @property int $share_id
 * @property string $version
 * @property 'stable'|'testing'|'dev' $status
 * @property string $path
 * @property int $size
 * @property string $sha256
 * @property string $folder
 * @property string $game_version
 * @property list<array{id: string, kind: string, min?: string, max?: string}> $dependencies
 * @property Carbon|null $taken_down_at
 * @property Carbon $created_at
 * @property Carbon $updated_at
 * @property-read Share $share
 */
class ShareRelease extends Model
{
    protected $guarded = [];

    /**
     * @return BelongsTo<Share, $this>
     */
    public function share(): BelongsTo
    {
        return $this->belongsTo(Share::class);
    }

    /**
     * The download url the release list names: on this site, so a takedown can answer 410 (rule 29).
     */
    public function url(): string
    {
        return route('shares.download', [
            'type' => $this->share->type,
            'id' => $this->share->listing_id,
            'version' => $this->version,
        ]);
    }

    /**
     * @return array<string, string>
     */
    protected function casts(): array
    {
        return ['size' => 'integer', 'dependencies' => 'array', 'taken_down_at' => 'datetime'];
    }
}
